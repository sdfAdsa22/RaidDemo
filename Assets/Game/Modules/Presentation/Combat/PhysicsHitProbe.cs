using RaidDemo.Combat;
using UnityEngine;
using UnityEngine.AI;

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
        private readonly bool m_LiftOriginToGround;

        /// <summary>创建射线检测实现。</summary>
        /// <param name="layerMask">层遮罩，默认检测所有层。</param>
        public PhysicsHitProbe(int layerMask = Physics.DefaultRaycastLayers)
            : this(false, layerMask)
        {
        }

        /// <summary>创建射线检测实现。</summary>
        /// <param name="liftOriginToGround">
        /// 是否把射线起点抬到"射手脚下的地面高度"。AI 的逻辑坐标只有平面（XZ），高度属于场景信息，
        /// 不抬的话站在装卸平台上的敌人会把子弹从平台下方打出去，永远够不到平台上的目标。
        /// 玩家开枪传入的已经是带高度的世界坐标，必须保持 false，否则会重复抬高。
        /// </param>
        /// <param name="layerMask">层遮罩，默认检测所有层。</param>
        public PhysicsHitProbe(bool liftOriginToGround, int layerMask = Physics.DefaultRaycastLayers)
        {
            m_LayerMask = layerMask;
            m_LiftOriginToGround = liftOriginToGround;
        }

        /// <inheritdoc />
        public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
        {
            hit = default;
            if (maxDistance <= 0f)
            {
                return false;
            }

            if (m_LiftOriginToGround
                && NavMesh.SamplePosition(origin, out var navHit, 4f, NavMesh.AllAreas))
            {
                origin.y += navHit.position.y;
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
