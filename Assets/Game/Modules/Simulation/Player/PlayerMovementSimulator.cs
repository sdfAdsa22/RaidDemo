using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 玩家移动与体力的权威模拟。
    /// </summary>
    /// <remarks>
    /// 本类是纯 C# 逻辑，不继承 MonoBehaviour，也不持有任何 Unity 对象的引用。
    /// 这意味着三件事：
    /// 一是可以在 EditMode 测试中直接构造、推进、断言，无需加载场景；
    /// 二是同一份逻辑将来可以在无头服务端上运行，客户端与服务端得到一致结果；
    /// 三是它必须由外部驱动，因此时间来源是显式传入的 deltaTime 参数，
    /// 而不是内部的 Time.deltaTime。
    /// </remarks>
    public sealed class PlayerMovementSimulator
    {
        /// <summary>移动配置。由外部持有，可在运行时调整（例如负重系统修改体力消耗）。</summary>
        private readonly PlayerMovementProfile m_Profile;

        /// <summary>距上次奔跑结束的时间。用于实现恢复延迟，负值表示尚未开始计时。</summary>
        private float m_TimeSinceSprintEnd = -1f;

        private PlayerMoveState m_State;

        /// <summary>移动碰撞世界。为 null 时不做碰撞修正（灰盒早期与纯逻辑测试会用到）。</summary>
        private readonly IMovementCollisionWorld m_CollisionWorld;

        /// <summary>移动体半径（米），传给碰撞世界做胶囊扫掠。</summary>
        private readonly float m_BodyRadius;

        /// <summary>
        /// 创建模拟器。
        /// </summary>
        /// <param name="profile">移动配置，不允许为 null。</param>
        /// <param name="initialPosition">初始位置。</param>
        /// <param name="initialFacing">初始朝向。</param>
        public PlayerMovementSimulator(
            PlayerMovementProfile profile,
            Vector2F initialPosition = default,
            Vector2F initialFacing = default,
            IMovementCollisionWorld collisionWorld = null,
            float bodyRadius = 0.4f)
        {
            m_Profile = profile ?? throw new System.ArgumentNullException(nameof(profile));
            m_State = PlayerMoveState.CreateInitial(initialPosition, initialFacing, profile.MaxStamina);
            m_CollisionWorld = collisionWorld;
            m_BodyRadius = bodyRadius > 0f ? bodyRadius : 0.4f;
        }

        /// <summary>移动体半径（米）。</summary>
        public float BodyRadius
        {
            get { return m_BodyRadius; }
        }

        /// <summary>当前使用的碰撞世界；为 null 表示不做碰撞修正。</summary>
        public IMovementCollisionWorld CollisionWorld
        {
            get { return m_CollisionWorld; }
        }

        /// <summary>当前移动状态的只读副本。</summary>
        public PlayerMoveState State => m_State;

        /// <summary>当前体力值。</summary>
        public float Stamina => m_State.Stamina;

        /// <summary>体力上限。供体力条等表现层元素换算比例。</summary>
        public float MaxStamina
        {
            get { return m_Profile.MaxStamina; }
        }

        /// <summary>是否处于力竭状态。</summary>
        public bool IsExhausted => m_State.IsExhausted;

        /// <summary>本帧是否在奔跑。</summary>
        public bool IsSprinting => m_State.IsSprinting;

        /// <summary>
        /// 推进一帧模拟。
        /// </summary>
        /// <param name="intent">
        /// 本帧的移动意图。移动方向长度表示推行力度（手柄摇杆），超过 1 会被限制。
        /// 朝向为零向量时保持当前朝向不变。
        /// </param>
        /// <param name="deltaTime">时间步长（秒）。由调用方提供，见类注释说明。</param>
        /// <param name="wantsToSprint">本帧是否请求奔跑。是否真的跑得起来还取决于体力状态。</param>
        public void Step(Vector2F moveDirection, Vector2F lookDirection, float deltaTime, bool wantsToSprint)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // 朝向由瞄准方向驱动，与移动方向完全解耦：
            // 静止时也能转动瞄准，侧向移动时也能保持朝前瞄准。
            if (!lookDirection.IsNearlyZero)
            {
                m_State.Facing = lookDirection.Normalized;
            }

            var hasMoveInput = !moveDirection.IsNearlyZero;
            // AllowSprint 是负重系统写入的能力开关：重装与超重时玩家仍可按住奔跑键，
            // 但这里直接忽略该意图。把判定放在模拟层，客户端就无法通过伪造输入绕过惩罚。
            var sprinting = m_Profile.AllowSprint
                            && wantsToSprint
                            && !m_State.IsExhausted
                            && m_State.Stamina > 0f
                            && hasMoveInput;
            var targetSpeed = sprinting
                ? m_Profile.SprintSpeed * ClampInput(moveDirection)
                : m_Profile.WalkSpeed * ClampInput(moveDirection);

            var speed = MoveTowards(m_State.CurrentSpeed, targetSpeed, m_Profile.Acceleration * deltaTime);

            if (hasMoveInput)
            {
                var direction = moveDirection.Normalized;
                var desired = direction * (speed * deltaTime);
                m_State.Position += ResolveCollision(desired);
            }

            m_State.CurrentSpeed = speed;
            m_State.IsSprinting = speed >= m_Profile.SprintSpeedThreshold;

            UpdateStamina(deltaTime, sprinting);
        }

        /// <summary>
        /// 把期望位移交给碰撞世界修正。
        /// </summary>
        /// <param name="desired">期望位移。</param>
        /// <returns>实际可执行的位移。</returns>
        /// <remarks>
        /// <para>碰撞世界为 null 时原样返回，让「没有碰撞」的世界与改动前的行为逐位一致——
        /// 这一点很重要：现有的一批移动测试断言的就是纯数学推进的结果。</para>
        ///
        /// <para>实现返回 false（被完全挡住）时使用它给出的零位移，而不是退回期望位移：
        /// 退回等于允许穿墙，那正是这次改动要修的问题。</para>
        /// </remarks>
        private Vector2F ResolveCollision(Vector2F desired)
        {
            if (m_CollisionWorld == null)
            {
                return desired;
            }

            m_CollisionWorld.TryResolveMove(m_State.Position, desired, m_BodyRadius, out var resolved);
            return resolved;
        }

        /// <summary>
        /// 直接把体力设为指定值。供测试、调试命令与存档载入使用。
        /// </summary>
        public void SetStamina(float value)
        {
            m_State.Stamina = Clamp(value, 0f, m_Profile.MaxStamina);
        }

        /// <summary>把移动状态重置到指定位置与朝向，并把体力恢复到满值。</summary>
        public void Reset(Vector2F position, Vector2F facing)
        {
            m_State = PlayerMoveState.CreateInitial(position, facing, m_Profile.MaxStamina);
            m_TimeSinceSprintEnd = -1f;
        }

        /// <summary>限制输入长度上限为 1，防止斜向输入获得额外速度。</summary>
        private static float ClampInput(Vector2F intent)
        {
            var magnitude = intent.Magnitude;
            return magnitude > 1f ? 1f : magnitude;
        }

        /// <summary>
        /// 体力更新。包含消耗、延迟恢复与力竭状态转换三部分。
        /// </summary>
        private void UpdateStamina(float deltaTime, bool sprinting)
        {
            if (sprinting)
            {
                m_State.Stamina -= m_Profile.StaminaDrainPerSecond * deltaTime;
                m_TimeSinceSprintEnd = -1f;

                if (m_State.Stamina <= 0f)
                {
                    m_State.Stamina = 0f;

                    // 归零即进入力竭。注意这里不重置计时器：
                    // 体力为 0 时无法奔跑，恢复计时会从下一帧的恢复分支开始。
                    m_State.IsExhausted = true;
                }

                return;
            }

            // 未奔跑：先累计延迟时间，再决定是否恢复。
            m_TimeSinceSprintEnd += deltaTime;
            var delay = m_State.IsExhausted
                ? m_Profile.ExhaustedRegenDelay
                : m_Profile.StaminaRegenDelay;

            if (m_TimeSinceSprintEnd < delay)
            {
                return;
            }

            if (m_State.Stamina >= m_Profile.MaxStamina)
            {
                m_State.Stamina = m_Profile.MaxStamina;
                return;
            }

            m_State.Stamina += m_Profile.StaminaRegenPerSecond * deltaTime;
            if (m_State.Stamina > m_Profile.MaxStamina)
            {
                m_State.Stamina = m_Profile.MaxStamina;
            }

            if (m_State.IsExhausted && m_State.Stamina >= m_Profile.ExhaustedRecoveryThreshold)
            {
                m_State.IsExhausted = false;
            }
        }

        /// <summary>把 current 以不超过 maxDelta 的步长移向 target。</summary>
        private static float MoveTowards(float current, float target, float maxDelta)
        {
            var difference = target - current;
            if (difference > maxDelta)
            {
                return current + maxDelta;
            }

            if (difference < -maxDelta)
            {
                return current - maxDelta;
            }

            return target;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
