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
            if (m_RemoteViews.Count == 0 || m_PlayerMotor == null)
            {
                return fallback;
            }

            var self = m_PlayerMotor.SimulatedPosition;
            var bestSqrDistance = float.MaxValue;
            var aim = fallback;

            foreach (var view in m_RemoteViews.Values)
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
    }
}
