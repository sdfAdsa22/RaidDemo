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
    ///
    /// <para><b>为什么不把单位当墙（U-50）：</b>玩家与 AI 的碰撞胶囊挂在独立单位层上
    /// （见 <see cref="PhysicsLayers"/>），这里统一改用排除单位层的遮罩。
    /// 修复前的症状是敌人贴身时把玩家完全挡住、两个模型卡在一起；
    /// 子弹射线不受影响——战斗查询用的是全层遮罩，照常命中单位。</para>
    /// </remarks>
    public sealed class PhysicsMovementCollisionService : IMovementCollisionWorld
    {
        /// <summary>最大迭代次数。两次足够处理「撞墙后沿墙滑行」这种最常见的情况。</summary>
        private const int MaxIterations = 2;

        /// <summary>小于该长度的位移视为静止（米）。</summary>
        private const float PositionEpsilon = 1e-4f;

        /// <summary>
        /// 可行走斜面的最大倾角（度）。
        /// </summary>
        /// <remarks>
        /// 胶囊扫掠时，坡道表面也会被当成命中。若把它当作墙，玩家走到坡道中段
        /// 就会因胶囊底部与斜面相交而被卡住。真正的障碍是倾角更大的竖直面，
        /// 因此这里把可行走斜面从「阻挡」里排除，高度由 PlayerMotor 的地面吸附负责。
        /// </remarks>
        private const float WalkableSlopeAngle = 45f;

        /// <summary>
        /// 可跨越的台阶高度（米）。
        /// </summary>
        /// <remarks>
        /// 脚底被挡住时，会把胶囊抬高这个高度再扫一次；如果抬高后畅通，
        /// 就允许移动，由地面吸附把角色抬到台阶顶。这样既不会卡在 0.3 米的
        /// 路缘 / 坡道接缝上，也不会让 1.2 米的平台边缘变成可随意翻越的矮墙。
        /// </remarks>
        private const float StepHeight = 0.35f;

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
                    // 脚底被挡住时先试一次"抬高跨越"：低台阶与坡道接缝可以过去，
                    // 平台边缘与栅栏仍然会被高处的胶囊挡住。
                    if (CanStepOver(start, direction, radius, remaining))
                    {
                        travelled += direction * remaining;
                    }

                    break;
                }

                if (CanStepOver(start, direction, radius, remaining))
                {
                    travelled += direction * remaining;
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
            return Cast(start, direction, radius, distance, verticalOffset: 0f, out hit);
        }

        /// <summary>
        /// 抬高胶囊后再扫一次，判断低台阶能否跨越。
        /// </summary>
        private bool CanStepOver(
            Vector2F start,
            Vector2F direction,
            float radius,
            float distance)
        {
            if (distance <= PositionEpsilon)
            {
                return true;
            }

            return !Cast(
                start,
                direction,
                radius,
                distance,
                StepHeight,
                out _);
        }

        /// <summary>带垂直偏移的胶囊扫掠。</summary>
        private bool Cast(
            Vector2F start,
            Vector2F direction,
            float radius,
            float distance,
            float verticalOffset,
            out RaycastHit hit)
        {
            var ownerY = m_Owner.position.y;
            var bottom = new Vector3(start.X, ownerY + 0.5f + verticalOffset, start.Y);
            var top = new Vector3(start.X, ownerY + m_BodyHeight - 0.4f + verticalOffset, start.Y);
            var castDirection = new Vector3(direction.X, 0f, direction.Y);

            var count = Physics.CapsuleCastNonAlloc(
                bottom,
                top,
                radius,
                castDirection,
                m_Hits,
                distance,
                PhysicsLayers.MovementBlockingMask,
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

                // 可行走斜面不是障碍：坡道高度交由 PlayerMotor 的地面吸附处理。
                if (Vector3.Angle(candidate.normal, Vector3.up) <= WalkableSlopeAngle)
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
