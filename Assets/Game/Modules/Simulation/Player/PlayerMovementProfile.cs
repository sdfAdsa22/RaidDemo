using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 移动与体力的配置参数。
    /// </summary>
    /// <remarks>
    /// 把参数集中在普通 C# 类中而非 ScriptableObject，原因有两个：
    /// 一是本程序集不引用 UnityEngine，无法使用 ScriptableObject；
    /// 二是服务端需要同一份配置运行权威模拟，普通类可在无头环境直接构造。
    /// 表现层负责把 ScriptableObject 资产转换成这个配置对象。
    ///
    /// 参数集中提供 ResetToDefault 而不是散落在字段声明处，
    /// 是为了让测试之间隔离与运行时还原有一处统一入口，避免漏改。
    /// </remarks>
    public sealed class PlayerMovementProfile
    {
        /// <summary>步行速度（单位/秒）。</summary>
        public float WalkSpeed = 3.5f;

        /// <summary>奔跑速度（单位/秒）。</summary>
        public float SprintSpeed = 6.5f;

        /// <summary>
        /// 速度变化率（单位/秒 squared）。
        /// 默认 200 时接近瞬时响应，手感更利落，符合本项目对标的快节奏基调。
        /// 若未来希望增加惯性感，向下调整即可，无需改动逻辑。
        /// </summary>
        public float Acceleration = 200f;

        /// <summary>速度高于该值时判定为奔跑。用于驱动噪音半径与表现层反馈。</summary>
        public float SprintSpeedThreshold = 4.5f;

        /// <summary>
        /// 是否允许奔跑。由负重系统写入——重装与超重状态下为 false。
        /// </summary>
        /// <remarks>
        /// 这里保存的是**能力**而不是**意愿**：玩家仍然可以按住奔跑键，模拟层直接忽略该意图。
        /// 把判断放在模拟层而不是输入层，是为了让客户端无法通过伪造输入绕过负重惩罚；
        /// 联机时同一个判定在服务端执行，两边结果必然一致。
        /// </remarks>
        public bool AllowSprint = true;

        /// <summary>体力上限。</summary>
        public float MaxStamina = 100f;

        /// <summary>奔跑时每秒消耗的体力。</summary>
        public float StaminaDrainPerSecond = 20f;

        /// <summary>
        /// 停止奔跑后开始恢复体力所需的延迟（秒）。
        /// 设置延迟是为了防止跑一步歇一步的抖动式操作钻空子：
        /// 若没有延迟，玩家可以通过高频点按奔跑来无限续航。
        /// </summary>
        public float StaminaRegenDelay = 1.5f;

        /// <summary>体力每秒恢复量。</summary>
        public float StaminaRegenPerSecond = 20f;

        /// <summary>
        /// 力竭状态下的恢复延迟（秒），显著长于常规延迟。
        /// 这是让力竭区别于喘口气的关键参数：体力耗尽后必须承受一段明确的恢复期，
        /// 期间完全无法奔跑。取值过小会让力竭失去惩罚意义。
        /// </summary>
        public float ExhaustedRegenDelay = 3f;

        /// <summary>
        /// 力竭解除阈值。
        /// 体力归零后进入力竭状态，必须恢复到该值才能再次奔跑。
        /// 这是整个体力系统的核心：让体力耗尽成为需要立即应对的处境，
        /// 而不是休息一秒就能继续跑的软限制。
        /// </summary>
        public float ExhaustedRecoveryThreshold = 30f;

        /// <summary>
        /// 是否已经记录过基准值。未记录时第一次应用修正会顺带记录。
        /// </summary>
        private bool m_HasBaseline;

        /// <summary>基准步行速度。负重修正始终基于基准值计算，避免多次叠加后越乘越小。</summary>
        private float m_BaseWalkSpeed;

        /// <summary>基准奔跑速度。</summary>
        private float m_BaseSprintSpeed;

        /// <summary>基准奔跑判定阈值。</summary>
        private float m_BaseSprintSpeedThreshold;

        /// <summary>基准体力恢复速度。</summary>
        private float m_BaseStaminaRegenPerSecond;

        /// <summary>
        /// 记录当前数值为基准值。负重修正的所有乘法都以基准值为起点。
        /// </summary>
        /// <remarks>
        /// 之所以需要基准值，是因为负重状态会随玩家拾取与丢弃而反复变化。
        /// 若每次修正都直接乘在当前值上，一次「重装 → 轻装」的往返就会让速度永久偏低，
        /// 而这类错误在实机上极难察觉——它看起来只是"手感越来越沉"。
        /// </remarks>
        public void CaptureBaseline()
        {
            m_BaseWalkSpeed = WalkSpeed;
            m_BaseSprintSpeed = SprintSpeed;
            m_BaseSprintSpeedThreshold = SprintSpeedThreshold;
            m_BaseStaminaRegenPerSecond = StaminaRegenPerSecond;
            m_HasBaseline = true;
        }

        /// <summary>
        /// 应用一组移动修正（来自负重系统）。
        /// </summary>
        /// <param name="modifiers">
        /// 三个倍率与一个开关。倍率基于基准值相乘，因此本方法可以安全地反复调用。
        /// </param>
        /// <remarks>
        /// 本方法是**整个项目里负重与移动唯一相遇的地方**：`Simulation` 不需要知道背包存在，
        /// `Inventory` 也不需要知道移动存在，两者只在启动层的装配代码中通过这个调用相连。
        /// </remarks>
        public void ApplyModifiers(in MovementModifiers modifiers)
        {
            if (!m_HasBaseline)
            {
                CaptureBaseline();
            }

            WalkSpeed = m_BaseWalkSpeed * modifiers.SpeedMultiplier;
            SprintSpeed = m_BaseSprintSpeed * modifiers.SpeedMultiplier;

            // 阈值也必须同比缩放：否则速度降到 0.4 倍时阈值仍留在原位，
            // 会出现"速度早已达到奔跑水平、判定却认为在走路"的错位。
            SprintSpeedThreshold = m_BaseSprintSpeedThreshold * modifiers.SpeedMultiplier;
            StaminaRegenPerSecond = m_BaseStaminaRegenPerSecond * modifiers.StaminaRegenMultiplier;
            AllowSprint = modifiers.CanSprint;
        }

        /// <summary>清除全部负重修正，恢复到基准数值。</summary>
        public void ClearModifiers()
        {
            if (m_HasBaseline)
            {
                WalkSpeed = m_BaseWalkSpeed;
                SprintSpeed = m_BaseSprintSpeed;
                SprintSpeedThreshold = m_BaseSprintSpeedThreshold;
                StaminaRegenPerSecond = m_BaseStaminaRegenPerSecond;
            }

            AllowSprint = true;
        }

        /// <summary>把全部参数恢复为默认值。</summary>
        public void ResetToDefault()
        {
            WalkSpeed = 3.5f;
            SprintSpeed = 6.5f;
            Acceleration = 200f;
            SprintSpeedThreshold = 4.5f;
            MaxStamina = 100f;
            StaminaDrainPerSecond = 20f;
            StaminaRegenDelay = 1.5f;
            StaminaRegenPerSecond = 20f;
            ExhaustedRegenDelay = 3f;
            ExhaustedRecoveryThreshold = 30f;
            AllowSprint = true;
            m_HasBaseline = false;
        }

        /// <summary>
        /// 校验参数是否处于合理范围，用于在启动阶段尽早发现配置错误。
        /// </summary>
        /// <returns>校验通过返回 null，否则返回描述问题的信息。</returns>
        public string Validate()
        {
            if (WalkSpeed <= 0f)
            {
                return "WalkSpeed 必须大于 0。";
            }

            if (SprintSpeed <= WalkSpeed)
            {
                return "SprintSpeed 必须大于 WalkSpeed，否则奔跑没有意义。";
            }

            if (MaxStamina <= 0f)
            {
                return "MaxStamina 必须大于 0。";
            }

            if (ExhaustedRecoveryThreshold < 0f || ExhaustedRecoveryThreshold > MaxStamina)
            {
                return "ExhaustedRecoveryThreshold 必须落在 0 到 MaxStamina 之间。";
            }

            if (ExhaustedRegenDelay < StaminaRegenDelay)
            {
                return "ExhaustedRegenDelay 不应短于 StaminaRegenDelay，否则力竭会失去惩罚意义。";
            }

            if (SprintSpeedThreshold <= WalkSpeed || SprintSpeedThreshold > SprintSpeed)
            {
                return "SprintSpeedThreshold 必须落在 WalkSpeed 与 SprintSpeed 之间，否则奔跑判定会失真。";
            }

            return null;
        }
    }
}
