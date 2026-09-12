using System;
using RaidDemo.Kernel;
using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 把移动模拟的结果应用到角色的 Transform 上。
    /// </summary>
    /// <remarks>
    /// 本组件是模拟层到表现层的适配器，属于整个项目中最薄的一层。
    /// 它不含任何移动规则：速度、体力、朝向全部由 PlayerMovementSimulator 决定，
    /// 这里只负责把结果搬运到 Unity 的 Transform 上。
    ///
    /// 之所以这样切分，是因为移动规则需要能在无头服务端运行，
    /// 而 Transform 只在客户端存在。把两者混在一起会让服务端无法复用移动逻辑。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerMotor : MonoBehaviour
    {
        /// <summary>高度偏移，使角色模型底部贴合地面。</summary>
        [SerializeField] private float m_GroundOffset;

        /// <summary>
        /// 是否自动吸附到脚下地面。
        /// </summary>
        /// <remarks>
        /// M5 引入装卸平台与坡道之后，角色不能再假设自己永远站在 y 等于 0 的平面上。
        /// 关掉它可以回到「固定高度」的旧行为，便于对照排查问题。
        /// </remarks>
        [SerializeField] private bool m_SnapToGround = true;

        /// <summary>
        /// 地面探测的起始高度（米）。探测从角色头顶上方这么高的位置向下打射线，总长是它的两倍。
        /// </summary>
        /// <remarks>
        /// M7 批次 2 把地图改成下沉盆地后，地形高差达到 6 米（谷底 -6、塬面 0），
        /// 原来的 4 米只覆盖 8 米总长，站在塬面边缘走下坡道时可能探不到脚下的地面。
        /// 提到 8 米（总长 16 米）后，无论是从装卸平台跳下还是从塬面沿坡道下行，
        /// 射线都能命中真正的地面。向上方向的过滤仍由 <see cref="MaxStepUpHeight"/> 负责，
        /// 因此「栅栏顶被当成地面」这类问题不会因为探得更远而复现。
        /// </remarks>
        [SerializeField] private float m_GroundProbeHeight = 8f;

        /// <summary>
        /// 地面吸附允许的最大抬升高度（米）。
        /// </summary>
        /// <remarks>
        /// 这个值必须与 <see cref="PhysicsMovementCollisionService"/> 的台阶高度一致。
        /// 没有它时，射线会命中栅栏顶部（2.2 米）或平台顶面，把角色直接吸到障碍物上方；
        /// 有了它，只有脚下极近的地面才会被当成落脚点，栅栏顶不再是"地面"。
        /// </remarks>
        private const float MaxStepUpHeight = 0.35f;

        /// <summary>所属玩家编号，用于过滤事件。</summary>
        [SerializeField] private int m_PlayerId;

        private EventBus m_EventBus;
        private IDisposable m_Subscription;

        /// <summary>
        /// 地面射线的复用缓冲。
        /// </summary>
        /// <remarks>
        /// 用 RaycastNonAlloc 而不是 RaycastAll：后者每次调用都会分配一个新数组，
        /// 而本方法每帧都会执行，长期累积会让 GC 周期性触发，表现为奔跑时轻微卡顿。
        /// </remarks>
        private readonly RaycastHit[] m_GroundHits = new RaycastHit[8];

        /// <summary>上一次吸附到的地面高度。台阶过滤以它为基准，避免视觉插值拖慢判定。</summary>
        private float m_LastGroundHeight;

        /// <summary>当前模拟位置（水平面）。</summary>
        public Vector2 SimulatedPosition { get; private set; }

        /// <summary>当前朝向角度（度）。</summary>
        public float FacingDegrees { get; private set; }

        private void Awake()
        {
            m_LastGroundHeight = transform.position.y - m_GroundOffset;
        }

        private void OnEnable()
        {
            // 事件总线由场景启动流程注册。若此时尚未就绪则跳过，
            // SceneBootstrap 会在初始化完成后调用 Rebind 补上订阅。
            if (!ServiceLocatorHolder.TryGet(out m_EventBus))
            {
                return;
            }

            m_Subscription = m_EventBus.Subscribe<PlayerMovementChanged>(OnMovementChanged);
        }

        private void OnDisable()
        {
            m_Subscription?.Dispose();
            m_Subscription = null;
        }

        /// <summary>在事件总线就绪后重新建立订阅。由场景启动流程调用。</summary>
        public void Rebind(EventBus eventBus)
        {
            m_Subscription?.Dispose();
            m_EventBus = eventBus;
            m_Subscription = m_EventBus.Subscribe<PlayerMovementChanged>(OnMovementChanged);
        }

        /// <summary>直接把位置与朝向设置到目标值，不经过插值。用于场景初始化与传送。</summary>
        public void SnapTo(Vector2 position, Vector2 facing)
        {
            SimulatedPosition = position;
            FacingDegrees = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            // 传送可能跨越高度差，先做一次不受台阶限制的采样，避免新位置被旧高度过滤掉。
            m_LastGroundHeight = SampleGroundHeight(float.PositiveInfinity);
            ApplyTransform(instant: true);
        }

        private void OnMovementChanged(PlayerMovementChanged evt)
        {
            if (evt.PlayerId != m_PlayerId)
            {
                return;
            }

            SimulatedPosition = new Vector2(evt.Position.X, evt.Position.Y);

            if (!evt.Facing.IsNearlyZero)
            {
                // 朝向直接采用模拟层的结果，不做插值。
                //
                // 这里曾经按角速度插值，结果出现了明显的错位：目标朝向变化后，
                // 模型要过若干帧才转过去，而准星是即时的，两者看起来就不在一条线上。
                // 更糟的是每帧推进量取决于 deltaTime——当编辑器失焦导致 deltaTime 极小时，
                // 每帧只能转不到一度，模型会长时间停在错误朝向上。
                //
                // 俯视角下朝向是核心反馈（玩家靠它判断自己在瞄哪里），
                // 因此宁可牺牲一点圆滑，也要保证即时准确。
                FacingDegrees = Mathf.Atan2(evt.Facing.Y, evt.Facing.X) * Mathf.Rad2Deg;
            }

            ApplyTransform(instant: false);
        }

        private void ApplyTransform(bool instant)
        {
            var target = new Vector3(SimulatedPosition.x, ResolveGroundHeight(), SimulatedPosition.y);
            transform.position = instant ? target : Vector3.Lerp(transform.position, target, 0.5f);

            // 坐标轴映射说明（这段映射容易搞错，特此写明推导依据）：
            //
            // 模拟层是二维 XY 平面：0 度指向 +X，90 度指向 +Y。
            // Unity 场景是 XZ 水平面，相机从 -Z 方向朝 +Z 俯视，因此：
            //   屏幕上方 = 世界 +Z，屏幕右方 = 世界 +X。
            //
            // 角色的占位模型朝向是本地 +Z（即模型的"鼻子"在 +Z），
            // 要让模型指向模拟层 +X 方向，需要把本地 +Z 转到世界 +Z，即旋转 90 度；
            // 要指向模拟层 +Y 方向，需要把本地 +Z 转到世界 +X，即旋转 0 度。
            //
            // 由此得到映射关系：Unity 旋转角 = 90 - 模拟角度。
            //
            // 历史记录：这里先后写错过两次——第一次多取了一个负号（差 180 度），
            // 第二次误以为两个坐标系轴向一一对应而直接使用模拟角度（差 90 度）。
            // 两次都表现为"角色朝向与准星不在一条线上"。
            transform.rotation = Quaternion.Euler(0f, 90f - FacingDegrees, 0f);
        }

        /// <summary>
        /// 求模拟位置脚下的地面高度。
        /// </summary>
        /// <returns>角色脚底应处的世界高度。</returns>
        /// <remarks>
        /// <para>地面高度由射线探测得出，而不是由移动模拟层提供：模拟层要能在无头服务端运行，
        /// 那里不存在碰撞体。高度属于场景信息，只能由表现层补上。</para>
        ///
        /// <para>射线要排除两类「假地面」：角色自己的胶囊，以及站在同一位置的敌人。
        /// 若不排除，玩家贴着一个敌人时会突然被抬高到对方头顶——
        /// 在俯视角下看起来像是被弹飞了。取最高命中点而不是最近命中点，
        /// 是为了在坡道与台面衔接处站在较高的那个面上，避免角色半个身子陷进台体。</para>
        /// </remarks>
        private float ResolveGroundHeight()
        {
            if (!m_SnapToGround)
            {
                return m_GroundOffset;
            }

            m_LastGroundHeight = SampleGroundHeight(m_LastGroundHeight + MaxStepUpHeight);
            return m_LastGroundHeight + m_GroundOffset;
        }

        /// <summary>
        /// 采样脚下地面高度。
        /// </summary>
        /// <param name="maxHeight">允许的最高落点。高于它的命中会被忽略。</param>
        private float SampleGroundHeight(float maxHeight)
        {
            var origin = new Vector3(
                SimulatedPosition.x,
                Mathf.Max(transform.position.y, m_LastGroundHeight) + m_GroundProbeHeight,
                SimulatedPosition.y);
            var count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                m_GroundHits,
                m_GroundProbeHeight * 2f,
                ~0,
                QueryTriggerInteraction.Ignore);

            var best = float.NegativeInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = m_GroundHits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.collider.transform == transform
                    || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.collider.GetComponentInParent<EnemyAgentView>() != null)
                {
                    continue;
                }

                // 只接受朝上的可站立面；竖直墙面即使被射线擦到也不算地面。
                if (hit.normal.y < 0.5f)
                {
                    continue;
                }

                if (hit.point.y > maxHeight)
                {
                    continue;
                }

                if (hit.point.y > best)
                {
                    best = hit.point.y;
                }
            }

            // 什么都没命中时保持上一次高度，避免角色掉进没有碰撞体的空隙里。
            return best > float.NegativeInfinity ? best : m_LastGroundHeight;
        }
    }
}
