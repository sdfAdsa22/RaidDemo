using RaidDemo.Presentation;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>联机验收脚本的瞄准部分：优先敌人、否则盯着最近的队友。</summary>
    public sealed partial class SceneBootstrap
    {
        /// <summary>验收模式下瞄准的最大距离（米）。比步枪射程略小，留一点余量。</summary>
        private const float AutoAimEnemyRangeMeters = 11f;

        /// <summary>
        /// 找射程内最近的远端敌人作为瞄准方向。
        /// </summary>
        /// <remarks>
        /// 只认还没阵亡的敌人：尸体不可被命中（服务器已经关掉了它的碰撞体），
        /// 继续朝它开枪会让验收一直停在"开了枪但没命中"。
        /// </remarks>
        /// <param name="aim">找到的瞄准方向。</param>
        /// <returns>射程内存在存活敌人时返回 true。</returns>
        private bool TryResolveEnemyAim(out Vector2F aim)
        {
            aim = Vector2F.Zero;

            if (m_RemoteEnemyViews.Count == 0 || m_PlayerMotor == null)
            {
                return false;
            }

            var self = m_PlayerMotor.SimulatedPosition;
            var bestSqrDistance = AutoAimEnemyRangeMeters * AutoAimEnemyRangeMeters;
            var found = false;

            foreach (var pair in m_RemoteEnemyViews)
            {
                var view = pair.Value;
                if (view == null || view.IsDestroyed)
                {
                    continue;
                }

                var position = view.transform.position;
                var dx = position.x - self.x;
                var dz = position.z - self.y;
                var sqrDistance = (dx * dx) + (dz * dz);
                if (sqrDistance >= bestSqrDistance || sqrDistance < 0.01f)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                aim = new Vector2F(dx, dz).Normalized;
                found = true;
            }

            return found;
        }

        /// <summary>验收模式下把朝向对准最近的远端玩家；没有目标时保持原方向。</summary>
        private Vector2F ResolveAutoAimDirection(Vector2F fallback)
        {
            if (RemoteViews.Count == 0 || m_PlayerMotor == null)
            {
                return fallback;
            }

            var self = m_PlayerMotor.SimulatedPosition;
            var bestSqrDistance = float.MaxValue;
            var aim = fallback;

            foreach (var view in RemoteViews.Values)
            {
                if (view == null)
                {
                    continue;
                }

                var position = view.transform.position;
                var dx = position.x - self.x;
                var dz = position.z - self.y;
                var sqrDistance = (dx * dx) + (dz * dz);
                if (sqrDistance >= bestSqrDistance || sqrDistance < 0.01f)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                aim = new Vector2F(dx, dz).Normalized;
            }

            return aim;
        }

        /// <summary>验收脚本感知到的倒地队友（由生命事件维护）。</summary>
        private readonly System.Collections.Generic.List<int> m_DownedTeammates =
            new System.Collections.Generic.List<int>();

        /// <summary>验收脚本：有队友倒地时走向他，靠近后按住救援键。</summary>
        /// <param name="aim">指向倒地队友的方向。</param>
        /// <param name="withinRange">是否已经近到可以施救。</param>
        /// <returns>存在倒地队友时返回 true。</returns>
        /// <remarks>
        /// 它**不改任何规则**：只是替玩家做出"去救人、按住 F"这两个操作，
        /// 施救判定、进度累计、倒计时全部仍在服务器上按同一套规则跑。
        /// </remarks>
        private bool TryResolveRescueAim(out Vector2F aim, out bool withinRange)
        {
            aim = Vector2F.Zero;
            withinRange = false;

            if (!m_AutoWalk || m_DownedTeammates.Count == 0 || m_PlayerMotor == null)
            {
                return false;
            }

            var self = m_PlayerMotor.SimulatedPosition;
            var bestSqrDistance = float.MaxValue;

            for (var i = 0; i < m_DownedTeammates.Count; i++)
            {
                var teamMateId = m_DownedTeammates[i];
                if (!RemoteViews.TryGetValue(teamMateId, out var view) || view == null)
                {
                    continue;
                }

                var position = view.transform.position;
                var dx = position.x - self.x;
                var dz = position.z - self.y;
                var sqrDistance = (dx * dx) + (dz * dz);
                if (sqrDistance >= bestSqrDistance)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                aim = sqrDistance > 0.0001f ? new Vector2F(dx, dz).Normalized : Vector2F.Up;
                withinRange = sqrDistance <= RaidDemo.Combat.PlayerLifeStateTracker.ReviveRangeMeters
                    * RaidDemo.Combat.PlayerLifeStateTracker.ReviveRangeMeters;
            }

            return bestSqrDistance < float.MaxValue;
        }

        /// <summary>验收脚本维护的倒地名单：由生命事件增删。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="downed">true 表示加入，false 表示移出。</param>
        private void NoteTeammateDowned(int playerId, bool downed)
        {
            if (downed)
            {
                if (!m_DownedTeammates.Contains(playerId))
                {
                    m_DownedTeammates.Add(playerId);
                }

                return;
            }

            m_DownedTeammates.Remove(playerId);
        }

    }
}
