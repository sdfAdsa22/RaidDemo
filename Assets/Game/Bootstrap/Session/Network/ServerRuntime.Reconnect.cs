using System.Collections.Generic;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"掉线宽限与重连"部分（P5）：断线的人留在局里，回来时接管原来的位置。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须做这件事（排障手册 P-48）：</b>旧实现在"连接断开"的那一刻立刻
    /// 把玩家从房间与权威世界清掉，而实测显示那一刻**其余玩家的上行链路会一起中断**——
    /// 服务器收不到他们的输入包，10 秒后按协议超时把他们也踢掉，玩家看到的是
    /// "队友掉了，我也被踢，房间解散"。</para>
    ///
    /// <para><b>做法：</b>断线只进入宽限（默认 60 秒，可被 <c>-grace</c> 覆盖）：
    /// 名册、身体、背包、战局进度全部保留；期间同一账号重新登录即"接管"自己原来的状态
    /// （房间名册换绑连接编号 + 权威世界里的东西搬家）。宽限到期才真正清场。</para>
    ///
    /// <para><b>为什么要"搬家"而不是"按新玩家入局"：</b>玩家断线时身上可能背着这一局刚搜到的东西，
    /// 位置也可能在撤离点旁边。按新玩家入局会把这些全丢掉——那不是重连，是"重开了一局"。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 默认的掉线宽限时长（秒）。
        /// </summary>
        /// <remarks>
        /// 取 60 秒：足够覆盖"网络闪断 / 客户端崩溃后马上重启"这两种最常见的情况，
        /// 又不至于让队友在"少一个人"的状态下打太久。服务器可以用 <c>-grace</c> 覆盖它。
        /// </remarks>
        public const float DefaultReconnectGraceSeconds = 60f;

        /// <summary>当前生效的宽限时长（秒）。</summary>
        private float ReconnectGraceSeconds
        {
            get
            {
                return m_Options != null && m_Options.ReconnectGraceSeconds > 0f
                    ? m_Options.ReconnectGraceSeconds
                    : DefaultReconnectGraceSeconds;
            }
        }

        /// <summary>每帧推进：有没有人的宽限已经到期。</summary>
        private void TickDisconnectGrace()
        {
            if (m_LobbyClients.Count == 0)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            List<int> expired = null;

            foreach (var pair in m_LobbyClients)
            {
                var client = pair.Value;
                if (!client.InGrace || now < client.GraceDeadline)
                {
                    continue;
                }

                expired ??= new List<int>();
                expired.Add(pair.Key);
            }

            if (expired == null)
            {
                return;
            }

            for (var i = 0; i < expired.Count; i++)
            {
                FinishDisconnectedSession(expired[i]);
            }
        }

        /// <summary>
        /// 宽限到期：真正把这个人从房间与世界里清掉。
        /// </summary>
        /// <param name="clientId">掉线时的连接编号。</param>
        private void FinishDisconnectedSession(int clientId)
        {
            var member = m_Room.Find(clientId);
            m_LobbyClients.Remove(clientId);
            RemovePlayerFromWorld(clientId);
            m_RaidProgress.Remove(clientId);

            if (member != null && m_Room.TryLeave(clientId, out var roomEnded))
            {
                m_Session?.Log.Info(
                    roomEnded
                        ? $"[服务器] 玩家 {clientId}「{member.Nickname}」的宽限到期仍未重连，房间已解散。"
                        : $"[服务器] 玩家 {clientId}「{member.Nickname}」的宽限到期仍未重连，"
                          + $"已退出房间（剩余 {m_Room.MemberCount} 人）。");

                BroadcastRoomState();
            }

            CheckRaidCompletion();
        }

        /// <summary>
        /// 找一名正在宽限中的玩家（按昵称）。
        /// </summary>
        /// <param name="nickname">账号昵称。</param>
        /// <param name="exceptClientId">要排除的连接编号（当前这条新连接）。</param>
        /// <returns>宽限记录；没有时返回 null。</returns>
        private LobbyClient FindGracedClient(string nickname, int exceptClientId)
        {
            foreach (var pair in m_LobbyClients)
            {
                var client = pair.Value;
                if (pair.Key == exceptClientId || !client.InGrace)
                {
                    continue;
                }

                if (string.Equals(client.Nickname, nickname, System.StringComparison.Ordinal))
                {
                    return client;
                }
            }

            return null;
        }

        /// <summary>
        /// 重连接管：把宽限中的旧连接换成这条新连接。
        /// </summary>
        /// <param name="graced">宽限中的旧记录。</param>
        /// <param name="fresh">刚登录成功的新连接。</param>
        /// <returns>成功接管返回 true；失败时调用方按"普通新玩家"继续。</returns>
        private bool TryResumeGracedSession(LobbyClient graced, LobbyClient fresh)
        {
            var oldId = graced.ClientId;
            var newId = fresh.ClientId;
            var nickname = graced.Nickname;

            if (!m_Room.TryRebind(oldId, newId, out var rebindError))
            {
                m_Session?.Log.Warning(
                    $"[服务器] 玩家「{nickname}」重连换绑失败：{DescribeRoomError(rebindError)}");
                return false;
            }

            fresh.Nickname = nickname;
            fresh.LoggedIn = true;
            m_LobbyClients.Remove(oldId);

            RebindPlayerState(oldId, newId);

            m_Session?.Log.Info(
                $"[服务器] 玩家「{nickname}」已重连回局（连接 {oldId} → {newId}，"
                + $"阶段 {DescribeLobbyPhase(m_Room.Phase)}）。");

            // 新连接需要的最少三样：房间状态（画角标）、进度（金币）、容器内容（背包与仓库）。
            SendRoomStateTo(newId);
            SendProfileStateTo(newId, ProfileStateReasons.Join);
            SendAllContainerContentsTo((ulong)newId);

            // 战局还在进行时把"进图"再发一次：客户端据此加载地图场景。
            // 玩家断线时可能正在安全屋里，因此这条只在战局中发。
            if (m_Room.Phase == LobbyPhase.InRaid)
            {
                SendRaidStartTo(newId);
            }

            BroadcastRoomState();
            return true;
        }

        /// <summary>
        /// 把权威世界里的玩家状态从旧编号搬到新编号（位置、背包、装备、战局进度）。
        /// </summary>
        /// <param name="oldId">旧编号。</param>
        /// <param name="newId">新编号。</param>
        /// <remarks>
        /// <para><b>顺序：</b>先把要保留的东西取出来（位置、装备对象、战局进度），
        /// 再移除旧编号，最后按新编号重新登记。中间任何一步失败都会在日志里留下痕迹，
        /// 而不会静默地把玩家的东西丢掉。</para>
        ///
        /// <para>生命状态按"活着"重建：宽限期内他不在操作，倒地状态没有继续推进的意义，
        /// 重生为站立状态比"重连回来还躺着"更符合预期。</para>
        /// </remarks>
        private void RebindPlayerState(int oldId, int newId)
        {
            var position = SpawnPositionFor(newId);
            var facing = SpawnFacing;

            if (m_World != null && m_World.TryGetSnapshot(oldId, out var snapshot))
            {
                position = snapshot.State.Position;
                facing = snapshot.State.Facing;
            }

            // 装备对象本身要留下：它是账号的资产，不是连接的附属物。
            PlayerLoadout loadout = null;
            m_Combat?.TryGetLoadout(oldId, out loadout);

            RaidProgress progress = null;
            if (m_RaidProgress.TryGetValue(oldId, out var existing))
            {
                progress = existing;
            }

            RemovePlayerFromWorld(oldId);

            CreatePlayerBody(newId);
            if (m_World != null && !m_World.TryAddPlayer(newId, position, facing, out var error))
            {
                m_Session?.Log.Warning($"[服务器] 重连换绑：玩家 {newId} 回到权威世界失败：{error}");
            }

            RegisterLifeState(newId);
            AddPlayerToCombat(newId, loadout);
            BindPlayerHitTarget(newId);
            RegisterPlayerContainers(newId);

            if (progress != null)
            {
                m_RaidProgress[newId] = progress;
                m_RaidProgress.Remove(oldId);
            }

            m_Session?.Log.Info(
                $"[服务器] 重连换绑完成：玩家 {oldId} → {newId}，位置 "
                + $"({position.X:F1}, {position.Y:F1})，背包 {(loadout != null ? loadout.Backpack.Items.Count : 0)} 件。");
        }
    }
}
