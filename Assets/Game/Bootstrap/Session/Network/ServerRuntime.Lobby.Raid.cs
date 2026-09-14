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
        /// 房间 → 战局：把成员放进权威世界，重置战局世界，然后通知所有人加载地图。
        /// </summary>
        /// <remarks>
        /// 顺序不能颠倒：先放人再重置，重置才能把所有人一并复位到出生点；
        /// 先通知客户端加载地图、服务器这边却还没准备好，客户端一进来就什么也看不到。
        /// </remarks>
        internal void StartRaidFromLobby()
        {
            m_AutoStartDeadline = -1f;

            // 任何调用路径都必须先让房间进入"战局中"。这里兜底而不是依赖调用方：
            // 自动开局（-autostart）曾经直接调本方法，结果房间停在"等待中"而玩家已经进了图——
            // 那会让房间在战局期间继续接受加入，而且全员的结算不会被判为"这一局结束"。
            if (m_Room.Phase != LobbyPhase.InRaid
                && !m_Room.TryStartRaid(m_Room.HostClientId, out var startError))
            {
                m_Session?.Log.Warning($"[服务器] 开局被状态机拒绝：{DescribeRoomError(startError)}");
                return;
            }

            var memberIds = new List<int>(m_Room.MemberCount);
            for (var i = 0; i < m_Room.Members.Count; i++)
            {
                memberIds.Add(m_Room.Members[i].ClientId);
            }

            for (var i = 0; i < memberIds.Count; i++)
            {
                SpawnPlayerIntoWorld(memberIds[i]);
            }

            RestartRaid();
            m_RaidStartedAt = Time.realtimeSinceStartup;

            BroadcastRaidStart(m_Options.MapSceneName);
            BroadcastRoomState();

            m_Session?.Log.Info(
                $"[服务器] 战局开始：{memberIds.Count} 名玩家进入房间「{m_Room.RoomName}」。");
        }

        /// <summary>
        /// 战局 → 等待：把成员移出世界，房间保留（房主可以再开一局）。
        /// </summary>
        /// <remarks>房间若已经解散（最后一人离开），这里只负责清场，不把房间"复活"成等待中。</remarks>
        internal void EndRaidToLobby()
        {
            var memberIds = new List<int>(m_Room.MemberCount);
            for (var i = 0; i < m_Room.Members.Count; i++)
            {
                memberIds.Add(m_Room.Members[i].ClientId);
            }

            for (var i = 0; i < memberIds.Count; i++)
            {
                RemovePlayerFromWorld(memberIds[i]);
            }

            m_RaidProgress.Clear();
            m_RaidStartedAt = -1f;
            m_Room.EndRaid();

            BroadcastRoomState();

            m_Session?.Log.Info(
                $"[服务器] 本局结束：全员已结算，{(m_Room.Exists ? "房间回到等待状态" : "房间已解散")}"
                + $"（等待中的成员 {m_Room.MemberCount} 人）。");
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
            if (m_Room.Phase != LobbyPhase.InRaid || m_World == null)
            {
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
            StartRaidFromLobby();
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
