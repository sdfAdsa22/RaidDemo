using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机会话的连接生命周期：发起连接、连上之后的登录、断开时的分流、握手超时。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>会话主体（<c>MultiplayerClientSession.cs</c>）管的是
    /// "我是谁、我在哪、房间里都有谁"这份状态；本文件管的是"网络连接本身在什么阶段"。
    /// 两者的改动原因不同：界面会跟着状态字段变，而连接时序只在网络行为变化时才动。</para>
    ///
    /// <para><b>P-51 之后的职责划分：</b>"断开之后要不要自动重连"的判断发生在这里
    /// （<see cref="OnClientDisconnectedFromServer"/>），真正的重连调度与窗口控制
    /// 在 <c>MultiplayerClientSession.Reconnect.cs</c>。</para>
    /// </remarks>
    public sealed partial class MultiplayerClientSession
    {
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
            return BeginConnect(address, nickname, passphrase, isReconnect: false);
        }

        /// <summary>
        /// 连接的统一入口：首次连接与自动重连（P-51）走同一条路径。
        /// </summary>
        /// <param name="address">服务器地址（主机[:端口]）。</param>
        /// <param name="nickname">昵称。</param>
        /// <param name="passphrase">口令（本地若存有该昵称的 token，则优先用 token）。</param>
        /// <param name="isReconnect">
        /// 是否来自自动重连。两者的网络动作完全一致，差别只在"发起失败"时：
        /// 重连失败回到"重连中"等下一次尝试，首次连接失败回到"未连接"并给出错误。
        /// </param>
        /// <returns>参数合法并已发起连接时返回 true。</returns>
        private bool BeginConnect(string address, string nickname, string passphrase, bool isReconnect)
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

            // 新的一次连接意图：清掉上一条断开的"主动"标记，否则重连会被误判成玩家主动退出。
            m_IntentionalDisconnect = false;

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
                if (isReconnect)
                {
                    // 重连不是终局：回到"重连中"，由 TickReconnect 安排下一次尝试。
                    // 这里刻意不写 LastError：重试期间界面不该反复闪错误红字。
                    Phase = MultiplayerClientPhase.Reconnecting;
                    StatusText = $"暂时无法连接 {host}:{port}，稍后重试…";
                    RaiseChanged();
                }
                else
                {
                    SetPhase(MultiplayerClientPhase.Offline);
                    SetError($"无法发起连接：{host}:{port}。");
                }

                return false;
            }

            m_ConnectDeadline = Time.realtimeSinceStartup + ConnectTimeoutSeconds;
            SetPhase(MultiplayerClientPhase.Connecting);
            StatusText = $"正在连接 {host}:{port} …";
            // 版本标识一起打出来：进不去房间时，"我这端认为自己是哪一版"是排查的第一条线索。
            Debug.Log($"[联机] 正在连接 {host}:{port}（昵称「{Nickname}」，版本 {BuildIdentity.Current}）。");
            return true;
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

            // P-51：重连的连接建立之后，给"登录结果"一个较短的等待窗口——
            // 服务器可能正在重建传输层，把这条刚建立的连接再次断掉，不能无限等下去。
            if (m_Reconnecting)
            {
                m_ReconnectLoginDeadline = Time.realtimeSinceStartup + ReconnectLoginTimeoutSeconds;
            }

            Debug.Log($"[联机] 已连接服务器，玩家编号 {LocalClientId}，开始登录。");

            // 有本地 token 就直接用它：这是"之后自动登录"路径。
            var secret = ClientAccountStore.TryGetToken(Nickname, out var token) ? token : m_PendingPassphrase;
            SendLobbyRequest(LobbyRequestKind.Login, Nickname, secret);
        }

        /// <summary>与服务器断开：清干净房间状态并通知界面。</summary>
        private void OnClientDisconnectedFromServer(ulong clientId)
        {
            // 断开前的阶段要在清理之前读：它决定"这次断开值不值得自动重连"。
            // 只有进过会话（登录中 / 已登录 / 房间中 / 战局中）的人才有东西可恢复；
            // "第一次连接就失败"要给明确错误，而不是进重连循环。
            var wasInSession = Phase == MultiplayerClientPhase.LoggingIn
                               || Phase == MultiplayerClientPhase.InLobby
                               || Phase == MultiplayerClientPhase.InRoom
                               || Phase == MultiplayerClientPhase.InRaid;

            CleanupHandlers();
            ResetRoomState();
            LocalClientId = -1;

            // 重连尝试自身的失败（握手后被断开等）：交给重连流程安排下一次，
            // 不进入"断开"的终局处理。
            if (m_Reconnecting)
            {
                OnReconnectAttemptFailed();
                return;
            }

            SetPhase(MultiplayerClientPhase.Offline);

            // P-51：意外掉线且曾经进入过会话 → 自动重连（与服务器的传输层自愈配套）。
            if (wasInSession && !m_IntentionalDisconnect && TryBeginAutoReconnect())
            {
                return;
            }

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

            ShutdownNetworkQuietly();

            if (m_Reconnecting)
            {
                // 重连尝试的握手超时：不是终局错误，让重连流程继续安排下一次。
                OnReconnectAttemptFailed();
                return;
            }

            SetPhase(MultiplayerClientPhase.Offline);
            SetError($"连接超时：{Address}（确认服务器已启动、地址端口正确、防火墙放行 UDP）。");
        }
    }
}
