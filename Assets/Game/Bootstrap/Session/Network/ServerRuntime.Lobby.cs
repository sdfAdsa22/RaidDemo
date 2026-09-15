using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的大厅部分：连接登记、请求分发、结果与状态的下行。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么"连上服务器"和"进入战局"被拆开：</b>P0~P3.5 里客户端一连上就被塞进战局世界，
    /// 那适合自动化验证，不适合真正的联机流程——玩家要先选服务器、建号、进房间、等队友，
    /// 这些阶段里地图上不该有他这个人。因此连接的语义变成"接入服务"，
    /// 只有房间开局才把成员放进权威世界（见 <c>ServerRuntime.Lobby.Raid.cs</c>）。</para>
    ///
    /// <para><b>权威边界没有变化：</b>谁是房主、能不能开局、房间满没满、战局里的一切判定，
    /// 全部在服务器。客户端只发意图并显示结果。请求的具体处理在
    /// <c>ServerRuntime.Lobby.Requests.cs</c>。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>一名已连接客户端的登录态。</summary>
        private sealed class LobbyClient
        {
            /// <summary>连接编号（同时也是玩家编号）。</summary>
            public int ClientId;

            /// <summary>登录后的昵称；未登录为 null。</summary>
            public string Nickname;

            /// <summary>是否已通过登录。未登录的连接只能发登录请求。</summary>
            public bool LoggedIn;

            /// <summary>
            /// 掉线宽限的截止时刻（<c>Time.realtimeSinceStartup</c>）；&lt;= 0 表示连接正常。
            /// </summary>
            /// <remarks>
            /// <para><b>为什么要有它（P5 / 排障手册 P-48）：</b>以前的实现是"连接一断就把人从
            /// 房间与世界里清掉"，而实测发现那一刻会把**其余玩家的上行链路**一起打断
            /// （服务器收不到他们的包，10 秒后按协议超时把他们也踢掉，表现为"队友掉线，房间解散"）。</para>
            ///
            /// <para>现在改为：断线的人保留在名册与世界里的位置，等宽限到期才真正清场；
            /// 这段时间里他可以重连回来接管原来的位置与背包。</para>
            /// </remarks>
            public float GraceDeadline;

            /// <summary>是否处于掉线宽限中。</summary>
            public bool InGrace
            {
                get { return GraceDeadline > 0f; }
            }
        }

        private readonly Dictionary<int, LobbyClient> m_LobbyClients = new Dictionary<int, LobbyClient>();

        /// <summary>房间状态机（纯逻辑，规则有单元测试钉着）。</summary>
        private readonly LobbyRoom m_Room = new LobbyRoom();

        private ServerIdentityStore m_Identities;

        /// <summary>自动开局的截止时刻（验收辅助 <c>-autostart</c>）；-1 表示未安排。</summary>
        private float m_AutoStartDeadline = -1f;

        /// <summary>本局开始时刻；未在战局中时为 -1。</summary>
        private float m_RaidStartedAt = -1f;

        /// <summary>房间（状态页与局域网发现都要读它）。</summary>
        internal LobbyRoom Room => m_Room;

        /// <summary>建立大厅：账号库、连接事件、请求通道。</summary>
        private void InitializeLobby()
        {
            m_Identities = new ServerIdentityStore(m_Options.SaveDirectory);

            // 连接 / 断开事件只订阅一次：NGO 关闭会话时重建的是消息处理器（CustomMessagingManager），
            // 而 NetworkManager 上的这两条事件订阅是活下来的（见 ServerRuntime.TransportWatchdog）。
            // 反过来，如果在这里"重建时也重新订阅一遍"，同一条事件会被处理两次。
            m_Network.OnClientConnectedCallback += OnLobbyClientConnected;
            m_Network.OnClientDisconnectCallback += OnLobbyClientDisconnected;

            RegisterLobbyMessageHandlers();

            m_Session.Log.Info(
                $"[服务器] 大厅已就绪：默认房间名「{m_Options.RoomName}」，等待第一个客户端创建房间。");
            // 版本标识是握手判定的依据，启动时就打出来：版本不一致时最需要的两行日志
            // 就是"两端各自认为自己是哪一版"，没有它只能靠猜（M10 第 13.1 节）。
            m_Session.Log.Info($"[服务器] 本机版本标识：{BuildIdentity.Current}（握手只比对本体版本段）");
            m_Session.Log.Info(
                $"[服务器] 账号库已就绪：{m_Identities.AccountCount} 个账号（{m_Identities.FilePath}）。");

            // 状态页与局域网发现：两个都不参与游戏规则，起不来只降级。
            InitializeServerIntegrations();
        }

        /// <summary>
        /// 注册大厅的命名消息处理器。
        /// </summary>
        /// <remarks>首次启动与传输层重建（P-51）后都要调用：NGO 每次启动会话都会新建一个
        /// <c>CustomMessagingManager</c>，旧的处理器不会跟过来。</remarks>
        private void RegisterLobbyMessageHandlers()
        {
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                LobbyChannel.RequestMessageName,
                OnLobbyRequestReceived);
        }

        /// <summary>断开大厅的事件与通道，丢弃连接表。</summary>
        private void ShutdownLobby()
        {
            ShutdownServerIntegrations();

            if (m_Network != null)
            {
                m_Network.OnClientConnectedCallback -= OnLobbyClientConnected;
                m_Network.OnClientDisconnectCallback -= OnLobbyClientDisconnected;
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    LobbyChannel.RequestMessageName);
            }

            m_LobbyClients.Clear();
            m_AutoStartDeadline = -1f;
        }

        /// <summary>收到一条大厅请求。</summary>
        private void OnLobbyRequestReceived(ulong senderId, FastBufferReader reader)
        {
            // 用消息自带的读取入口而不是整体 ReadValueSafe：尾部字段（版本标识）在老客户端上不存在，
            // 整体读取会因缓冲区不足抛异常，表现为"这位玩家的请求服务器毫无反应"（M10 第 13.1 节）。
            var message = LobbyRequestMessage.Read(reader);
            HandleLobbyRequest((int)senderId, in message);
        }

        /// <summary>按请求种类分发。</summary>
        private void HandleLobbyRequest(int clientId, in LobbyRequestMessage message)
        {
            if (!m_LobbyClients.TryGetValue(clientId, out var client))
            {
                // 极端时序（连接回调晚于消息）下补登记，而不是把请求丢掉：
                // 丢掉会表现为"点了登录没反应"，且日志里什么都没有。
                client = new LobbyClient { ClientId = clientId };
                m_LobbyClients[clientId] = client;
            }

            var kind = (LobbyRequestKind)message.Kind;
            var fieldA = message.FieldA.ToString();
            var fieldB = message.FieldB.ToString();
            var buildId = message.BuildId.ToString();

            switch (kind)
            {
                case LobbyRequestKind.Login:
                    HandleLobbyLogin(client, fieldA, fieldB);
                    break;

                case LobbyRequestKind.CreateRoom:
                    HandleLobbyCreateRoom(client, fieldA, fieldB, buildId);
                    break;

                case LobbyRequestKind.JoinRoom:
                    HandleLobbyJoinRoom(client, fieldB, buildId);
                    break;

                case LobbyRequestKind.StartRaid:
                    // 字段 A 携带房主选择的地图场景名（P4.5-b：出口选图之后才开局）。
                    HandleLobbyStartRaid(client, fieldA);
                    break;

                case LobbyRequestKind.LeaveRoom:
                    HandleLobbyLeaveRoom(client);
                    break;

                default:
                    m_Session?.Log.Warning($"[服务器] 未知的大厅请求种类 {(byte)message.Kind}（来自 {clientId}）。");
                    break;
            }
        }

        /// <summary>回一条请求结果（点对点）。</summary>
        private void SendLobbyResult(
            int clientId,
            LobbyRequestKind kind,
            bool success,
            LobbyError error,
            string detail,
            string token = null)
        {
            var manager = m_Network;
            var target = (ulong)clientId;
            if (manager?.CustomMessagingManager == null || !IsClientConnected(target))
            {
                return;
            }

            var message = new LobbyResultMessage
            {
                Kind = (byte)kind,
                Success = success,
                Error = (byte)error,
                Detail = detail ?? string.Empty,
                Token = token ?? string.Empty,
            };

            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(
                    LobbyChannel.ResultMessageName,
                    target,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>把房间状态广播给所有已连接客户端（含未登录者——他们也要看到房间存不存在）。</summary>
        private void BroadcastRoomState()
        {
            var manager = m_Network;
            if (manager?.CustomMessagingManager == null || manager.ConnectedClientsIds.Count == 0)
            {
                return;
            }

            var message = m_Room.ToMessage();

            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessageToAll(
                    LobbyChannel.RoomStateMessageName,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>把房间状态点对点发给一个客户端（登录成功时用，避开处理器注册竞态）。</summary>
        private void SendRoomStateTo(int clientId)
        {
            var manager = m_Network;
            var target = (ulong)clientId;
            if (manager?.CustomMessagingManager == null || !IsClientConnected(target))
            {
                return;
            }

            var message = m_Room.ToMessage();

            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(
                    LobbyChannel.RoomStateMessageName,
                    target,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>该连接是否仍在服务器上。</summary>
        private bool IsClientConnected(ulong clientId)
        {
            var ids = m_Network?.ConnectedClientsIds;
            if (ids == null)
            {
                return false;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == clientId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>登录失败原因的中文说明（正常路径下用账号库给的 detail，这里只兜底）。</summary>
        private static string DescribeLoginError(LobbyError error)
        {
            switch (error)
            {
                case LobbyError.BadNickname:
                    return $"昵称需要 1~{LobbyLimits.MaxNicknameLength} 个字符，且不能包含竖线或控制字符。";
                case LobbyError.BadPasswordFormat:
                    return $"口令需要 {LobbyLimits.MinPassphraseDigits}~{LobbyLimits.MaxPassphraseDigits} 位数字。";
                case LobbyError.NicknameTaken:
                    return "该昵称已被注册，口令不正确；请换一个昵称或输入正确口令。";
                case LobbyError.NicknameOnline:
                    return "该昵称已在服务器上游戏中。";
                default:
                    return "登录失败。";
            }
        }

        /// <summary>房间操作失败原因的中文说明。</summary>
        private static string DescribeRoomError(LobbyError error)
        {
            switch (error)
            {
                case LobbyError.NotLoggedIn:
                    return "请先登录。";
                case LobbyError.RoomExists:
                    return "服务器上已经有房间了（一台服务器一个房间），可以直接加入。";
                case LobbyError.RoomNotFound:
                    return "服务器上还没有房间，请先创建。";
                case LobbyError.RoomFull:
                    return $"房间已满（上限 {LobbyLimits.MaxPlayers} 人）。";
                case LobbyError.WrongPassword:
                    return "房间密码不正确。";
                case LobbyError.RaidRunning:
                    return "这一局已经开始，无法中途加入。";
                case LobbyError.AlreadyInRoom:
                    return "你已经在房间里了。";
                case LobbyError.NotInRoom:
                    return "你不在任何房间里。";
                case LobbyError.NotHost:
                    return "只有房主可以开始战局。";
                case LobbyError.StartRejected:
                    return "当前阶段不能开始战局。";
                case LobbyError.BadRoomName:
                    return $"房间名需要 1~{LobbyLimits.MaxRoomNameLength} 个字符。";
                case LobbyError.BadPasswordFormat:
                    return $"房间密码需要留空或 {LobbyLimits.RoomPasswordDigits} 位数字。";
                case LobbyError.VersionMismatch:
                    // 具体文案由握手规则给出（含两个版本号），这里只是错误码的兜底说明。
                    return "客户端版本与服务器不一致，请更新客户端。";
                default:
                    return "操作失败。";
            }
        }
    }
}
