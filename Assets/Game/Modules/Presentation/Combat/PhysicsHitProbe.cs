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

        /// <summary>
        /// 候选命中的共享缓冲：跳过射手自己的那条路径要收集全部命中再挑最近的有效项。
        /// </summary>
        /// <remarks>与相机遮挡探测同一套做法（复用静态数组，避免每次开火产生托管分配）。
        /// 16 个候选足够覆盖"身体 + 掩体 + 目标"这类最坏情形。</remarks>
        private static readonly RaycastHit[] s_Candidates = new RaycastHit[16];

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

            origin = LiftOriginIfNeeded(origin);

            if (!Physics.Raycast(origin, direction, out var raycastHit, maxDistance, m_LayerMask))
            {
                return false;
            }

            hit = ResolveHit(raycastHit);
            return true;
        }

        /// <inheritdoc />
        public bool TryRaycastIgnoringTarget(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            int ignoredTargetId,
            out HitInfo hit)
        {
            hit = default;
            if (maxDistance <= 0f)
            {
                return false;
            }

            if (ignoredTargetId == 0)
            {
                // 没有要跳过的目标时走单发路径：结果相同，也省一次全量收集。
                return TryRaycast(origin, direction, maxDistance, out hit);
            }

            origin = LiftOriginIfNeeded(origin);
            var count = Physics.RaycastNonAlloc(origin, direction, s_Candidates, maxDistance, m_LayerMask);

            var found = false;
            var bestDistance = float.MaxValue;
            var best = default(RaycastHit);
            for (var i = 0; i < count; i++)
            {
                var candidate = s_Candidates[i];

                // 距离为零的命中表示探测起点已经在碰撞体内部（相机遮挡排查时同一类过滤）：
                // 它不是一条真实的弹道阻挡，跳过。
                if (candidate.distance <= 0f || candidate.distance >= bestDistance)
                {
                    continue;
                }

                if (ResolveTargetId(candidate.collider) == ignoredTargetId)
                {
                    continue;   // 射手自己：透明，继续往后找
                }

                best = candidate;
                bestDistance = candidate.distance;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            hit = ResolveHit(best);
            return true;
        }

        /// <summary>按配置把起点抬到射手脚下的地面高度（见构造函数的说明）。</summary>
        private Vector3 LiftOriginIfNeeded(Vector3 origin)
        {
            if (m_LiftOriginToGround
                && NavMesh.SamplePosition(origin, out var navHit, 4f, NavMesh.AllAreas))
            {
                origin.y += navHit.position.y;
            }

            return origin;
        }

        /// <summary>把物理命中转换为战斗层的命中结果。</summary>
        private static HitInfo ResolveHit(in RaycastHit raycastHit)
        {
            var target = raycastHit.collider.GetComponentInParent<CombatTargetView>();
            var targetId = target != null ? target.TargetId : 0;
            var center = target != null ? target.CenterWorldPosition : Vector3.zero;

            return new HitInfo(targetId, raycastHit.point, center, raycastHit.distance);
        }

        /// <summary>取碰撞体所属的受击目标标识；没有登记目标时返回 0（环境）。</summary>
        private static int ResolveTargetId(Collider collider)
        {
            var target = collider != null ? collider.GetComponentInParent<CombatTargetView>() : null;
            return target != null ? target.TargetId : 0;
        }
    }
}
