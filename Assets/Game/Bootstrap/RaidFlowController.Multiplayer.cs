using System.Collections.Generic;
using RaidDemo.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的联机部分：主菜单 ↔ 联机界面 ↔ 房间界面 ↔ 战局。
    /// </summary>
    /// <remarks>
    /// <para><b>它在联机里扮演什么：</b>界面只负责"画"与"点"，会话只负责"协议与状态"，
    /// 这个文件负责把两者接起来，并且决定"什么时候切场景"。
    /// 场景切换放在这里而不是会话里，是因为只有装配层同时认识流程状态、界面与场景加载。</para>
    ///
    /// <para><b>屏幕的对应关系：</b></para>
    /// <list type="bullet">
    /// <item><description>未连接 / 连接中 / 登录中 → 联机界面（地址、昵称、口令、局域网扫描）；</description></item>
    /// <item><description>已登录（不在房间）→ 房间界面（创建 / 加入表单）；</description></item>
    /// <item><description>已在房间 → 房间界面（成员列表、房主开局）；</description></item>
    /// <item><description>战局中 → 两个界面都收起，由服务器通知加载地图。</description></item>
    /// </list>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        private MultiplayerMenuScreen m_MultiplayerScreen;
        private LobbyScreen m_LobbyScreen;
        private MultiplayerClientSession m_Session;

        /// <summary>传给房间界面的成员列表（复用同一份，避免每帧分配）。</summary>
        private readonly List<LobbyRoomMember> m_RoomMembers = new List<LobbyRoomMember>(LobbyLimits.MaxPlayers);

        /// <summary>局域网扫描的收尾时刻；-1 表示当前没有扫描。</summary>
        private float m_ScanDeadline = -1f;

        /// <summary>联机相关的界面当前是否处于显示状态（用于判断"断开后要不要回到联机界面"）。</summary>
        private bool m_MultiplayerUiActive;

        /// <summary>地址被隐藏时，地址输入框里显示的占位文本。</summary>
        private const string HiddenAddressPlaceholder = "已隐藏";

        /// <summary>启动器预填的服务器地址（真实值）；地址被隐藏时界面只显示占位符。</summary>
        private string m_HintedServerAddress;
        private int m_HintedServerPort;

        /// <summary>联机界面的地址框当前是否显示"已隐藏"占位符。</summary>
        /// <remarks>启动器对需要隐藏地址的更新源（云主机）传 <c>-hideserver</c>：
        /// 地址不上屏，但连接时仍使用它——玩家没有改过这一格，不该因为"看不见"而连不上。</remarks>
        private bool m_ServerAddressHidden;

        /// <summary>正在主动退出联机（点「返回主菜单」或暂停菜单退出）时为 true。</summary>
        /// <remarks>
        /// 断开连接会同步触发 <see cref="OnSessionChanged"/>，而那段逻辑看到"联机界面开着"
        /// 就会把服务器列表**再推回来**——于是「返回主菜单」看起来毫无反应（P4 用户反馈）。
        /// 用一个显式标记把"玩家主动退出"与"被动掉线"区分开。
        /// </remarks>
        private bool m_LeavingMultiplayer;

        /// <summary>
        /// 是否有任何"阻塞型界面"正在显示（主菜单 / 结算 / 联机 / 房间）。
        /// </summary>
        /// <remarks>
        /// 场景启动类用它决定"要不要锁光标、Esc 归谁"：这些界面都需要鼠标，
        /// 而它们不属于背包/商人那套已知面板，安全屋原来只认那几个，于是玩家一点输入框
        /// 光标就被重新锁住了（P4 用户反馈的"鼠标消失"）。
        /// </remarks>
        public bool IsBlockingScreenVisible =>
            State == FlowState.MainMenu || State == FlowState.Result || m_MultiplayerUiActive;

        /// <summary>局域网扫描窗口（秒）。直接取发现模块的常量，避免两处数值不一致。</summary>
        private const float ScanWindowSeconds = LanDiscoveryScanner.ScanWindowSeconds;

        /// <summary>创建联机界面与会话订阅。由 Awake 调用一次。</summary>
        private void InitializeMultiplayerFlow()
        {
            m_MultiplayerScreen = gameObject.AddComponent<MultiplayerMenuScreen>();
            m_MultiplayerScreen.Initialize(new MultiplayerMenuActions
            {
                Connect = OnMultiplayerConnect,
                Scan = OnMultiplayerScan,
                JoinFound = OnMultiplayerJoinFound,
                Back = ReturnToMainMenuFromMultiplayer,
            });

            m_LobbyScreen = gameObject.AddComponent<LobbyScreen>();
            m_LobbyScreen.Initialize(new LobbyRoomActions
            {
                CreateRoom = OnMultiplayerCreateRoom,
                JoinRoom = OnMultiplayerJoinRoom,
                StartRaid = OnMultiplayerStartRaid,
                LeaveRoom = OnMultiplayerLeaveRoom,
                Back = OnMultiplayerBackToServers,
            });
        }

        /// <summary>
        /// 主菜单 → 联机界面。
        /// </summary>
        /// <remarks>
        /// 这里把 <c>Time.timeScale</c> 恢复为 1：主菜单是"暂停"语义，而联机流程依赖帧推进
        /// （连接握手、超时判定都在 Update 里），停在暂停状态会表现为"一直连不上"。
        /// </remarks>
        public void ShowMultiplayerMenu()
        {
            HidePauseMenu();
            State = FlowState.MainMenu;
            Time.timeScale = 1f;
            m_MenuScreen.SetVisible(false);
            m_ResultScreen.SetVisible(false);
            m_LobbyScreen.SetVisible(false);

            ShowServersScreen(null, null);
            UnlockCursor();
            EnsureSessionSubscription();
        }

        /// <summary>每帧推进：会话订阅与局域网扫描收尾。</summary>
        private void Update()
        {
            EnsureSessionSubscription();
            TickScanWindow();
        }

        /// <summary>会话第一次出现时挂上事件订阅（会话可能在界面创建之后才建立）。</summary>
        private void EnsureSessionSubscription()
        {
            var session = MultiplayerClientSession.Current;
            if (session == null || session == m_Session)
            {
                return;
            }

            if (m_Session != null)
            {
                m_Session.Changed -= OnSessionChanged;
                m_Session.RaidStarting -= OnRaidStarting;
                m_Session.RaidEnding -= OnRaidEnding;
                m_Session.MoneyChanged -= OnMoneyChanged;
                m_Session.ResumedFromReconnect -= OnResumedFromReconnect;
            }

            m_Session = session;
            m_Session.Changed += OnSessionChanged;
            m_Session.RaidStarting += OnRaidStarting;
            m_Session.RaidEnding += OnRaidEnding;
            m_Session.MoneyChanged += OnMoneyChanged;
            m_Session.ResumedFromReconnect += OnResumedFromReconnect;

            // 会话可能在我们订阅之前就已经走到了某个阶段（例如命令行直接连接），
            // 因此订阅之后立刻按当前状态刷一次界面。
            OnSessionChanged();
        }

        /// <summary>
        /// 服务器下发了金币：贴到本地这份镜像上并刷新界面（P5）。
        /// </summary>
        /// <remarks>
        /// <para>联机时金币的权威在服务器：买卖、任务奖励、撤离结算都由它算并落库。
        /// 客户端这一份只用于画右上角的余额，下一次下发会覆盖它。</para>
        ///
        /// <para>不写单机存档：联机进度与单机存档是两套账（见 <c>SaveNow</c> 的隔离规则）。</para>
        /// </remarks>
        private void OnMoneyChanged(int money)
        {
            if (Progress == null)
            {
                return;
            }

            Progress.ApplyServerMoney(money);
        }

        /// <summary>点「连接」：建立会话并登录。</summary>
        private void OnMultiplayerConnect(string address, int port, string nickname, string passphrase)
        {
            // 地址框显示"已隐藏"占位符时，用启动器预填的真实地址连接——
            // 玩家没有改过这一格，他不该因为"看不见地址"而连不上。
            // 他一旦手动改写这一格（内容不再是占位符），就按他输入的内容走。
            if (m_ServerAddressHidden
                && string.Equals(address?.Trim(), HiddenAddressPlaceholder, System.StringComparison.Ordinal)
                && !string.IsNullOrEmpty(m_HintedServerAddress))
            {
                address = m_HintedServerAddress;
                port = m_HintedServerPort;
            }

            var session = MultiplayerClientSession.Ensure();
            EnsureSessionSubscription();
            session.AutoRoom = false;

            m_MultiplayerScreen.SetBusy(true);
            m_MultiplayerScreen.SetStatus($"正在连接 {address}:{port} …", false);

            if (!session.Connect(address, port, nickname, passphrase))
            {
                m_MultiplayerScreen.SetBusy(false);
                m_MultiplayerScreen.SetStatus(session.LastError, true);
            }
        }

        /// <summary>点「创建房间」。</summary>
        private void OnMultiplayerCreateRoom(string roomName, string password)
        {
            if (m_Session == null)
            {
                return;
            }

            m_LobbyScreen.SetBusy(true);
            m_LobbyScreen.SetStatus("正在创建房间…", false);
            m_Session.CreateRoom(roomName, password);
        }

        /// <summary>点「加入房间」。</summary>
        private void OnMultiplayerJoinRoom(string password)
        {
            if (m_Session == null)
            {
                return;
            }

            m_LobbyScreen.SetBusy(true);
            m_LobbyScreen.SetStatus("正在加入房间…", false);
            m_Session.JoinRoom(password);
        }

        /// <summary>房主点「开始战局」。</summary>
        private void OnMultiplayerStartRaid()
        {
            if (m_Session == null)
            {
                return;
            }

            m_LobbyScreen.SetBusy(true);
            m_LobbyScreen.SetStatus("正在开始战局…", false);
            m_Session.StartRaid();
        }

        /// <summary>点「离开房间」：回到联机界面（连接与登录保持）。</summary>
        private void OnMultiplayerLeaveRoom()
        {
            if (m_Session == null)
            {
                return;
            }

            m_Session.LeaveRoom();
        }

        /// <summary>
        /// 最终退出流程（返回主菜单 / 退出桌面）是否已经在进行。
        /// </summary>
        /// <remarks>等待服务器确认"离开房间"的几百毫秒里，玩家可能再点一次按钮——
        /// 没有这道闸就会启动第二条退出协程，出现重复断线与重复重载场景。</remarks>
        private bool m_ExitTransitionActive;

        /// <summary>等待"离开房间"确认的最长时间（秒）。</summary>
        private const float LeaveRoomWaitSeconds = 0.6f;

        /// <summary>
        /// 最终退出的统一入口：防止重复触发，并先把"离开房间"发出去再执行后续动作。
        /// </summary>
        /// <param name="continuation">离开确认（或超时）之后要执行的退出动作。</param>
        private void BeginExitTransition(System.Action continuation)
        {
            if (m_ExitTransitionActive)
            {
                return;
            }

            m_ExitTransitionActive = true;
            LeaveRoomThen(continuation);
        }

        /// <summary>
        /// 先向服务器发"离开房间"（可靠消息）并等确认或短超时，再执行后续动作。
        /// </summary>
        /// <param name="continuation">离开确认（或超时）之后要执行的动作。</param>
        /// <remarks>
        /// <para><b>为什么不能直接断开：</b>直接 Shutdown 会被服务器当作"意外掉线"，
        /// 进入 60 秒宽限——房间成员、战局进度和玩家的身体都留在服务器上；
        /// 他在宽限期内重进就会被"重连接管"回旧房间，表现为"战局进行中，进不去"
        /// （负责人反馈的云服务器问题）。主动退出必须先明确地说一句"我离开"。</para>
        ///
        /// <para><b>为什么要等：</b>LeaveRoom 走可靠通道，发送到送达需要一点时间；
        /// 发完立刻 Shutdown 有概率让它出不了网。超时后照常执行后续动作——
        /// 即使这一次没送到，服务器还有宽限逻辑兜底，不会卡住玩家。</para>
        /// </remarks>
        private void LeaveRoomThen(System.Action continuation)
        {
            if (m_Session != null && MultiplayerClientSession.IsActive && m_Session.SelfInRoom)
            {
                m_Session.LeaveRoom();
                StartCoroutine(LeaveRoomRoutine(continuation));
                return;
            }

            continuation();
        }

        /// <summary>等"离开房间"被服务器确认（阶段回到"已登录"）或超时。</summary>
        private System.Collections.IEnumerator LeaveRoomRoutine(System.Action continuation)
        {
            var deadline = Time.realtimeSinceStartup + LeaveRoomWaitSeconds;
            while (Time.realtimeSinceStartup < deadline
                   && m_Session != null
                   && m_Session.Phase != MultiplayerClientPhase.InLobby)
            {
                yield return null;
            }

            continuation();
        }

        /// <summary>
        /// 房间界面点「返回」：断开会话并回到联机界面（服务器列表）。
        /// </summary>
        /// <remarks>
        /// 断开而不是"保留连接回到列表"：联机界面上的「连接」是唯一的入口，
        /// 保留一条已建立的连接会让那个按钮处于"已经连上了"的哑火状态，玩家只能靠猜。
        /// 离开房间走显式的 LeaveRoom（而不是靠断线被当作退出）——
        /// 后者会进 60 秒宽限，玩家重新连接时被接管回旧房间。
        /// </remarks>
        private void OnMultiplayerBackToServers()
        {
            LeaveRoomThen(() =>
            {
                m_Session?.Disconnect();
                ShowServersScreen(null, "已返回服务器列表。");
            });
        }

        /// <summary>联机界面点「返回主菜单」：断开连接并回到主菜单。</summary>
        private void ReturnToMainMenuFromMultiplayer()
        {
            m_LeavingMultiplayer = true;
            m_MultiplayerUiActive = false;
            m_MultiplayerScreen?.SetVisible(false);
            m_LobbyScreen?.SetVisible(false);

            // 先"离开房间"再断开：直接断开会进 60 秒宽限，宽限期内重进会被接管回旧房间
            // （负责人反馈的"战局进行中，进不去"）。
            BeginExitTransition(() =>
            {
                m_Session?.Disconnect();

                // 与"暂停菜单 → 返回主菜单"走同一条路：改进程身份 + 重载安全屋。
                // 只切状态的话，安全屋身上还挂着已经断开的移动/容器链路，
                // 命令处理器也停在"只上行"的版本上——玩家会看到主菜单回来了，
                // 但人物不动、仓库点不动（同一类缺陷的另一个入口）。
                ClientMode.Deactivate();

                m_LeavingMultiplayer = false;

                HidePauseMenu();
                State = FlowState.MainMenu;
                Time.timeScale = 1f;
                SceneManager.LoadScene(GameScenes.SafeHouse);
            });
        }

        /// <summary>拆分"主机[:端口]"（用于把上次地址回填到界面）。</summary>
        private static void ParseAddress(string raw, out string address, out int port)
        {
            address = string.IsNullOrWhiteSpace(raw) ? "127.0.0.1" : raw.Trim();
            port = LaunchOptions.DefaultPort;

            var separator = address.LastIndexOf(':');
            if (separator <= 0)
            {
                return;
            }

            if (int.TryParse(address.Substring(separator + 1), out var parsed) && parsed > 0 && parsed <= 65535)
            {
                port = parsed;
                address = address.Substring(0, separator);
            }
        }
    }
}
