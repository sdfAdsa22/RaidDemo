using NUnit.Framework;
using RaidDemo.Shared;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 负重修正对移动配置的作用测试。
    /// </summary>
    /// <remarks>
    /// 这里验证的是 M1（移动）与 M2（负重）之间的**唯一接口**：
    /// 负重系统不直接改模拟逻辑，只把一组倍率写进配置；模拟层再按新配置运行。
    /// 把这些断言独立成文件，是为了让"负重是否真的影响到了移动"这件事有明确的证据，
    /// 而不是靠实机试玩时"感觉好像慢了一点"来判断。
    /// </remarks>
    [TestFixture]
    public sealed class PlayerMovementModifierTests
    {
        /// <summary>测试用的固定时间步长，与目标帧率一致。</summary>
        private const float Step = 1f / 60f;

        private PlayerMovementProfile m_Profile;
        private PlayerMovementSimulator m_Simulator;

        /// <summary>记录基准值，供断言比较使用。</summary>
        private float m_BaseWalkSpeed;

        private float m_BaseSprintSpeed;
        private float m_BaseSprintThreshold;
        private float m_BaseRegen;

        [SetUp]
        public void SetUp()
        {
            m_Profile = new PlayerMovementProfile();
            m_BaseWalkSpeed = m_Profile.WalkSpeed;
            m_BaseSprintSpeed = m_Profile.SprintSpeed;
            m_BaseSprintThreshold = m_Profile.SprintSpeedThreshold;
            m_BaseRegen = m_Profile.StaminaRegenPerSecond;
            m_Simulator = new PlayerMovementSimulator(m_Profile);
        }

        /// <summary>推进指定的游戏时间。</summary>
        private void Advance(float seconds, Vector2F move, bool sprint = false)
        {
            AdvanceSteps((int)(seconds / Step), move, sprint);
        }

        /// <summary>
        /// 按固定步数推进。
        /// </summary>
        /// <remarks>
        /// 恢复速度的断言必须按**步数**而不是按秒数推算期望值：
        /// 秒数换算成步数时会因浮点截断少走一步，期望值随之偏移，
        /// 这类误差在 1e-2 的容差下足以让一个正确的实现判为失败。
        /// </remarks>
        private void AdvanceSteps(int steps, Vector2F move, bool sprint = false)
        {
            for (var i = 0; i < steps; i++)
            {
                m_Simulator.Step(move, Vector2F.Zero, Step, sprint);
            }
        }

        [Test]
        public void ApplyModifiers_ScalesWalkAndSprintSpeed()
        {
            m_Profile.ApplyModifiers(new MovementModifiers(0.5f, 1f, true));

            Assert.AreEqual(m_BaseWalkSpeed * 0.5f, m_Profile.WalkSpeed, 1e-4f,
                "步行速度应等于基准值乘以速度倍率。");
            Assert.AreEqual(m_BaseSprintSpeed * 0.5f, m_Profile.SprintSpeed, 1e-4f,
                "奔跑速度应等于基准值乘以速度倍率。");
        }

        [Test]
        public void ApplyModifiers_ScalesSprintThreshold()
        {
            m_Profile.ApplyModifiers(new MovementModifiers(0.5f, 1f, true));

            Assert.AreEqual(m_BaseSprintThreshold * 0.5f, m_Profile.SprintSpeedThreshold, 1e-4f,
                "奔跑判定阈值必须同比缩放，否则降速后会把走路误判成奔跑。");
        }

        [Test]
        public void ApplyModifiers_AppliedTwice_DoesNotCompound()
        {
            var modifiers = new MovementModifiers(0.5f, 1f, true);

            m_Profile.ApplyModifiers(modifiers);
            m_Profile.ApplyModifiers(modifiers);

            Assert.AreEqual(m_BaseWalkSpeed * 0.5f, m_Profile.WalkSpeed, 1e-4f,
                "重复应用同一组修正不应该叠加——负重状态每帧都可能重算，叠加会让人物越走越慢。");
        }

        [Test]
        public void ClearModifiers_RestoresBaseline()
        {
            m_Profile.ApplyModifiers(new MovementModifiers(0.4f, 0f, false));

            m_Profile.ClearModifiers();

            Assert.AreEqual(m_BaseWalkSpeed, m_Profile.WalkSpeed, 1e-4f, "清除修正后应恢复基准步行速度。");
            Assert.AreEqual(m_BaseSprintSpeed, m_Profile.SprintSpeed, 1e-4f, "清除修正后应恢复基准奔跑速度。");
            Assert.AreEqual(m_BaseRegen, m_Profile.StaminaRegenPerSecond, 1e-4f, "清除修正后应恢复基准体力恢复速度。");
            Assert.IsTrue(m_Profile.AllowSprint, "清除修正后应重新允许奔跑。");
        }

        [Test]
        public void ApplyModifiers_WithSprintDisabled_IgnoresSprintIntent()
        {
            m_Profile.ApplyModifiers(new MovementModifiers(1f, 1f, false));

            Advance(1f, Vector2F.Right, sprint: true);

            Assert.IsFalse(m_Simulator.IsSprinting,
                "负重系统禁止奔跑时，即使玩家按住奔跑键也不应判定为奔跑。");
            Assert.AreEqual(m_BaseWalkSpeed, m_Simulator.State.CurrentSpeed, 0.1f,
                "被禁止奔跑时速度应停留在步行水平。");
        }

        [Test]
        public void ApplyModifiers_WithZeroRegen_KeepsStaminaFlat()
        {
            m_Simulator.SetStamina(50f);
            m_Profile.ApplyModifiers(new MovementModifiers(1f, 0f, false));

            // 先越过恢复延迟，再推进一整秒。若不断言「越过延迟之后」的状态，
            // 仅仅因为处于延迟期内没恢复，就会让一个坏实现也能通过。
            AdvanceSteps(RecoveryDelaySteps() + 60, Vector2F.Zero);

            Assert.AreEqual(50f, m_Simulator.Stamina, 1e-3f,
                "超重状态下体力不回复，越过恢复延迟后体力值应保持不变。");
        }

        [Test]
        public void ApplyModifiers_WithHalfRegen_HalvesRecovery()
        {
            m_Simulator.SetStamina(0f);
            m_Profile.ApplyModifiers(new MovementModifiers(1f, 0.5f, true));

            // 先推进足够长的时间，确保恢复确实已经开始，再记录起点。
            // 不这样做的话，断言会因为"恰好还在延迟期内"而误判——
            // 恢复计时器的初值是 -1，实际开始恢复的时刻比 StaminaRegenDelay 晚 1 秒。
            AdvanceSteps(RecoveryDelaySteps() + 120, Vector2F.Zero);
            var before = m_Simulator.Stamina;
            Assert.Greater(before, 0f, "越过恢复延迟后体力应当已经开始恢复。");

            const int RegenSteps = 60;
            AdvanceSteps(RegenSteps, Vector2F.Zero);

            var expected = before + m_BaseRegen * 0.5f * Step * RegenSteps;
            Assert.AreEqual(expected, m_Simulator.Stamina, 1e-2f,
                "重装状态下体力恢复速度应为基准值的一半。");
        }

        /// <summary>恢复延迟对应的步数，向上取整以避免恰好卡在边界上。</summary>
        private int RecoveryDelaySteps()
        {
            return (int)(m_Profile.StaminaRegenDelay / Step) + 1;
        }
    }
}
