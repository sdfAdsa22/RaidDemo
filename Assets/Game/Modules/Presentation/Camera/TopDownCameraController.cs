using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 斜俯视跟随相机。
    /// </summary>
    /// <remarks>
    /// 采用固定俯角（默认 45 度）跟随目标，不做旋转。这是本项目选择的视角形式：
    /// 相比正俯视能保留立体感与高低差可读性，相比第三人称又能大幅压低动画与美术成本。
    ///
    /// 相机不参与任何游戏逻辑，只读取目标位置。因此它可以被随时替换或删除，
    /// 不影响模拟层的正确性。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TopDownCameraController : MonoBehaviour
    {
        /// <summary>跟随目标。为空时相机保持原位。</summary>
        [SerializeField] private Transform m_Target;

        /// <summary>俯角（度）。45 度是俯视与立体感的折中值。</summary>
        [SerializeField] private float m_PitchDegrees = 45f;

        /// <summary>相机到目标的距离。</summary>
        [SerializeField] private float m_Distance = 14f;

        /// <summary>
        /// 相机位置的世界坐标偏移。
        /// 用于把镜头稍微抬高，使玩家获得更远的视野；不需要时保持零即可。
        /// </summary>
        [SerializeField] private Vector3 m_WorldOffset = Vector3.zero;

        /// <summary>
        /// 平滑时间（秒）。取 0 表示硬跟随。
        /// 默认 0.12 秒在"跟手"与"稳定"之间取得平衡：既不会明显滞后，也能吸收微小抖动。
        /// </summary>
        [SerializeField] private float m_SmoothTime = 0.12f;

        /// <summary>
        /// 是否启用遮挡回避。关闭后相机行为与本功能加入之前完全一致。
        /// 留这个开关是为了在遮挡判定本身出问题时能一键排除它的嫌疑。
        /// </summary>
        [SerializeField] private bool m_CollisionEnabled = true;

        /// <summary>
        /// 遮挡探测的球半径（米）。用小球而不是一条射线：射线在镜头贴边掠过物体时
        /// 会出现"这一帧没挡住、下一帧挡住了"的抖动，表现为镜头反复穿进地形又弹出来。
        /// </summary>
        [SerializeField] private float m_CollisionRadius = 0.3f;

        /// <summary>
        /// 命中后保留的安全余量（米）。把相机停在碰撞面之前一段距离，
        /// 避免镜头紧贴墙面导致近裁剪面把几何体裁掉、露出场景背面。
        /// </summary>
        [SerializeField] private float m_CollisionMargin = 0.35f;

        /// <summary>
        /// 遮挡回避允许拉近到的最小距离（米）。再近相机就会穿进角色模型里。
        /// 同时也是相机距离的下限：<see cref="m_Distance"/> 配得比它小也不会生效。
        /// </summary>
        [SerializeField] private float m_MinDistance = 3f;

        /// <summary>
        /// 遮挡探测的命中缓冲。复用一个静态数组交给 <c>SphereCastNonAlloc</c> 填充，
        /// 避免每帧探测都产生一次托管分配——这类每帧分配会稳定地推高 GC 频率。
        /// </summary>
        private static readonly RaycastHit[] s_OcclusionHits = new RaycastHit[16];

        private Vector3 m_Velocity;

        /// <summary>
        /// 当前生效的相机距离（米）。负值表示尚未初始化，首次使用时对齐到配置值。
        /// </summary>
        private float m_CurrentDistance = -1f;

        /// <summary>距离恢复平滑用的速度缓存，由 <c>Mathf.SmoothDamp</c> 维护。</summary>
        private float m_DistanceVelocity;

        /// <summary>本帧是否因遮挡而把相机压近。压近时要跳过位置平滑，否则会慢半拍、露出穿帮。</summary>
        private bool m_OcclusionPullIn;

        /// <summary>设置跟随目标。</summary>
        public void SetTarget(Transform target, bool snap = true)
        {
            m_Target = target;
            if (snap)
            {
                SnapToTarget();
            }
        }

        /// <summary>立即把相机放到目标位置，不经过平滑。用于进入战局时的初始化。</summary>
        public void SnapToTarget()
        {
            var desired = ResolveDesiredPosition();
            if (desired.HasValue)
            {
                transform.position = desired.Value;
                transform.rotation = Quaternion.Euler(m_PitchDegrees, 0f, 0f);
            }
        }

        private void LateUpdate()
        {
            var desired = ResolveDesiredPosition();
            if (!desired.HasValue)
            {
                return;
            }

            // 在 LateUpdate 中更新：此时角色移动已完成，可避免相机与角色在同一帧内互相追赶产生抖动。
            if (m_OcclusionPullIn || m_SmoothTime <= 0f)
            {
                // 被障碍物压近时不走平滑：慢一帧就意味着这一帧的镜头已经钻进地形里了。
                // 同时清掉速度缓存，防止残留的移动趋势把镜头又拉出去。
                transform.position = desired.Value;
                m_Velocity = Vector3.zero;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    desired.Value,
                    ref m_Velocity,
                    m_SmoothTime);
            }

            transform.rotation = Quaternion.Euler(m_PitchDegrees, 0f, 0f);
        }

        /// <summary>
        /// 计算相机应当在的位置。
        /// </summary>
        /// <remarks>
        /// 俯角与距离是极坐标参数，这里显式展开为世界坐标，而不是使用 Transform 的
        /// 局部偏移。原因是相机自身的旋转由俯角决定，若再用局部偏移定位，
        /// 旋转与位置会互相影响，调整俯角时相机会绕圈而非原地改变角度。
        /// </remarks>
        private Vector3? ResolveDesiredPosition()
        {
            if (m_Target == null)
            {
                return null;
            }

            var pitchRadians = m_PitchDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(0f, -Mathf.Sin(pitchRadians), Mathf.Cos(pitchRadians));

            var focus = m_Target.position + m_WorldOffset;
            return focus - (forward * ResolveCameraDistance(focus, forward));
        }

        /// <summary>
        /// 计算当前应当使用的相机距离：被地形或道具挡住时立即压近，脱离遮挡后平滑恢复。
        /// </summary>
        /// <param name="focus">相机看向的焦点（世界坐标）。</param>
        /// <param name="forward">相机的视线方向（由俯角决定）。</param>
        /// <remarks>
        /// <para><b>为什么要分成"快收慢放"两条节奏：</b>拉近如果也做平滑，镜头会在几帧里
        /// 停在障碍物内部（画面被墙体或箱体填满），是最容易被一眼看出来的穿帮；
        /// 反过来，拉远如果立即生效，角色每次绕过一根柱子镜头都会"弹"一下。
        /// 所以拉近直接生效，拉远复用 <see cref="m_SmoothTime"/> 平滑。</para>
        ///
        /// <para>没有遮挡时返回值恒等于配置的 <see cref="m_Distance"/>，
        /// 因此开关关闭、或场景里没有障碍物时，相机行为与加入本功能之前一致。</para>
        /// </remarks>
        private float ResolveCameraDistance(Vector3 focus, Vector3 forward)
        {
            var maxDistance = Mathf.Max(m_MinDistance, m_Distance);
            if (m_CurrentDistance < 0f)
            {
                // 首次使用时对齐 Inspector 配置：字段默认值是给新建对象用的，
                // 场景里的相机可能被调过距离，不能假设当前距离就等于配置值。
                m_CurrentDistance = maxDistance;
            }

            var desired = maxDistance;
            if (m_CollisionEnabled && TryFindOcclusion(focus, forward, maxDistance, out var hitDistance))
            {
                desired = Mathf.Clamp(hitDistance - m_CollisionMargin, m_MinDistance, maxDistance);
            }

            m_OcclusionPullIn = desired < m_CurrentDistance;
            if (m_OcclusionPullIn || m_SmoothTime <= 0f)
            {
                m_CurrentDistance = desired;
            }
            else
            {
                m_CurrentDistance = Mathf.SmoothDamp(
                    m_CurrentDistance,
                    desired,
                    ref m_DistanceVelocity,
                    m_SmoothTime);
            }

            return m_CurrentDistance;
        }

        /// <summary>
        /// 从焦点朝相机方向做球形探测，取最近的有效命中距离。
        /// </summary>
        /// <param name="focus">探测起点（焦点）。</param>
        /// <param name="forward">相机视线方向；相机位于焦点的 <c>-forward</c> 一侧。</param>
        /// <param name="maxDistance">探测的最大距离（米）。</param>
        /// <param name="hitDistance">最近的有效命中距离；无命中时为最大距离。</param>
        /// <returns>存在有效遮挡时返回 true。</returns>
        /// <remarks>
        /// <para><b>必须排除跟随目标自身及其子物体：</b>玩家/敌人的碰撞胶囊就压在焦点附近，
        /// 若不排除，最近命中永远是目标自己，相机会被自己顶到最小距离上，表现为"镜头一直是怼脸视角"。</para>
        ///
        /// <para>同时也排除目标的父级：角色的碰撞体偶尔挂在父节点上（例如以后把角色再包一层挂点），
        /// 只判断"是目标或目标的子物体"会漏掉这种情况，而漏掉的表现同样是镜头被自己顶住。</para>
        ///
        /// <para>距离为 0 的命中同样要跳过：那表示球形探测起点已经在碰撞体内部，
        /// 这个距离不代表真实的遮挡位置，用它会把相机瞬间拉到最近处。</para>
        /// </remarks>
        private bool TryFindOcclusion(Vector3 focus, Vector3 forward, float maxDistance, out float hitDistance)
        {
            hitDistance = maxDistance;

            var count = Physics.SphereCastNonAlloc(
                focus,
                m_CollisionRadius,
                -forward,
                s_OcclusionHits,
                maxDistance,
                ~0,
                QueryTriggerInteraction.Ignore);

            var nearest = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = s_OcclusionHits[i];
                if (hit.distance <= 0f)
                {
                    continue;
                }

                var hitTransform = hit.collider.transform;
                if (m_Target != null
                    && (hitTransform == m_Target
                        || hitTransform.IsChildOf(m_Target)
                        || m_Target.IsChildOf(hitTransform)))
                {
                    continue;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                }
            }

            if (float.IsPositiveInfinity(nearest))
            {
                return false;
            }

            hitDistance = nearest;
            return true;
        }
    }
}
