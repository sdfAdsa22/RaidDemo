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
        /// 房间界面点「返回」：断开会话并回到联机界面（服务器列表）。
        /// </summary>
        /// <remarks>
        /// 断开而不是"保留连接回到列表"：联机界面上的「连接」是唯一的入口，
        /// 保留一条已建立的连接会让那个按钮处于"已经连上了"的哑火状态，玩家只能靠猜。
        /// 服务器侧会把这次断开当作退出房间处理。
        /// </remarks>
        private void OnMultiplayerBackToServers()
        {
            m_Session?.Disconnect();
            ShowServersScreen(null, "已返回服务器列表。");
        }

        /// <summary>联机界面点「返回主菜单」：断开连接并回到主菜单。</summary>
        private void ReturnToMainMenuFromMultiplayer()
        {
            m_LeavingMultiplayer = true;
            m_MultiplayerUiActive = false;
            m_MultiplayerScreen?.SetVisible(false);
            m_LobbyScreen?.SetVisible(false);

            m_Session?.Disconnect();
            ShowMainMenu();

            m_LeavingMultiplayer = false;
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
