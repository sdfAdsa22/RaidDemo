using System;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的大厅请求处理：登录、建 / 加房、开局、离开。
    /// </summary>
    /// <remarks>
    /// <para>每个处理器都是"校验 → 调用状态机 → 回结果 / 广播"这三步。规则本身在
    /// <see cref="LobbyRoom"/> 与 <see cref="ServerIdentityStore"/> 里，
    /// 这里只负责把它们串起来并翻译成人话。</para>
    ///
    /// <para><b>失败也要回消息</b>：客户端界面的每一句提示都来自这里的 detail，
    /// 只记服务器日志不回消息的话，玩家看到的是"点了没反应"。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>登录（不存在则建号），成功后把房间状态点对点发给他。</summary>
        private void HandleLobbyLogin(LobbyClient client, string nickname, string secret)
        {
            if (client.LoggedIn)
            {
                SendLobbyResult(client.ClientId, LobbyRequestKind.Login, false,
                    LobbyError.AlreadyLoggedIn, "这次连接已经登录过了。");
                return;
            }

            if (!m_Identities.TryLogin(
                    nickname,
                    secret,
                    out var error,
                    out var detail,
                    out var token,
                    out var created))
            {
                var reason = string.IsNullOrEmpty(detail) ? DescribeLoginError(error) : detail;
                SendLobbyResult(client.ClientId, LobbyRequestKind.Login, false, error, reason);
                m_Session?.Log.Info(
                    $"[服务器] 客户端 {client.ClientId} 登录失败（{(nickname ?? string.Empty).Trim()}）：{reason}");
                return;
            }

            var displayName = (nickname ?? string.Empty).Trim();

            // 重连接管（P5）：如果这个昵称正处在掉线宽限里，本次登录就是"回来接管自己"，
            // 而不是一次新的登录。必须排在"昵称是否在线"之前判断——宽限中的那条记录
            // 在名册上仍然是 LoggedIn，不区分的话会被判成"昵称已被占用"。
            var graced = FindGracedClient(displayName, client.ClientId, out var graceKey);
            if (graced != null)
            {
                client.Nickname = displayName;
                client.LoggedIn = true;

                if (TryResumeGracedSession(graced, graceKey, client))
                {
                    SendLobbyResult(
                        client.ClientId,
                        LobbyRequestKind.Login,
                        true,
                        LobbyError.None,
                        "已重连回原来的房间。",
                        token);
                    return;
                }

                // 接管失败（例如房间已经解散）：按普通登录继续走下面的流程。
                client.Nickname = null;
                client.LoggedIn = false;
            }

            if (IsNicknameOnline(displayName, client.ClientId))
            {
                SendLobbyResult(client.ClientId, LobbyRequestKind.Login, false,
                    LobbyError.NicknameOnline, "该昵称已在服务器上游戏中，请换一个昵称。");
                return;
            }

            client.Nickname = displayName;
            client.LoggedIn = true;

            SendLobbyResult(
                client.ClientId,
                LobbyRequestKind.Login,
                true,
                LobbyError.None,
                string.IsNullOrEmpty(detail) ? "登录成功。" : detail,
                token);

            m_Session?.Log.Info(
                $"[服务器] 玩家 {client.ClientId} 以「{displayName}」登录{(created ? "（新建账号）" : "（老账号）")}。");

            // 登录即把该账号的进度（金币 / 任务 / 随身装备）从服务端存档里取出来（P5）。
            // 仓库是房间级共享的，不在这里取。
            var profile = ResolveProfile(displayName);
            if (profile != null)
            {
                m_Session?.Log.Info(
                    $"[服务器] 账号「{displayName}」的进度已就绪：金币 {profile.Money}，"
                    + $"随身背包 {profile.Loadout.Backpack.Items.Count} 件，"
                    + $"共享仓库 {profile.Stash.Items.Count} 件。");
            }
            else
            {
                // 目录还没交接到（极早期登录）：不阻塞登录，进度会在后续操作里按需补上。
                m_Session?.Log.Warning(
                    $"[服务器] 账号「{displayName}」的进度暂不可用（物品目录或存档未就绪），稍后重试。");
            }

            // 登录成功后才点对点发房间状态：此时对方的处理器一定已经注册好了（P-20）。
            SendRoomStateTo(client.ClientId);
        }

        /// <summary>创建房间（仅空闲阶段）。</summary>
        private void HandleLobbyCreateRoom(LobbyClient client, string roomName, string password)
        {
            if (!RequireLobbyLogin(client, LobbyRequestKind.CreateRoom))
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(roomName) ? m_Options.RoomName : roomName.Trim();
            if (!m_Room.TryCreate(client.ClientId, client.Nickname, name, password ?? string.Empty, out var error))
            {
                SendLobbyResult(client.ClientId, LobbyRequestKind.CreateRoom, false, error, DescribeRoomError(error));
                return;
            }

            SendLobbyResult(client.ClientId, LobbyRequestKind.CreateRoom, true, LobbyError.None, "房间已创建。");
            m_Session?.Log.Info(
                $"[服务器] 房间「{m_Room.RoomName}」已创建：房主 {client.ClientId}「{client.Nickname}」"
                + (m_Room.HasPassword ? "（设了密码）" : "（未设密码）"));

            // 进房即进屋（P4.5-b）：联机首站是共享安全屋，房主在这里等队友、整备、从出口出击。
            SpawnMemberIntoSafeHouse(client.ClientId);

            // P5：把该账号的进度摘要（金币）发给他本人，右上角余额立刻是服务器那份。
            SendProfileStateTo(client.ClientId, ProfileStateReasons.Join);

            // P5.5：任务状态同理（联机时客户端的任务副本只是镜像）。
            SendQuestStateTo(client.ClientId);

            BroadcastRoomState();
            ScheduleAutoStart();
        }

        /// <summary>加入房间。</summary>
        private void HandleLobbyJoinRoom(LobbyClient client, string password)
        {
            if (!RequireLobbyLogin(client, LobbyRequestKind.JoinRoom))
            {
                return;
            }

            if (!m_Room.TryJoin(client.ClientId, client.Nickname, password ?? string.Empty, out var error))
            {
                SendLobbyResult(client.ClientId, LobbyRequestKind.JoinRoom, false, error, DescribeRoomError(error));
                m_Session?.Log.Info(
                    $"[服务器] 玩家 {client.ClientId}「{client.Nickname}」加入房间失败：{DescribeRoomError(error)}");
                return;
            }

            SendLobbyResult(client.ClientId, LobbyRequestKind.JoinRoom, true, LobbyError.None, "已加入房间。");
            m_Session?.Log.Info(
                $"[服务器] 玩家 {client.ClientId}「{client.Nickname}」加入房间（{m_Room.MemberCount}/{LobbyLimits.MaxPlayers}）。");

            // 与建房同一条规则：加入房间就把人放进共享安全屋，队友立刻能看见他。
            SpawnMemberIntoSafeHouse(client.ClientId);

            SendProfileStateTo(client.ClientId, ProfileStateReasons.Join);

            SendQuestStateTo(client.ClientId);

            BroadcastRoomState();
        }

        /// <summary>
        /// 开始战局（仅房主、仅等待阶段、且全员都在安全屋）。
        /// </summary>
        /// <param name="client">发起请求的客户端。</param>
        /// <param name="mapSceneName">房主选择的地图场景名；为空时由服务器决定（验收路径）。</param>
        /// <remarks>
        /// <para><b>P4.5-b 的语义变化：</b>开局不再是"房间里点一个按钮"，
        /// 而是"所有人都回到安全屋、房主走到出口选图"——因此这条请求要通过门禁检查
        /// （<see cref="AreAllMembersInSafeHouse"/>）才能进入换图流程。</para>
        /// </remarks>
        private void HandleLobbyStartRaid(LobbyClient client, string mapSceneName)
        {
            if (!RequireLobbyLogin(client, LobbyRequestKind.StartRaid))
            {
                return;
            }

            if (!TryStartRaidFromSafeHouse(client.ClientId, mapSceneName, out var failure))
            {
                SendLobbyResult(client.ClientId, LobbyRequestKind.StartRaid, false, LobbyError.StartRejected, failure);
                m_Session?.Log.Info($"[服务器] 玩家 {client.ClientId} 的出击请求被拒绝：{failure}");
                return;
            }

            SendLobbyResult(client.ClientId, LobbyRequestKind.StartRaid, true, LobbyError.None, "正在前往战局…");
        }

        /// <summary>离开房间。</summary>
        private void HandleLobbyLeaveRoom(LobbyClient client)
        {
            if (m_Room.Find(client.ClientId) == null)
            {
                SendLobbyResult(client.ClientId, LobbyRequestKind.LeaveRoom, false,
                    LobbyError.NotInRoom, "你不在任何房间里。");
                return;
            }

            if (!m_Room.TryLeave(client.ClientId, out var roomEnded))
            {
                return;
            }

            // 离开房间就等于离开当前世界：战局中是退赛（队友继续打），
            // 安全屋中是"先走了"——两种情况下他都不该再以木桩的形式留在别人的屏幕上。
            RemovePlayerFromWorld(client.ClientId);
            m_RaidProgress.Remove(client.ClientId);

            SendLobbyResult(client.ClientId, LobbyRequestKind.LeaveRoom, true, LobbyError.None, "已离开房间。");
            m_Session?.Log.Info(
                roomEnded
                    ? $"[服务器] 玩家 {client.ClientId}「{client.Nickname}」离开，房间已解散。"
                    : $"[服务器] 玩家 {client.ClientId}「{client.Nickname}」离开房间（剩余 {m_Room.MemberCount} 人）。");

            BroadcastRoomState();
            CheckRaidCompletion();
        }

        /// <summary>登录检查：未登录时回一条明确的失败结果。</summary>
        private bool RequireLobbyLogin(LobbyClient client, LobbyRequestKind kind)
        {
            if (client.LoggedIn)
            {
                return true;
            }

            SendLobbyResult(client.ClientId, kind, false, LobbyError.NotLoggedIn, "请先输入昵称与口令登录。");
            return false;
        }

        /// <summary>昵称是否已被另一条在线连接占用。</summary>
        /// <remarks>
        /// 大小写敏感，与账号库的查找规则一致（`StringComparer.Ordinal`）：
        /// 两边规则不同会出现"能登录、却被判在线重复"这种自相矛盾的结果。
        /// </remarks>
        private bool IsNicknameOnline(string nickname, int exceptClientId)
        {
            foreach (var pair in m_LobbyClients)
            {
                if (pair.Key == exceptClientId || !pair.Value.LoggedIn)
                {
                    continue;
                }

                // 掉线宽限中的人不算"在线"：他并没有占着这条连接在玩，
                // 而把他算作在线会让"自己的重连"被判成"昵称被占用"（P5）。
                if (pair.Value.InGrace)
                {
                    continue;
                }

                if (string.Equals(pair.Value.Nickname, nickname, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
