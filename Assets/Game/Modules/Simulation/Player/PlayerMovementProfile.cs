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
