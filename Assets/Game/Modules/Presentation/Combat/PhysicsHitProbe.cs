using RaidDemo.Combat;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 用 Unity 物理系统实现射线检测。
    /// </summary>
    /// <remarks>
    /// <para>这是战斗层与引擎之间的唯一接触面。战斗规则只认识 IHitProbe，
    /// 因此它可以在没有场景的测试里被替换成任意编排好的命中结果。</para>
    /// <para>射线长度限制在武器射程内——射程因此不只是显示数值，它真的决定了打得到多远。</para>
    /// </remarks>
    public sealed class PhysicsHitProbe : IHitProbe
    {
        /// <summary>射线检测的层遮罩。默认检测所有层。</summary>
        private readonly int m_LayerMask;

        /// <summary>创建射线检测实现。</summary>
        /// <param name="layerMask">层遮罩，默认检测所有层。</param>
        public PhysicsHitProbe(int layerMask = Physics.DefaultRaycastLayers)
        {
            m_LayerMask = layerMask;
        }

        /// <inheritdoc />
        public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
        {
            hit = default;
            if (maxDistance <= 0f)
            {
                return false;
            }

            if (!Physics.Raycast(origin, direction, out var raycastHit, maxDistance, m_LayerMask))
            {
                return false;
            }

            var target = raycastHit.collider.GetComponentInParent<CombatTargetView>();
            var targetId = target != null ? target.TargetId : 0;
            var center = target != null ? target.CenterWorldPosition : Vector3.zero;

            hit = new HitInfo(targetId, raycastHit.point, center, raycastHit.distance);
            return true;
        }
    }
}
