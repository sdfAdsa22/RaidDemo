using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机会话的大厅部分：请求上行、结果 / 房间状态 / 开局下行的处理。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>会话主体管生命周期（连接、阶段、退出），
    /// 这里管协议（发什么、收到什么、怎么改变阶段）。两者改动的原因不同，放一起会长成一大坨。</para>
    ///
    /// <para><b>注册时机：</b>消息处理器在"连上服务器"的那一刻注册，而不是在发起连接之前——
    /// 命名消息处理器必须挂在已经初始化的消息管理器上（P-20 的教训：
    /// 早于对方就绪的消息会被直接丢掉，而且没有任何报错）。</para>
    /// </remarks>
    public sealed partial class MultiplayerClientSession
    {
        /// <summary>收到"战局开始"（参数为要加载的地图场景名）。</summary>
        public event System.Action<string> RaidStarting;

        /// <summary>
        /// 收到"本局结束、回安全屋"（参数为要返回的场景名）。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="RaidStarting"/> 对称：装配层据此加载安全屋场景，
        /// 而世界内容（谁在屋里、站在哪）由服务器在同一个时刻重建（P4.5-b 的战后回屋循环）。
        /// 两条事件都定义在这里，因为它们的唯一来源就是本文件里的两条下行消息处理器。
        /// </remarks>
        public event System.Action<string> RaidEnding;

        /// <summary>注册大厅下行消息处理器。</summary>
        private void RegisterHandlers()
        {
            if (m_HandlersRegistered || m_Network == null || m_Network.CustomMessagingManager == null)
            {
                return;
            }

            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                LobbyChannel.ResultMessageName,
                OnLobbyResultReceived);
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                LobbyChannel.RoomStateMessageName,
                OnRoomStateReceived);
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                LobbyChannel.RaidStartMessageName,
                OnRaidStartReceived);
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                LobbyChannel.RaidEndMessageName,
                OnRaidEndReceived);

            m_HandlersRegistered = true;
        }

        /// <summary>退订大厅消息处理器（断线或销毁时调用）。</summary>
        private void CleanupHandlers()
        {
            if (!m_HandlersRegistered || m_Network == null)
            {
                return;
            }

            var messaging = m_Network.CustomMessagingManager;
            if (messaging != null)
            {
                messaging.UnregisterNamedMessageHandler(LobbyChannel.ResultMessageName);
                messaging.UnregisterNamedMessageHandler(LobbyChannel.RoomStateMessageName);
                messaging.UnregisterNamedMessageHandler(LobbyChannel.RaidStartMessageName);
                messaging.UnregisterNamedMessageHandler(LobbyChannel.RaidEndMessageName);
            }

            m_HandlersRegistered = false;
        }

        /// <summary>
        /// 验收辅助：自动进房（<c>-autoroom</c>）。
        /// </summary>
        /// <remarks>
        /// 先尝试创建；若服务器上已有房间（返回"房间已存在"），再改为加入——
        /// 这样"先启动的客户端当房主"不需要额外约定，脚本里谁先起来都可以。
        /// </remarks>
        private void TickAutoRoom()
        {
            if (!AutoRoom || m_AutoRoomRequested || Phase != MultiplayerClientPhase.InLobby)
            {
                return;
            }

            m_AutoRoomRequested = true;

            var roomName = ClientMode.Options != null ? ClientMode.Options.RoomName : "自动房间";
            Debug.Log($"[联机] 验收辅助 -autoroom：自动创建或加入房间「{roomName}」。");
            SendLobbyRequest(LobbyRequestKind.CreateRoom, roomName, AutoRoomPassword);
        }

        /// <summary>发一条大厅请求（上行）。</summary>
        /// <param name="kind">请求种类。</param>
        /// <param name="fieldA">字段 A（登录=昵称 / 创建=房间名）。</param>
        /// <param name="fieldB">字段 B（登录=口令或 token / 创建与加入=房间密码）。</param>
        private void SendLobbyRequest(LobbyRequestKind kind, string fieldA, string fieldB)
        {
            var messaging = m_Network != null ? m_Network.CustomMessagingManager : null;
            if (messaging == null || !IsConnected)
            {
                SetError("尚未连接到服务器。");
                return;
            }

            m_Sequence++;

            var message = new LobbyRequestMessage
            {
                Kind = (byte)kind,
                FieldA = fieldA ?? string.Empty,
                FieldB = fieldB ?? string.Empty,
                Sequence = m_Sequence,
            };

            using (var writer = new FastBufferWriter(192, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                messaging.SendNamedMessage(
                    LobbyChannel.RequestMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>收到一条请求结果。</summary>
        private void OnLobbyResultReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(LobbyResultMessage);
            reader.ReadValueSafe(out message);

            var kind = (LobbyRequestKind)message.Kind;
            var error = (LobbyError)message.Error;
            var detail = message.Detail.ToString();

            // 验收辅助：自动进房时"创建失败因为已有房间"不是错误，而是"改为加入"的信号。
            if (!message.Success && AutoRoom && kind == LobbyRequestKind.CreateRoom && error == LobbyError.RoomExists)
            {
                Debug.Log("[联机] 服务器上已有房间，改为加入（-autoroom）。");
                SendLobbyRequest(LobbyRequestKind.JoinRoom, string.Empty, AutoRoomPassword);
                return;
            }

            if (!message.Success)
            {
                SetError(string.IsNullOrEmpty(detail) ? $"操作失败（{error}）。" : detail);
                return;
            }

            LastError = string.Empty;

            switch (kind)
            {
                case LobbyRequestKind.Login:
                {
                    var token = message.Token.ToString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        ClientAccountStore.Save(Nickname, token, Address);
                    }

                    SetPhase(MultiplayerClientPhase.InLobby);
                    StatusText = string.IsNullOrEmpty(detail) ? "已登录" : detail;
                    RaiseChanged();
                    break;
                }

                case LobbyRequestKind.CreateRoom:
                case LobbyRequestKind.JoinRoom:
                    SetPhase(MultiplayerClientPhase.InRoom);
                    StatusText = string.IsNullOrEmpty(detail) ? "已在房间" : detail;
                    RaiseChanged();
                    break;

                case LobbyRequestKind.LeaveRoom:
                    ResetRoomState();
                    SetPhase(MultiplayerClientPhase.InLobby);
                    StatusText = "已离开房间";
                    RaiseChanged();
                    break;

                case LobbyRequestKind.StartRaid:
                    SetStatus("正在开始战局…");
                    break;
            }
        }

        /// <summary>收到房间状态广播。</summary>
        private void OnRoomStateReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(RoomStateMessage);
            reader.ReadValueSafe(out message);
            ApplyRoomState(in message);
        }

        /// <summary>收到开局通知。</summary>
        private void OnRaidStartReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(RaidStartMessage);
            reader.ReadValueSafe(out message);
            EnterRaid(message.MapSceneName.ToString());
        }

        /// <summary>收到"本局结束"通知（P4.5-b：全员回共享安全屋）。</summary>
        private void OnRaidEndReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(RaidEndMessage);
            reader.ReadValueSafe(out message);
            ReturnFromRaid(message.SceneName.ToString());
        }

        /// <summary>
        /// 应用一份房间状态。
        /// </summary>
        /// <param name="message">服务器广播的房间状态。</param>
        /// <remarks>
        /// <para>阶段以服务器为准，但有一个例外：<b>战局中不影响已经在战局里的自己</b>——
        /// 进战局的时机由开局消息决定（那一瞬间要加载地图场景），
        /// 房间状态只是"房间里有什么"的展示数据。</para>
        ///
        /// <para>另外这里不覆盖 <see cref="LastError"/>：广播随时可能来，
        /// 而玩家刚看到的错误提示不该被它抹掉。</para>
        /// </remarks>
        private void ApplyRoomState(in RoomStateMessage message)
        {
            RoomPhase = (LobbyPhase)message.Phase;
            RoomName = message.RoomName.ToString();
            RoomHasPassword = message.HasPassword;
            RoomHostClientId = message.HostClientId;

            m_RoomMembers.Clear();

            var count = Mathf.Clamp(message.MemberCount, 0, LobbyLimits.MaxPlayers);
            for (var i = 0; i < count; i++)
            {
                var entry = message.GetMember(i);
                var nickname = entry.Nickname.ToString();
                if (entry.ClientId == 0 && string.IsNullOrEmpty(nickname))
                {
                    continue;
                }

                m_RoomMembers.Add(new MultiplayerMemberView
                {
                    ClientId = entry.ClientId,
                    Nickname = nickname,
                    IsHost = entry.IsHost,
                });
            }

            // 阶段与自身处境对齐：
            // ① 自己在房间里、房间在等待 → 自己处于"房间中"；
            // ② 房间空了或自己不在名单里（被停止房间、被踢出）→ 退回"已登录"；
            // ③ 战局中且自己在名单里 → 保持现状（进图由开局消息驱动）。
            var inRoom = SelfInRoom;
            var waiting = RoomPhase == LobbyPhase.Waiting;

            if (waiting && inRoom && (Phase == MultiplayerClientPhase.InLobby || Phase == MultiplayerClientPhase.InRoom))
            {
                Phase = MultiplayerClientPhase.InRoom;
            }
            else if (!waiting && !inRoom
                     && (Phase == MultiplayerClientPhase.InRoom || Phase == MultiplayerClientPhase.InLobby))
            {
                Phase = MultiplayerClientPhase.InLobby;
            }

            RaiseChanged();
        }
    }
}
