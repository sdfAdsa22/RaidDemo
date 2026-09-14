using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>联机客户端所处的位置。</summary>
    public enum MultiplayerClientPhase
    {
        /// <summary>未连接。</summary>
        Offline = 0,

        /// <summary>已发起连接，等待服务器接受。</summary>
        Connecting = 1,

        /// <summary>已连接，正在登录。</summary>
        LoggingIn = 2,

        /// <summary>已登录，尚未进入房间。</summary>
        InLobby = 3,

        /// <summary>已在房间里等待开局。</summary>
        InRoom = 4,

        /// <summary>战局进行中。</summary>
        InRaid = 5,
    }

    /// <summary>房间成员（客户端侧视图）。</summary>
    public sealed class MultiplayerMemberView
    {
        /// <summary>成员编号。</summary>
        public int ClientId;

        /// <summary>昵称。</summary>
        public string Nickname = string.Empty;

        /// <summary>是否房主。</summary>
        public bool IsHost;
    }

    /// <summary>
    /// 联机客户端会话：连接、登录、建 / 加房、接收房间状态与开局。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么它必须跨场景存活：</b>大厅在安全屋场景里，战局是另一张场景。
    /// 连接如果跟着场景销毁，开局加载地图的那一刻连接就断了——所以网络管理器与本会话
    /// 挂在跨场景对象上，由它持有唯一的网络管理器，战局装配根只是"借用"它。</para>
    ///
    /// <para><b>它不做什么：</b>不碰战局内容（移动预测、快照、容器、AI 表现都在
    /// <c>SceneBootstrap.Multiplayer</c> 里）。本类只维护"我在哪、房间里都有谁、开局了没有"，
    /// 并把变化广播给界面。</para>
    ///
    /// <para><b>与服务端的约定：</b>所有请求走上行通道，结果点对点回来，房间状态与开局是广播。
    /// 请求里只带意图与输入文本，没有任何权威数据。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class MultiplayerClientSession : MonoBehaviour
    {
        /// <summary>连不上服务器的判定时间（秒）。</summary>
        private const float ConnectTimeoutSeconds = 8f;

        /// <summary>当前会话（进程内唯一）。</summary>
        public static MultiplayerClientSession Current { get; private set; }

        /// <summary>是否存在活动的联机会话。</summary>
        public static bool IsActive => Current != null;

        /// <summary>网络管理器（战局装配根会借用它）。</summary>
        public NetworkManager Network => m_Network;

        /// <summary>当前阶段。</summary>
        public MultiplayerClientPhase Phase { get; private set; } = MultiplayerClientPhase.Offline;

        /// <summary>是否已连上服务器（网络层）。</summary>
        public bool IsConnected => m_Network != null && m_Network.IsConnectedClient;

        /// <summary>本机在服务器上的玩家编号；未连接为 -1。</summary>
        public int LocalClientId { get; private set; } = -1;

        /// <summary>当前使用的地址（主机[:端口]）。</summary>
        public string Address { get; private set; } = string.Empty;

        /// <summary>本次登录使用的昵称。</summary>
        public string Nickname { get; private set; } = string.Empty;

        /// <summary>最近一次失败原因（界面直接显示）；成功时为空。</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>进行中的状态说明（例如"正在连接…"）。</summary>
        public string StatusText { get; private set; } = string.Empty;

        /// <summary>房间阶段（来自服务器广播）。</summary>
        public LobbyPhase RoomPhase { get; private set; } = LobbyPhase.Empty;

        /// <summary>房间名；空闲时为空。</summary>
        public string RoomName { get; private set; } = string.Empty;

        /// <summary>房间是否设了密码。</summary>
        public bool RoomHasPassword { get; private set; }

        /// <summary>房主编号；无房间时为 -1。</summary>
        public int RoomHostClientId { get; private set; } = -1;

        /// <summary>房间成员列表。</summary>
        public IReadOnlyList<MultiplayerMemberView> RoomMembers => m_RoomMembers;

        /// <summary>自己是不是房主。</summary>
        public bool IsHost => LocalClientId >= 0 && RoomHostClientId == LocalClientId;

        /// <summary>自己是否在房间里。</summary>
        public bool SelfInRoom
        {
            get
            {
                for (var i = 0; i < m_RoomMembers.Count; i++)
                {
                    if (m_RoomMembers[i].ClientId == LocalClientId)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>验收辅助：连接后自动登录并自动创建 / 加入房间（<c>-autoroom</c>）。</summary>
        public bool AutoRoom { get; set; }

        /// <summary>验收辅助：自动加入时使用的房间密码（<c>-roompass</c>）。</summary>
        public string AutoRoomPassword { get; set; } = string.Empty;

        /// <summary>任一可见状态变化（界面据此重绘）。</summary>
        public event Action Changed;

        private readonly List<MultiplayerMemberView> m_RoomMembers = new List<MultiplayerMemberView>();

        private NetworkManager m_Network;
        private UnityTransport m_Transport;
        private string m_PendingPassphrase = string.Empty;
        private float m_ConnectDeadline = -1f;
        private uint m_Sequence;
        private bool m_HandlersRegistered;
        private bool m_AutoRoomRequested;
        private bool m_RaidStartSeen;

        /// <summary>取得（必要时创建）唯一的联机会话。</summary>
        /// <remarks>宿主对象跨场景存活：大厅在安全屋、战局在地图场景，连接必须活过这次切换。</remarks>
        public static MultiplayerClientSession Ensure()
        {
            if (Current != null)
            {
                return Current;
            }

            var host = new GameObject("MultiplayerClient");
            Current = host.AddComponent<MultiplayerClientSession>();
            return Current;
        }

        /// <summary>大厅消息处理器是否已注册（战局装配据此避免重复注册）。</summary>
        internal bool HandlersRegistered => m_HandlersRegistered;

        private void Awake()
        {
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>每帧推进：连接超时与自动进房。</summary>
        private void Update()
        {
            // 客户端同样要看门狗：客户端卡顿会让*服务器*判定它掉线（P-48 的排查教训）。
            FrameStallWatchdog.Tick("客户端");

            TickConnectTimeout();
            TickAutoRoom();
        }

        /// <summary>
        /// 连接到服务器并登录。
        /// </summary>
        /// <param name="address">服务器地址（主机[:端口]）。</param>
        /// <param name="nickname">昵称。</param>
        /// <param name="passphrase">口令（本地若存有该昵称的 token，则优先用 token）。</param>
        /// <returns>参数合法并已发起连接时返回 true。</returns>
        /// <remarks>地址与端口分开的重载供界面直接使用（界面把主机与端口分成两个输入框）。</remarks>
        public bool Connect(string address, int port, string nickname, string passphrase)
        {
            var host = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            var text = port > 0 && port <= 65535 ? $"{host}:{port}" : host;
            return Connect(text, nickname, passphrase);
        }

        /// <summary>
        /// 连接到服务器并登录。
        /// </summary>
        /// <param name="address">服务器地址（主机[:端口]）。</param>
        /// <param name="nickname">昵称。</param>
        /// <param name="passphrase">口令（本地若存有该昵称的 token，则优先用 token）。</param>
        /// <returns>参数合法并已发起连接时返回 true。</returns>
        public bool Connect(string address, string nickname, string passphrase)
        {
            if (IsConnected)
            {
                SetError("已经连接到服务器了。");
                return false;
            }

            if (!LobbyLimits.IsValidNickname(nickname))
            {
                SetError($"昵称需要 1~{LobbyLimits.MaxNicknameLength} 个字符，且不能包含竖线。");
                return false;
            }

            if (!LobbyLimits.IsValidPassphrase(passphrase))
            {
                SetError($"口令需要 {LobbyLimits.MinPassphraseDigits}~{LobbyLimits.MaxPassphraseDigits} 位数字。");
                return false;
            }

            var (host, port) = SplitAddress(address, out var parsed);
            if (!parsed)
            {
                SetError($"服务器地址不合法：「{address}」。格式为 主机 或 主机:端口。");
                return false;
            }

            Address = string.IsNullOrWhiteSpace(address) ? host : address.Trim();
            Nickname = nickname.Trim();
            m_PendingPassphrase = passphrase;
            m_AutoRoomRequested = false;
            m_RaidStartSeen = false;

            CreateNetworkManager();
            m_Transport.SetConnectionData(host, port);

            // 与服务器共用同一份配置工厂：任何一处差异都会让 NGO 在握手阶段直接断开（P-11）。
            m_Network.NetworkConfig = NetworkConfigFactory.Create(m_Transport);

            if (!m_Network.StartClient())
            {
                SetPhase(MultiplayerClientPhase.Offline);
                SetError($"无法发起连接：{host}:{port}。");
                return false;
            }

            m_ConnectDeadline = Time.realtimeSinceStartup + ConnectTimeoutSeconds;
            SetPhase(MultiplayerClientPhase.Connecting);
            StatusText = $"正在连接 {host}:{port} …";
            Debug.Log($"[联机] 正在连接 {host}:{port}（昵称「{Nickname}」）。");
            return true;
        }

        /// <summary>断开连接并回到未连接状态。</summary>
        public void Disconnect()
        {
            if (m_Network != null && m_Network.IsListening)
            {
                m_Network.Shutdown();
            }

            CleanupHandlers();
            ResetRoomState();
            LocalClientId = -1;
            m_ConnectDeadline = -1f;
            m_RaidStartSeen = false;
            SetPhase(MultiplayerClientPhase.Offline);
            StatusText = string.Empty;
        }

        /// <summary>清空提示（界面切换时用）。</summary>
        public void ClearNotice()
        {
            LastError = string.Empty;
            StatusText = string.Empty;
            RaiseChanged();
        }

        /// <summary>建立网络管理器与客户端回调（只在第一次连接时建）。</summary>
        private void CreateNetworkManager()
        {
            if (m_Network != null)
            {
                return;
            }

            m_Network = gameObject.AddComponent<NetworkManager>();
            m_Transport = gameObject.AddComponent<UnityTransport>();

            // 诊断联机问题时客户端侧的证据同样重要（与服务器 -logLevel 对称）。
            var verbose = ClientMode.Options != null
                          && ClientMode.Options.MinimumLogLevel == RaidDemo.Kernel.LogLevel.Verbose;
            m_Network.LogLevel = verbose ? Unity.Netcode.LogLevel.Developer : Unity.Netcode.LogLevel.Normal;

            m_Network.OnClientConnectedCallback += OnClientConnectedToServer;
            m_Network.OnClientDisconnectCallback += OnClientDisconnectedFromServer;
            m_Network.OnClientStopped += OnClientStopped;
        }

        /// <summary>连上服务器：注册处理器并立刻登录。</summary>
        private void OnClientConnectedToServer(ulong clientId)
        {
            LocalClientId = (int)clientId;
            m_ConnectDeadline = -1f;
            RegisterHandlers();
            SetPhase(MultiplayerClientPhase.LoggingIn);
            StatusText = "正在登录…";

            Debug.Log($"[联机] 已连接服务器，玩家编号 {LocalClientId}，开始登录。");

            // 有本地 token 就直接用它：这是"之后自动登录"路径。
            var secret = ClientAccountStore.TryGetToken(Nickname, out var token) ? token : m_PendingPassphrase;
            SendLobbyRequest(LobbyRequestKind.Login, Nickname, secret);
        }

        /// <summary>与服务器断开：清干净房间状态并通知界面。</summary>
        private void OnClientDisconnectedFromServer(ulong clientId)
        {
            CleanupHandlers();
            ResetRoomState();
            LocalClientId = -1;
            SetPhase(MultiplayerClientPhase.Offline);

            // 主动断开（Disconnect）与意外断开走同一条回调；主动断开时不该再写"连接失败"。
            if (!string.IsNullOrEmpty(LastError))
            {
                RaiseChanged();
                return;
            }

            SetError("与服务器断开连接。");
        }

        /// <summary>网络管理器停止（客户端侧收尾）。</summary>
        private void OnClientStopped(bool wasHost)
        {
            CleanupHandlers();
        }

        /// <summary>连接超时：服务器没起、端口不对或被防火墙拦截时给一句人话。</summary>
        private void TickConnectTimeout()
        {
            if (m_ConnectDeadline < 0f || Phase != MultiplayerClientPhase.Connecting)
            {
                return;
            }

            if (Time.realtimeSinceStartup < m_ConnectDeadline)
            {
                return;
            }

            m_ConnectDeadline = -1f;

            if (m_Network != null && m_Network.IsListening)
            {
                m_Network.Shutdown();
            }

            SetPhase(MultiplayerClientPhase.Offline);
            SetError($"连接超时：{Address}（确认服务器已启动、地址端口正确、防火墙放行 UDP）。");
        }

        /// <summary>房间状态复位。</summary>
        private void ResetRoomState()
        {
            m_RoomMembers.Clear();
            RoomPhase = LobbyPhase.Empty;
            RoomName = string.Empty;
            RoomHasPassword = false;
            RoomHostClientId = -1;
        }

    }
}
