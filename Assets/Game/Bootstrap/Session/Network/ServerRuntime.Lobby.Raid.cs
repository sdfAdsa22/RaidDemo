using System.Collections.Generic;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"房间 → 战局 → 房间"转移：开局、收尾、自动开局与状态页操作。
    /// </summary>
    /// <remarks>
    /// <para><b>这一段是 P4 的核心行为变化</b>：战局不再随服务器启动而开始，而是随房主开局开始；
    /// 一局结束后房间回到等待状态，房主可以再开一局——服务器的生命周期与"局"分开了。</para>
    ///
    /// <para>世界层面的复位复用 P3.5 的 <c>RestartRaid</c>（容器重抽、AI 重建、玩家回出生点），
    /// 因此这里不重复实现"怎么重置一局"，只决定"什么时候重置、谁进局、谁出局"。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 安全屋 → 战局：门禁通过后切换服务器世界，世界就绪时把成员放进图并通知所有人加载地图。
        /// </summary>
        /// <remarks>
        /// <para><b>顺序不能颠倒：</b>先放人再重置，重置才能把所有人一并复位到出生点；
        /// 先让世界就绪再通知客户端，客户端一进来才不会看到一张空地图。</para>
        ///
        /// <para><b>为什么是两阶段（P4.5-b）：</b>服务器此刻还在安全屋场景里，
        /// 战局地图要等 <c>LoadScene</c> 真正生效才能放人（碰撞体、地面高度、导航都在那张场景里）。
        /// 因此本方法只负责"门禁 + 切世界"，真正开局的动作交给世界就绪回调
        /// <see cref="BeginRaidRound"/>。</para>
        /// </remarks>
        /// <param name="requesterId">发起出击的玩家编号（房主）；自动开局时传房主编号。</param>
        /// <param name="mapSceneName">房主选择的地图场景名；为空时用服务器参数 / 默认地图。</param>
        /// <param name="failure">失败原因（可直接显示给玩家的中文）。</param>
        /// <returns>门禁与状态机都通过、世界切换已发起时返回 true。</returns>
        internal bool TryStartRaidFromSafeHouse(int requesterId, string mapSceneName, out string failure)
        {
            m_AutoStartDeadline = -1f;
            failure = string.Empty;

            // 门禁先于状态机：门禁不过时房间不该进入"战局中"——
            // 否则房间会卡在"正在打"而地图上一个人都没有，玩家只能重启服务器。
            if (!AreAllMembersInSafeHouse(out var gateReason))
            {
                failure = gateReason;
                m_Session?.Log.Warning($"[服务器] 出击被门禁拦下：{gateReason}");
                return false;
            }

            // 任何调用路径都必须先让房间进入"战局中"。这里兜底而不是依赖调用方：
            // 自动开局（-autostart）曾经直接调本方法，结果房间停在"等待中"而玩家已经进了图——
            // 那会让房间在战局期间继续接受加入，而且全员的结算不会被判为"这一局结束"。
            if (m_Room.Phase != LobbyPhase.InRaid
                && !m_Room.TryStartRaid(requesterId, out var startError))
            {
                failure = DescribeRoomError(startError);
                m_Session?.Log.Warning($"[服务器] 开局被状态机拒绝：{failure}");
                return false;
            }

            var scene = ResolveRaidSceneName(mapSceneName);
            m_Session?.Log.Info($"[服务器] 房主确认出击：切换到战局地图「{scene}」。");

            EnterRaidWorld(scene, BeginRaidRound);
            return true;
        }

        /// <summary>
        /// 战局世界就绪：把成员放进图、重置这一局，然后通知客户端加载地图。
        /// </summary>
        private void BeginRaidRound()
        {
            var memberIds = new List<int>(m_Room.MemberCount);
            for (var i = 0; i < m_Room.Members.Count; i++)
            {
                memberIds.Add(m_Room.Members[i].ClientId);
            }

            // 撤离点必须先收集：它们的惰性收集原本发生在切图之后的第一帧，
            // 而"把玩家放进图"就在这一刻——验收钩子 -spawnzone（出生在撤离区）
            // 和将来的"出生点校验"都依赖这份数据已经就位（P4.5-b）。
            TryCollectExtractionZones();

            for (var i = 0; i < memberIds.Count; i++)
            {
                SpawnPlayerIntoWorld(memberIds[i]);
            }

            RestartRaid();
            m_RaidStartedAt = Time.realtimeSinceStartup;

            // 地图名以"服务器实际托管的场景"为准：它是这一次门禁与切换的最终结果。
            BroadcastRaidStart(m_WorldSceneName);
            BroadcastRoomState();

            m_Session?.Log.Info(
                $"[服务器] 战局开始：{memberIds.Count} 名玩家进入房间「{m_Room.RoomName}」。");
        }

        /// <summary>
        /// 战局 → 安全屋：房间回到等待，服务器切回安全屋并让所有人一起回屋。
        /// </summary>
        /// <remarks>
        /// <para>房间若已经解散（最后一人离开），这里只负责清场，不把房间"复活"成等待中；
        /// 但世界仍然切回安全屋——服务器要在那里等下一批玩家。</para>
        ///
        /// <para>顺序：先让房间回到等待（这样门禁看到的就是"大家都回来了"），
        /// 再切世界；世界就绪之后才通知客户端回屋——客户端加载安全屋场景时，
        /// 服务器这边已经准备好接住他的移动输入。</para>
        /// </remarks>
        internal void EndRaidToLobby()
        {
            m_RaidProgress.Clear();
            m_RaidStartedAt = -1f;
            m_Room.EndRaid();

            BroadcastRoomState();

            m_Session?.Log.Info(
                $"[服务器] 本局结束：全员已结算，{(m_Room.Exists ? "房间回到等待状态" : "房间已解散")}"
                + $"（等待中的成员 {m_Room.MemberCount} 人），正在返回共享安全屋。");

            EnterSafeHouseWorld(() =>
            {
                SpawnMembersIntoSafeHouse();
                BroadcastRaidEnd();
            });
        }

        /// <summary>
        /// 检查这一局是否已经没人还在打；是则收尾回等待。
        /// </summary>
        /// <remarks>
        /// 两个必须的提前返回：
        /// ① 有人还没有战局进度——说明开局那一帧还没走完，不能把"还没开始"当成"已经结束"；
        /// ② 有人既没结算也还在世界里——他还在打。
        /// </remarks>
        private void CheckRaidCompletion()
        {
            // 收尾的判据是"服务器是否托管着战局世界"，而不是"房间是否还处于战局中"：
            // 最后一人离开会把房间直接解散（相位回到 Empty）；若这里继续按房间相位提前返回，
            // 世界就永远留在战局——下一批玩家建新房时会踩到残留状态
            //（负责人反馈的"创建相同房间号也能进入但很多 bug"）。
            if (m_WorldKind != ServerWorldKind.Raid || m_World == null)
            {
                return;
            }

            if (m_Room.Phase != LobbyPhase.InRaid)
            {
                // 房间已解散或已回到等待：没有人需要这场战局了，直接收尾回安全屋。
                // EndRaidToLobby 内部对"房间已解散"是安全的（不会把房间复活成等待中）。
                EndRaidToLobby();
                return;
            }

            var activePlayers = 0;

            for (var i = 0; i < m_Room.Members.Count; i++)
            {
                var id = m_Room.Members[i].ClientId;
                if (!m_PlayerBodies.ContainsKey(id))
                {
                    continue;
                }

                if (!m_RaidProgress.TryGetValue(id, out var progress))
                {
                    return;
                }

                if (!progress.Settled)
                {
                    activePlayers++;
                }
            }

            if (activePlayers > 0)
            {
                return;
            }

            EndRaidToLobby();
        }

        /// <summary>房间成立后按 <c>-autostart</c> 安排自动开局（验收辅助）。</summary>
        private void ScheduleAutoStart()
        {
            if (m_Options.AutoStartSeconds <= 0f || !m_Room.Exists)
            {
                return;
            }

            m_AutoStartDeadline = Time.realtimeSinceStartup + m_Options.AutoStartSeconds;
            m_Session?.Log.Info(
                $"[服务器] 验收辅助：{m_Options.AutoStartSeconds:F0} 秒后自动开始战局（-autostart，等价于房主点开始）。");
        }

        /// <summary>每帧推进大厅：目前只有自动开局的到点判断。</summary>
        private void TickLobby()
        {
            // 掉线宽限的到期判定（P5）：宽限结束才真正清场。
            TickDisconnectGrace();

            if (m_AutoStartDeadline < 0f || m_Room.Phase != LobbyPhase.Waiting)
            {
                return;
            }

            if (Time.realtimeSinceStartup < m_AutoStartDeadline)
            {
                return;
            }

            m_AutoStartDeadline = -1f;
            m_Session?.Log.Info("[服务器] 验收辅助 -autostart 到点：自动开始战局。");

            if (!TryStartRaidFromSafeHouse(m_Room.HostClientId, null, out var failure))
            {
                m_Session?.Log.Warning($"[服务器] -autostart 开局失败：{failure}");
            }
        }

        /// <summary>把当前房间整理成局域网发现用的公告。</summary>
        internal LanRoomInfo BuildRoomAnnouncement()
        {
            return new LanRoomInfo
            {
                RoomName = m_Room.Exists ? m_Room.RoomName : m_Options.RoomName,
                GamePort = m_Options.Port,
                PlayerCount = m_Room.MemberCount,
                MaxPlayers = LobbyLimits.MaxPlayers,
                HasPassword = m_Room.HasPassword,
                Phase = (byte)m_Room.Phase,
            };
        }

        /// <summary>状态页的"停止房间"：把所有人请出房间并清场。</summary>
        internal void StopRoomFromDashboard()
        {
            var memberIds = new List<int>(m_Room.MemberCount);
            for (var i = 0; i < m_Room.Members.Count; i++)
            {
                memberIds.Add(m_Room.Members[i].ClientId);
            }

            for (var i = 0; i < memberIds.Count; i++)
            {
                var id = memberIds[i];
                RemovePlayerFromWorld(id);
                m_RaidProgress.Remove(id);
                m_Room.TryLeave(id, out _);

                SendLobbyResult(id, LobbyRequestKind.LeaveRoom, false,
                    LobbyError.NotInRoom, "房间已被服务器停止。");
            }

            BroadcastRoomState();
            m_RaidStartedAt = -1f;

            m_Session?.Log.Info("[服务器] 状态页请求：已停止房间（成员全部退出）。");
        }

        /// <summary>状态页的"停止服务器"：先收摊再退出进程。</summary>
        internal void StopServerFromDashboard()
        {
            m_Session?.Log.Info("[服务器] 状态页请求：停止服务器进程。");
            Shutdown();

#if UNITY_EDITOR
            if (Application.isEditor)
            {
                UnityEditor.EditorApplication.isPlaying = false;
                return;
            }
#endif

            Application.Quit();
        }
    }
}
