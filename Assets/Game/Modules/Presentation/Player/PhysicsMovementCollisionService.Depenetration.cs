using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 移动碰撞服务的「重叠放松」部分（U-69）：起点已经嵌进阻挡几何时，
    /// 只放松"正在离开"方向上的阻挡，**不修改角色位置**。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独拆一个文件</b>：本类主体已经接近工程规定的单文件行数上限，
    /// 这块能力独立、可单独阅读（只服务"起点已经重叠"这一种情况），因此按项目惯例拆成 partial——
    /// 与 <c>PlayerWeaponController.Firing</c> 同样的做法。</para>
    ///
    /// <para><b>要解决的问题（U-69）</b>：角色走下平台 / 坡道的侧面时，脚底已被地面吸附降到下层地面，
    /// 但胶囊半径（0.4 米）还压在侧棱里（每帧水平只前进约 0.05 米，跨过边缘必然留下这段重叠）。
    /// 此时 PhysX 对**已经重叠**的碰撞体在**每个方向**都返回 0 距离命中（实测四个方向都是 d=0.000），
    /// 位移被压成 0，角色被永久钉死在棱边。</para>
    ///
    /// <para><b>两次踩过的坑（务必保留）</b>：</para>
    /// <list type="number">
    /// <item><description><b>不能"把角色推出去"</b>：第一版实现真的改位置（每次最多推 1 米），
    /// 结果在地形附近大面积误触发——胶囊边缘会吃进起伏地形，而"离开方向"又算错，
    /// 角色一帧被弹飞最多 1 米（全区扫描实测 71 个异常点），表现为"走两步就瞬移"。</description></item>
    /// <item><description><b>不能用 <c>ClosestPoint</c> 求方向</b>：非凸网格碰撞体（地形、山体这类大网格）
    /// 的 <c>ClosestPoint</c> 会把输入点原样返回（实测），于是每条记录都退化成"没方向"，
    /// 只能退到包围盒中心——地形的包围盒中心在地图正中，方向就变成了"沿地图半径向外"。</description></item>
    /// </list>
    ///
    /// <para><b>现在的做法（与碰撞体类型无关）</b>：把胶囊沿**本帧移动方向**试探性地挪一小步
    /// （<see cref="OverlapProbeDistance"/> 米），重新做一次重叠查询：
    /// 原来压着的几何在试探位置**不再重叠**的，说明这个方向就是在离开它——这一批 0 距离命中被放松；
    /// 仍然重叠的（朝里走、贴着蹭）保持阻挡。整个过程只读不写，位置永远只由位移决定。</para>
    /// </remarks>
    public sealed partial class PhysicsMovementCollisionService
    {
        /// <summary>
        /// 试探步长（米）：沿移动方向把胶囊挪这么远，重新看重叠是否解除。
        /// </summary>
        /// <remarks>
        /// 取"半径 + 0.05"：胶囊半径就是最深的横向重叠，再留 0.05 米余量，
        /// 保证"完全压在棱边里"这种最坏情况也能在试探位置脱开；
        /// 而"朝里走"的试探位置只会更深地压在几何里，因此不会被误判为离开。
        /// </remarks>
        private const float OverlapProbeMargin = 0.05f;

        /// <summary>重叠查询的复用缓冲（当前位置），避免每帧分配。</summary>
        private readonly Collider[] m_Overlaps = new Collider[8];

        /// <summary>重叠查询的复用缓冲（试探位置），避免每帧分配。</summary>
        private readonly Collider[] m_ProbeOverlaps = new Collider[8];

        /// <summary>本帧可放松的碰撞体数量（<see cref="m_Overlaps"/> 前这么多项有效）。</summary>
        private int m_OverlapCount;

        /// <summary>本帧是否启用重叠放松。</summary>
        private bool m_RelaxationActive;

        /// <summary>
        /// 刷新本帧的重叠放松集合：找出"当前位置压着、但沿移动方向挪一步就不再压着"的碰撞体。
        /// </summary>
        /// <param name="position">角色当前水平位置。</param>
        /// <param name="moveDirection">本帧移动方向（单位向量）。</param>
        /// <param name="radius">胶囊半径。</param>
        /// <returns>存在可放松的碰撞体时返回 true。</returns>
        private bool RefreshOverlapRelaxation(Vector2F position, Vector2F moveDirection, float radius)
        {
            m_OverlapCount = 0;
            m_RelaxationActive = false;

            var ownerY = m_Owner.position.y;
            var count = OverlapAt(position, ownerY, radius, m_Overlaps);
            if (count == 0)
            {
                return false;
            }

            var probeDistance = radius + OverlapProbeMargin;
            var probePosition = new Vector2F(
                position.X + (moveDirection.X * probeDistance),
                position.Y + (moveDirection.Y * probeDistance));
            var probeCount = OverlapAt(probePosition, ownerY, radius, m_ProbeOverlaps);

            var relaxed = 0;
            for (var i = 0; i < count; i++)
            {
                var candidate = m_Overlaps[i];
                if (candidate == null || IsOwnerCollider(candidate))
                {
                    continue;
                }

                // 试探位置仍然压着它 → 这个方向不是在离开它（朝里走 / 贴着蹭），保持阻挡。
                if (ContainsOverlap(m_ProbeOverlaps, probeCount, candidate))
                {
                    continue;
                }

                m_Overlaps[relaxed] = candidate;
                relaxed++;
            }

            m_OverlapCount = relaxed;
            m_RelaxationActive = relaxed > 0;
            return m_RelaxationActive;
        }

        /// <summary>
        /// 判断某个 0 距离命中是否应当被放松（忽略）。
        /// </summary>
        /// <param name="collider">命中到的碰撞体。</param>
        /// <returns>应当忽略该命中时返回 true。</returns>
        /// <remarks>只有"本帧被判定为正在离开"的那批碰撞体才会被放松，其它照旧阻挡。</remarks>
        private bool ShouldRelaxContact(Collider collider)
        {
            if (!m_RelaxationActive)
            {
                return false;
            }

            return ContainsOverlap(m_Overlaps, m_OverlapCount, collider);
        }

        /// <summary>做一次胶囊重叠查询，把结果写进指定缓冲并返回数量。</summary>
        /// <param name="position">水平位置。</param>
        /// <param name="ownerY">角色脚底高度（胶囊两端跟着它走）。</param>
        /// <param name="radius">胶囊半径。</param>
        /// <param name="buffer">写入的缓冲。</param>
        private int OverlapAt(Vector2F position, float ownerY, float radius, Collider[] buffer)
        {
            var bottom = new Vector3(position.X, ownerY + 0.5f, position.Y);
            var top = new Vector3(position.X, ownerY + m_BodyHeight - 0.4f, position.Y);
            return Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                buffer,
                PhysicsLayers.MovementBlockingMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>判断某个碰撞体是否出现在重叠缓冲的前 N 项里。</summary>
        private static bool ContainsOverlap(Collider[] buffer, int count, Collider collider)
        {
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == collider)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
