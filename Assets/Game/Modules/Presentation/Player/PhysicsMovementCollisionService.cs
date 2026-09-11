using RaidDemo.Shared;
using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 用 PhysX 胶囊扫掠实现移动碰撞：引擎能力适配器。
    /// </summary>
    /// <remarks>
    /// <para><b>它属于表现层而不是模拟层</b>，原因与寻路服务一样：
    /// 模拟层要能在无头服务端运行，那里没有 PhysX。
    /// 逻辑只依赖 <see cref="IMovementCollisionWorld"/>，实现留在这里。</para>
    ///
    /// <para><b>为什么是胶囊扫掠而不是刚体：</b>角色的位置由移动模拟直接算出，
    /// 不是由物理引擎推出来的。加刚体会让两套权威打架——物理把角色推向一边，
    /// 模拟下一秒又把它拉回来。扫掠只回答「这段位移能不能走、能走多少」，
    /// 权威仍然只有一个。</para>
    /// </remarks>
    public sealed class PhysicsMovementCollisionService : IMovementCollisionWorld
    {
        /// <summary>最大迭代次数。两次足够处理「撞墙后沿墙滑行」这种最常见的情况。</summary>
        private const int MaxIterations = 2;

        /// <summary>小于该长度的位移视为静止（米）。</summary>
        private const float PositionEpsilon = 1e-4f;

        private readonly Transform m_Owner;
        private readonly float m_BodyHeight;
        private readonly float m_Skin;

        /// <summary>扫掠结果的复用缓冲，避免每帧分配。</summary>
        private readonly RaycastHit[] m_Hits = new RaycastHit[8];

        /// <summary>创建碰撞服务。</summary>
        /// <param name="owner">移动体自身的根节点，用于排除自己的碰撞体。</param>
        /// <param name="bodyHeight">移动体高度（米）。</param>
        /// <param name="skin">贴合障碍时保留的间隙（米），用于避免贴脸抖动。</param>
        public PhysicsMovementCollisionService(Transform owner, float bodyHeight = 1.8f, float skin = 0.02f)
        {
            m_Owner = owner;
            m_BodyHeight = bodyHeight > 0f ? bodyHeight : 1.8f;
            m_Skin = skin > 0f ? skin : 0.02f;
        }

        /// <inheritdoc />
        public bool TryResolveMove(Vector2F from, Vector2F delta, float radius, out Vector2F resolved)
        {
            resolved = delta;
            if (m_Owner == null)
            {
                return false;
            }

            var distance = delta.Magnitude;
            if (distance <= PositionEpsilon)
            {
                return false;
            }

            var direction = delta.Normalized;
            var travelled = Vector2F.Zero;
            var remaining = distance;
            var blocked = false;

            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                if (remaining <= PositionEpsilon)
                {
                    break;
                }

                var start = new Vector2F(from.X + travelled.X, from.Y + travelled.Y);
                if (!Cast(start, direction, radius, remaining, out var hit))
                {
                    travelled += direction * remaining;
                    break;
                }

                blocked = true;
                var allowed = Mathf.Max(0f, hit.distance - m_Skin);
                travelled += direction * allowed;
                remaining -= allowed;

                if (remaining <= PositionEpsilon || allowed <= PositionEpsilon)
                {
                    break;
                }

                // 沿障碍面滑动：把剩余位移投影到命中平面上，再走一次。
                // 没有这一步时，贴着墙斜向移动会被完全挡住，手感表现为「卡在墙上」。
                var normal = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (normal.sqrMagnitude <= 1e-6f)
                {
                    // 命中面是水平的（例如踩到箱顶边缘）：没有可滑动的方向，停下。
                    break;
                }

                normal.Normalize();
                var motion = new Vector3(direction.X, 0f, direction.Y) * remaining;
                var slide = Vector3.ProjectOnPlane(motion, normal);
                if (slide.sqrMagnitude <= 1e-8f)
                {
                    break;
                }

                direction = new Vector2F(slide.normalized.x, slide.normalized.z);
                remaining = slide.magnitude;
            }

            resolved = travelled;
            return blocked;
        }

        /// <summary>
        /// 朝指定方向做一次胶囊扫掠，返回最近的有效命中。
        /// </summary>
        /// <param name="start">起点（水平面坐标）。</param>
        /// <param name="direction">方向（单位向量）。</param>
        /// <param name="radius">胶囊半径。</param>
        /// <param name="distance">扫掠距离。</param>
        /// <param name="hit">最近的有效命中。</param>
        /// <returns>存在有效命中返回 true。</returns>
        /// <remarks>
        /// <para>胶囊的两端跟随 owner 当前高度：玩家走上坡道与高台时，
        /// 若把高度写死在地面，扫掠胶囊会插进坡道实体里，表现为走到坡道中段被卡住。</para>
        ///
        /// <para>起点与自身胶囊重叠时 PhysX 可能返回 distance 为零的命中，
        /// 这会表现为「角色一步都走不动」。过滤掉 owner 自身的碰撞体是必须的，
        /// 而不是可选的优化。</para>
        /// </remarks>
        private bool Cast(
            Vector2F start,
            Vector2F direction,
            float radius,
            float distance,
            out RaycastHit hit)
        {
            var ownerY = m_Owner.position.y;
            var bottom = new Vector3(start.X, ownerY + 0.5f, start.Y);
            var top = new Vector3(start.X, ownerY + m_BodyHeight - 0.4f, start.Y);
            var castDirection = new Vector3(direction.X, 0f, direction.Y);

            var count = Physics.CapsuleCastNonAlloc(
                bottom,
                top,
                radius,
                castDirection,
                m_Hits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            hit = default;
            var found = false;
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var candidate = m_Hits[i];
                if (candidate.collider == null || IsOwnerCollider(candidate.collider))
                {
                    continue;
                }

                if (candidate.distance >= nearest)
                {
                    continue;
                }

                nearest = candidate.distance;
                hit = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>该碰撞体是否属于移动体自身。</summary>
        private bool IsOwnerCollider(Collider collider)
        {
            var target = collider.transform;
            return target == m_Owner || target.IsChildOf(m_Owner);
        }
    }
}
