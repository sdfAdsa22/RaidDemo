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

        /// <summary>连接意外断开，正在自动重连（P-51 的客户端半边）。</summary>
        Reconnecting = 6,
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

        /// <summary>
        /// 是否存在活动的联机会话。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么要排除"已主动断开"的会话（AR-06）：</b>退出联机时
        /// <see cref="Disconnect"/> 只关了网络与房间状态，而 <see cref="Current"/> 仍然非空
        /// （只有 <c>OnDestroy</c> 才会清空）。于是"返回主菜单"重载安全屋后，
        /// <c>SafeHouseBootstrap.IsMultiplayerProcess</c> 依旧为真，安全屋按联机进程装配：
        /// 不显示主菜单、直接进入安全屋，Esc 只能弹出暂停菜单。</para>
        /// <para>现在把"主动断开"记成终态：它一旦置位，本会话就不再算活动会话，
        /// 重载后的安全屋会走"显示主菜单"那条分支。</para>
        /// </remarks>
        public static bool IsActive => Current != null && !Current.m_Disconnected;

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

        /// <summary>
        /// 是否已被玩家主动断开（终态）。
        /// </summary>
        /// <remarks>
        /// 与"连接意外断开"区分：后者会走自动重连（<c>Reconnecting</c> 阶段），
        /// 本标记只由 <see cref="Disconnect"/> 置位，且只能通过销毁重建来清除。
        /// </remarks>
        private bool m_Disconnected;

        /// <summary>本会话是否已主动断开（供界面与测试读取）。</summary>
        public bool IsDisconnected => m_Disconnected;

        /// <summary>取得（必要时创建）唯一的联机会话。</summary>
        /// <remarks>宿主对象跨场景存活：大厅在安全屋、战局在地图场景，连接必须活过这次切换。</remarks>
        public static MultiplayerClientSession Ensure()
        {
            if (Current != null)
            {
                if (!Current.m_Disconnected)
                {
                    return Current;
                }

                // 旧会话已主动断开：它不是可复用的活动会话，销毁后重建，
                // 否则"退出联机 → 再次联机"会拿到一个已死掉的会话。
                UnityEngine.Object.Destroy(Current.gameObject);
                Current = null;
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
            TickReconnect();
            TickAutoRoom();
        }

        /// <summary>断开连接并回到未连接状态。</summary>
        public void Disconnect()
        {
            // 玩家主动退出：取消（并禁止）自动重连。
            MarkIntentionalDisconnect();

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

            // 终态标记必须最后置位：上面的清理过程里还会读 Phase / 状态，
            // 提前置位会让"断开中"的半成品状态被当成已断开。
            m_Disconnected = true;
        }

        /// <summary>清空提示（界面切换时用）。</summary>
        public void ClearNotice()
        {
            LastError = string.Empty;
            StatusText = string.Empty;
            RaiseChanged();
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
