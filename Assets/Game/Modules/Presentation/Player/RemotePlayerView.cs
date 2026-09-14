using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 远端玩家的表现：位置与朝向来自插值结果，动画参数按与本地玩家相同的规则写入。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用本地玩家那套事件链路：</b>本地玩家通过 <c>PlayerMovementChanged</c>
    /// 事件驱动，那条链路面向"我自己"；远端玩家的数据来自网络快照，
    /// 每帧直接喂给视图更直接，也避免让所有角色视图都去订阅总线上每一名玩家的移动事件。</para>
    ///
    /// <para><b>与本地玩家的表现保持一致的部分：</b>坐标系映射（Unity 旋转角 = 90 − 模拟角度）
    /// 与动画参数规则（速度、奔跑状态、播放倍率）都照抄本地玩家的实现——
    /// 两边不一致的后果是"队友走路的样子和我自己不一样"，一眼就能看出来。</para>
    ///
    /// <para><b>朝向不做插值：</b>俯视角下朝向是核心信息（判断队友在看哪边），
    /// 本地玩家已因此改为直接赋值，远端保持一致。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RemotePlayerView : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int SprintingId = Animator.StringToHash("Sprinting");
        private static readonly int ArmedId = Animator.StringToHash("Armed");
        private static readonly int LocomotionRateId =
            Animator.StringToHash(LocomotionAnimationBinding.RateParameterName);

        /// <summary>落地跟随的平滑系数（每秒）。</summary>
        private const float GroundFollowRate = 12f;

        private Animator m_Animator;
        private LocomotionAnimationBinding m_Binding;
        private float m_GroundHeight;
        private bool m_HasGroundHeight;

        /// <summary>该视图对应的玩家标识。</summary>
        public int PlayerId { get; private set; }

        /// <summary>最近一次写入的移动动画播放倍率（调试用）。</summary>
        public float CurrentPlaybackRate { get; private set; } = 1f;

        /// <summary>
        /// 创建一个远端玩家视图。
        /// </summary>
        /// <param name="characterPrefab">角色外观预制体；为 null 时退回灰盒胶囊。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="initialPosition">初始世界位置。</param>
        public static RemotePlayerView Create(GameObject characterPrefab, int playerId, Vector3 initialPosition)
        {
            var host = new GameObject($"RemotePlayer_{playerId}");
            host.transform.position = initialPosition;

            var view = host.AddComponent<RemotePlayerView>();
            view.PlayerId = playerId;
            view.BindVisual(characterPrefab);
            return view;
        }

        /// <summary>
        /// 应用一帧的插值状态。
        /// </summary>
        /// <param name="state">插值后的移动状态。</param>
        /// <param name="deltaTime">帧间隔，用于地面高度平滑。</param>
        /// <param name="armed">是否显示持枪姿态。P1 尚未同步装备，统一按持枪显示。</param>
        public void Apply(in PlayerMoveState state, float deltaTime, bool armed = true)
        {
            var ground = ResolveGroundHeight(state, deltaTime);
            transform.position = new Vector3(state.Position.X, ground, state.Position.Y);

            if (!state.Facing.IsNearlyZero)
            {
                var degrees = Mathf.Atan2(state.Facing.Y, state.Facing.X) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 90f - degrees, 0f);
            }

            if (m_Animator == null)
            {
                return;
            }

            m_Animator.SetFloat(SpeedId, state.CurrentSpeed);
            m_Animator.SetBool(SprintingId, state.IsSprinting);
            m_Animator.SetBool(ArmedId, armed);

            var designSpeed = m_Binding != null
                ? (state.IsSprinting ? m_Binding.RunDesignSpeed : m_Binding.WalkDesignSpeed)
                : 0f;
            CurrentPlaybackRate = LocomotionAnimationRate.Calculate(state.CurrentSpeed, designSpeed);
            m_Animator.SetFloat(LocomotionRateId, CurrentPlaybackRate);
        }

        /// <summary>
        /// 解析脚下地面高度。
        /// </summary>
        /// <remarks>
        /// 与本地玩家同源但独立实现：本地那份绑着"模拟位置"字段，远端只有网络来的坐标。
        /// 取最高命中而不是最近命中，理由与本地一致——坡道与台面衔接处要站在较高的那个面上。
        /// </remarks>
        private float ResolveGroundHeight(in PlayerMoveState state, float deltaTime)
        {
            var plane = new RaidDemo.Shared.Vector2F(state.Position.X, state.Position.Y);
            var last = m_HasGroundHeight ? m_GroundHeight : transform.position.y;

            // 用与本地玩家、服务器**同一份**采样规则（GroundProbe）：
            // 曾经这里另写了一套"取最高命中"的探测，缺了"朝上才算地面""单次抬升上限 0.35 米"
            // 与"排除自身碰撞体"三条规则，结果是远端角色在集装箱/栅栏/坡体旁边被抬到更高的面上——
            // 队友看你时你浮在天上，而你自己看自己是正常的（P4 的用户反馈）。
            var sampled = GroundProbe.SampleGroundHeight(plane, transform.position.y, last, transform);

            if (!m_HasGroundHeight)
            {
                m_HasGroundHeight = true;
                m_GroundHeight = sampled;
                return sampled;
            }

            m_GroundHeight = Mathf.Lerp(m_GroundHeight, sampled, Mathf.Clamp01(deltaTime * GroundFollowRate));
            return m_GroundHeight;
        }

        /// <summary>实例化角色外观并缓存动画组件；预制体缺失时退回胶囊。</summary>
        private void BindVisual(GameObject characterPrefab)
        {
            if (characterPrefab == null)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "RemotePlayerPlaceholder";
                capsule.transform.SetParent(transform, false);
                capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                Destroy(capsule.GetComponent<Collider>());

                // 占位物也必须用工程自己的 URP 材质：内置默认材质在构建版里是洋红（missing shader）。
                PresentationFallback.Apply(capsule);
                return;
            }

            var instance = Instantiate(characterPrefab, transform, false);
            instance.name = characterPrefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            m_Animator = instance.GetComponentInChildren<Animator>(true);
            m_Binding = m_Animator != null
                ? m_Animator.GetComponent<LocomotionAnimationBinding>()
                : null;
        }
    }
}
