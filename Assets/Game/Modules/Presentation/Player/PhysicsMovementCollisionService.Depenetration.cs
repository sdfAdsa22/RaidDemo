using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 移动碰撞服务的「去穿透」部分（U-69）：把已经嵌进阻挡几何的角色横向推出来。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独拆一个文件</b>：本类主体已经接近工程规定的单文件行数上限，
    /// 去穿透是一块独立、可单独阅读的能力（只服务"起点已经重叠"这一种情况），
    /// 因此按项目惯例拆成 partial——与 <c>PlayerWeaponController.Firing</c> 同样的做法。</para>
    ///
    /// <para><b>典型触发场景</b>：角色走下平台 / 坡道的侧面——脚底已被地面吸附降到下层地面，
    /// 但胶囊半径（0.4 米）还压在侧棱里（每帧水平只前进约 0.05 米，跨过边缘必然留下这段重叠）。
    /// 此时 PhysX 对**已经重叠**的碰撞体在每个方向都返回 0 距离命中（实测四个方向都是 d=0.000），
    /// 位移被压成 0，角色被永久钉死在棱边。</para>
    /// </remarks>
    public sealed partial class PhysicsMovementCollisionService
    {
        /// <summary>去穿透的最大迭代次数。每次推出 <see cref="DepenetrationStep"/> 米，合计上限 1 米。</summary>
        /// <remarks>
        /// 必须**在同一帧内完全脱离**重叠：只要还剩哪怕 0.1 米重叠，
        /// 扫掠仍会返回 0 距离命中、位移依旧被压成 0（回归测试实测到这一点）。
        /// 上限 1 米用于兜底"被传送进几何内部"这类极端情况，避免无限推。
        /// </remarks>
        private const int MaxDepenetrationIterations = 20;

        /// <summary>单次去穿透迭代推出的水平距离（米）。</summary>
        /// <remarks>
        /// 取 0.05 而不是一次推足，是为了让每一小步都用一次重叠查询验证——
        /// 推出方向随位置变化（例如在拐角处），一次推足容易推过头。
        /// </remarks>
        private const float DepenetrationStep = 0.05f;

        /// <summary>
        /// 判定「最近点在正下方（地板 / 斜面）」的比例阈值。
        /// </summary>
        /// <remarks>
        /// 水平分量小于竖直分量的这个比例时，视为脚下地面：脚底高度由地面吸附负责，
        /// 去穿透不参与。侧棱给出的水平分量远大于竖直分量，因此不会被误伤。
        /// </remarks>
        private const float FloorSkipRatio = 0.5f;

        /// <summary>判定「最近点退化」的阈值（水平距离平方）。</summary>
        /// <remarks>
        /// 球心落进碰撞体内部（或正好压在表面上）时，ClosestPoint 会原样返回球心自身，
        /// 水平距离算出来是 0——平台边缘实测到这一种，需要走兜底方向。
        /// </remarks>
        private const float DegenerateEscapeEpsilon = 1e-8f;

        /// <summary>
        /// 去穿透结束后要留出的余隙（米）。
        /// </summary>
        /// <remarks>
        /// 必须比扫掠用的安全间隙（0.02 米）大一点：如果只推到"正好贴面"，
        /// 扫掠从相切位置出发仍然会返回 0 距离命中、位移照样是 0
        /// （回归测试实测到 resolved 停在 0.3 米不再前进）。
        /// </remarks>
        private const float EscapeMargin = 0.03f;

        /// <summary>去穿透用的重叠查询缓冲，避免每帧分配。</summary>
        private readonly Collider[] m_Overlaps = new Collider[8];

        /// <summary>
        /// 去穿透：把角色从已经嵌进去的阻挡几何里横向推出来（U-69）。
        /// </summary>
        /// <param name="position">角色当前水平位置。</param>
        /// <param name="radius">胶囊半径。</param>
        /// <returns>需要额外施加的水平位移（可能为零）。</returns>
        /// <remarks>
        /// <para>为什么不是"忽略 0 距离命中"：那种写法在贴着薄墙时会允许角色直接穿过去。
        /// 去穿透保证角色先被推到几何外面，再交给正常的扫掠判定——
        /// 朝墙走的结果仍然是"被挡住"，只是不再被钉死。</para>
        ///
        /// <para>推出方向只取**水平分量**：脚下的地板 / 斜面虽然也可能与胶囊重叠，
        /// 但它们给出的方向是竖直的，会被自动忽略——脚底高度由地面吸附负责，
        /// 这里不会把角色从坡道上顶飞。</para>
        /// </remarks>
        private Vector2F ResolvePenetration(Vector2F position, float radius)
        {
            var escaped = Vector2F.Zero;
            for (var iteration = 0; iteration < MaxDepenetrationIterations; iteration++)
            {
                var probe = new Vector2F(position.X + escaped.X, position.Y + escaped.Y);
                if (!TryFindPenetration(probe, radius, out var direction))
                {
                    break;
                }

                escaped += direction * DepenetrationStep;
            }

            return escaped;
        }

        /// <summary>
        /// 找出一条把胶囊推出重叠的水平方向。
        /// </summary>
        /// <param name="position">待检查的水平位置。</param>
        /// <param name="radius">胶囊半径。</param>
        /// <param name="direction">输出：水平推出方向（单位向量）。</param>
        /// <returns>存在重叠且能给出方向时返回 true。</returns>
        private bool TryFindPenetration(Vector2F position, float radius, out Vector2F direction)
        {
            direction = Vector2F.Zero;
            var ownerY = m_Owner.position.y;
            var bottom = new Vector3(position.X, ownerY + 0.5f, position.Y);
            var top = new Vector3(position.X, ownerY + m_BodyHeight - 0.4f, position.Y);

            var count = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                m_Overlaps,
                PhysicsLayers.MovementBlockingMask,
                QueryTriggerInteraction.Ignore);

            var bestPenetration = 0f;
            var bestX = 0f;
            var bestZ = 0f;
            for (var i = 0; i < count; i++)
            {
                var candidate = m_Overlaps[i];
                if (candidate == null || IsOwnerCollider(candidate))
                {
                    continue;
                }

                // 上下两个球心各求一次最近点：取水平分量更大的那个方向。
                // 侧棱给出的方向是水平的（会被采用），地板给出的方向是竖直的（被 FloorSkipRatio 过滤）。
                EvaluateEscape(candidate, bottom, radius, ref bestPenetration, ref bestX, ref bestZ);
                EvaluateEscape(candidate, top, radius, ref bestPenetration, ref bestX, ref bestZ);
            }

            if (bestPenetration <= 0f)
            {
                return false;
            }

            var length = Mathf.Sqrt((bestX * bestX) + (bestZ * bestZ));
            direction = new Vector2F(bestX / length, bestZ / length);
            return true;
        }

        /// <summary>求一个球心到碰撞体的推出方向与穿透深度，保留穿透最深的一条。</summary>
        /// <param name="collider">参与判定的碰撞体。</param>
        /// <param name="sphereCenter">胶囊某一端的球心。</param>
        /// <param name="radius">胶囊半径。</param>
        /// <param name="bestPenetration">当前最大的穿透深度（引用更新）。</param>
        /// <param name="bestX">当前最优方向 X 分量（未归一化，引用更新）。</param>
        /// <param name="bestZ">当前最优方向 Z 分量（未归一化，引用更新）。</param>
        private static void EvaluateEscape(
            Collider collider,
            Vector3 sphereCenter,
            float radius,
            ref float bestPenetration,
            ref float bestX,
            ref float bestZ)
        {
            var closest = collider.ClosestPoint(sphereCenter);
            var dx = sphereCenter.x - closest.x;
            var dz = sphereCenter.z - closest.z;
            var horizontal = Mathf.Sqrt((dx * dx) + (dz * dz));
            float penetration;

            if (horizontal <= DegenerateEscapeEpsilon)
            {
                // 退化情形：球心已经落在碰撞体内部（或正好压在表面上）。
                // 兜底方向改用「碰撞体包围盒中心 → 球心」的水平分量；
                var boundsCenter = collider.bounds.center;
                dx = sphereCenter.x - boundsCenter.x;
                dz = sphereCenter.z - boundsCenter.z;
                horizontal = Mathf.Sqrt((dx * dx) + (dz * dz));
                if (horizontal <= DegenerateEscapeEpsilon)
                {
                    // 连方向都定不出来（球心正好是包围盒中心）：放弃这一条。
                    return;
                }

                // 球心在内部，按整半径 + 余隙推出。
                penetration = radius + EscapeMargin;
            }
            else
            {
                // 地板 / 斜面：最近点在正下方，水平分量远小于竖直分量 → 交给地面吸附。
                var vertical = Mathf.Abs(sphereCenter.y - closest.y);
                if (horizontal <= FloorSkipRatio * vertical)
                {
                    return;
                }

                // 侧棱：需要推出的距离 = 半径 - 球心到侧面的水平距离。
                // 注意不能要求这个距离足够大——平台边缘实测球心只差 0.00027 米就压在面上，
                // 方向完全正确，若按"最小距离"过滤会把这种情形整个丢掉（U-69 第一次修复的坑）。
                penetration = radius + EscapeMargin - horizontal;
            }

            if (penetration <= bestPenetration)
            {
                return;
            }

            bestPenetration = penetration;
            bestX = dx;
            bestZ = dz;
        }
    }
}
