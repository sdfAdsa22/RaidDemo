using NUnit.Framework;
using RaidDemo.Shared;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 玩家移动与体力的行为测试。
    /// </summary>
    /// <remarks>
    /// 这些用例全部在 EditMode 下运行，不加载任何场景，也不依赖 Unity 的 Update 循环。
    /// 能做到这一点，是因为移动逻辑被刻意实现为纯 C# 类，时间由调用方显式传入。
    /// </remarks>
    [TestFixture]
    public sealed class PlayerMovementTests
    {
        /// <summary>测试用的固定时间步长，与目标帧率一致。</summary>
        private const float Step = 1f / 60f;

        private PlayerMovementProfile m_Profile;
        private PlayerMovementSimulator m_Simulator;

        [SetUp]
        public void SetUp()
        {
            m_Profile = new PlayerMovementProfile();
            m_Simulator = new PlayerMovementSimulator(m_Profile);
        }

        /// <summary>推进指定的游戏时间。</summary>
        private void Advance(float seconds, Vector2F move, bool sprint = false)
        {
            var steps = (int)(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                m_Simulator.Step(move, Vector2F.Zero, Step, sprint);
            }
        }

        [Test]
        public void Step_WithNoInput_DoesNotMove()
        {
            var start = m_Simulator.State.Position;

            Advance(1f, Vector2F.Zero);

            Assert.AreEqual(start.X, m_Simulator.State.Position.X, 1e-4f);
            Assert.AreEqual(start.Y, m_Simulator.State.Position.Y, 1e-4f);
            Assert.AreEqual(0f, m_Simulator.State.CurrentSpeed, 1e-3f);
        }

        [Test]
        public void Step_WithForwardInput_MovesAlongThatDirection()
        {
            Advance(1f, Vector2F.Right);

            Assert.Greater(m_Simulator.State.Position.X, 1f, "持续向右输入应当使角色向 X 轴正方向移动。");
            Assert.AreEqual(0f, m_Simulator.State.Position.Y, 1e-3f, "垂直方向不应产生位移。");
        }

        [Test]
        public void Step_WalkSpeed_ApproximatesConfiguredValue()
        {
            Advance(2f, Vector2F.Right);

            Assert.AreEqual(m_Profile.WalkSpeed, m_Simulator.State.CurrentSpeed, 0.05f);
        }

        [Test]
        public void Step_SprintSpeed_IsFasterThanWalk()
        {
            Advance(1f, Vector2F.Right, sprint: true);

            Assert.AreEqual(m_Profile.SprintSpeed, m_Simulator.State.CurrentSpeed, 0.05f);
            Assert.IsTrue(m_Simulator.State.IsSprinting, "奔跑时应当被标记为奔跑状态。");
        }

        /// <summary>
        /// 斜向移动不应比直线移动更快。
        /// </summary>
        /// <remarks>
        /// 这是俯视角游戏中最经典的移动缺陷：若不对角速度做限制，
        /// 玩家会习惯性地斜着走以获取约 41% 的速度优势。
        /// </remarks>
        [Test]
        public void Step_DiagonalInput_DoesNotExceedStraightSpeed()
        {
            var straight = new PlayerMovementSimulator(new PlayerMovementProfile());
            var diagonal = new PlayerMovementSimulator(new PlayerMovementProfile());

            for (var i = 0; i < 120; i++)
            {
                straight.Step(Vector2F.Right, Vector2F.Zero, Step, false);
                diagonal.Step(new Vector2F(1f, 1f), Vector2F.Zero, Step, false);
            }

            Assert.AreEqual(
                straight.State.CurrentSpeed,
                diagonal.State.CurrentSpeed,
                0.05f,
                "斜向输入的速度必须与直线一致，否则斜走会成为最优解。");
        }

        /// <summary>
        /// 朝向由瞄准方向决定，与移动方向无关。
        /// </summary>
        [Test]
        public void Step_FacingFollowsLookDirection_NotMoveDirection()
        {
            // 向右侧移动，但朝向上方瞄准。
            m_Simulator.Step(Vector2F.Right, Vector2F.Up, Step, false);

            Assert.AreEqual(0f, m_Simulator.State.Facing.X, 1e-3f);
            Assert.AreEqual(1f, m_Simulator.State.Facing.Y, 1e-3f);
            Assert.Greater(m_Simulator.State.Position.X, 0f, "移动方向应当仍然生效。");
        }

        [Test]
        public void Step_ZeroLookDirection_KeepsCurrentFacing()
        {
            m_Simulator.Step(Vector2F.Zero, Vector2F.Up, Step, false);
            var afterFirst = m_Simulator.State.Facing;

            m_Simulator.Step(Vector2F.Right, Vector2F.Zero, Step, false);

            Assert.AreEqual(afterFirst.X, m_Simulator.State.Facing.X, 1e-4f);
            Assert.AreEqual(afterFirst.Y, m_Simulator.State.Facing.Y, 1e-4f);
        }

        [Test]
        public void Step_ZeroDeltaTime_IsIgnored()
        {
            m_Simulator.Step(Vector2F.Right, Vector2F.Right, 0f, true);

            Assert.AreEqual(0f, m_Simulator.State.Position.X, 1e-6f);
            Assert.AreEqual(m_Profile.MaxStamina, m_Simulator.State.Stamina, 1e-4f);
        }

        [Test]
        public void Stamina_DrainsWhileSprinting()
        {
            Advance(1f, Vector2F.Right, sprint: true);

            var expected = m_Profile.MaxStamina - m_Profile.StaminaDrainPerSecond;
            Assert.AreEqual(expected, m_Simulator.State.Stamina, 1f);
        }

        [Test]
        public void Stamina_DoesNotDrainWhileWalking()
        {
            Advance(3f, Vector2F.Right);

            Assert.AreEqual(m_Profile.MaxStamina, m_Simulator.State.Stamina, 1e-3f);
        }

        [Test]
        public void Stamina_DoesNotDrainWithoutMovementInput()
        {
            // 按住奔跑键但不动，不应消耗体力。
            Advance(2f, Vector2F.Zero, sprint: true);

            Assert.AreEqual(m_Profile.MaxStamina, m_Simulator.State.Stamina, 1e-3f);
        }

        /// <summary>
        /// 体力归零后进入力竭状态。
        /// </summary>
        [Test]
        public void Stamina_ReachingZero_EntersExhaustedState()
        {
            // 满体力 100、消耗 20/秒，因此 5 秒耗尽。
            Advance(5.5f, Vector2F.Right, sprint: true);

            Assert.AreEqual(0f, m_Simulator.State.Stamina, 1e-3f);
            Assert.IsTrue(m_Simulator.State.IsExhausted, "体力耗尽必须进入力竭状态。");
        }

        /// <summary>
        /// 力竭期间无法奔跑，即使体力已恢复一部分。
        /// </summary>
        /// <remarks>
        /// 这是体力系统的核心规则：若无此限制，玩家只要松手一秒就能继续跑，
        /// 体力条会形同虚设。
        /// </remarks>
        [Test]
        public void Exhausted_CannotSprintUntilRecoveredAboveThreshold()
        {
            Advance(5.5f, Vector2F.Right, sprint: true);
            Assert.IsTrue(m_Simulator.State.IsExhausted, "前置条件：应当已力竭。");

            // 立刻尝试奔跑：力竭期间应当只能步行，速度保持在步行值。
            Advance(1f, Vector2F.Right, sprint: true);

            Assert.AreEqual(
                m_Profile.WalkSpeed,
                m_Simulator.State.CurrentSpeed,
                0.05f,
                "力竭期间即使按住奔跑键也只能步行。");
        }

        [Test]
        public void Exhausted_ClearsAfterRecoveringAboveThreshold()
        {
            Advance(5.5f, Vector2F.Right, sprint: true);

            // 恢复足够长时间：跨越力竭恢复延迟，并使体力超过解除阈值。
            Advance(m_Profile.ExhaustedRegenDelay + 3f, Vector2F.Right);

            Assert.IsFalse(m_Simulator.State.IsExhausted, "体力恢复到阈值以上后应解除力竭。");
        }

        /// <summary>
        /// 力竭期间体力确实会缓慢回升，且恢复到力竭阈值后即可重新奔跑。
        /// </summary>
        /// <remarks>
        /// 这条用例与上一条互为补充：上一条证明力竭期间跑不动，
        /// 本条证明力竭不会永久持续，且解除后奔跑能力恢复正常。
        ///
        /// 注意恢复阶段必须松开奔跑键。若一直按住，体力一旦恢复到阈值就会立刻
        /// 被重新抽干，形成"刚解除力竭又立刻力竭"的震荡——这是设计上正确的行为，
        /// 但不符合本用例要验证的场景。
        /// </remarks>
        [Test]
        public void Exhausted_RecoversAndThenSprintsAgain()
        {
            Advance(5.5f, Vector2F.Right, sprint: true);
            Assert.IsTrue(m_Simulator.State.IsExhausted);

            // 松开奔跑键，等待力竭恢复延迟与足够长的恢复时间。
            // 恢复所需时间 = 力竭延迟 + 阈值 / 恢复速度，这里额外多给一秒余量，
            // 避免测试时间卡在临界值上导致偶发失败。
            var recoverySeconds =
                m_Profile.ExhaustedRegenDelay
                + (m_Profile.ExhaustedRecoveryThreshold / m_Profile.StaminaRegenPerSecond)
                + 1f;
            Advance(recoverySeconds, Vector2F.Right, sprint: false);
            Assert.IsFalse(m_Simulator.State.IsExhausted, "恢复到阈值以上后应解除力竭。");

            // 再次奔跑应当恢复正常。
            Advance(1f, Vector2F.Right, sprint: true);
            Assert.AreEqual(
                m_Profile.SprintSpeed,
                m_Simulator.State.CurrentSpeed,
                0.05f,
                "解除力竭后应当能够正常奔跑。");
        }

        /// <summary>
        /// 力竭状态下的恢复延迟必须长于常规延迟，否则力竭没有惩罚意义。
        /// </summary>
        [Test]
        public void Stamina_ExhaustedRecoveryIsSlowerThanNormal()
        {
            Assert.Greater(
                m_Profile.ExhaustedRegenDelay,
                m_Profile.StaminaRegenDelay,
                "力竭恢复延迟必须长于常规恢复延迟。");
        }

        /// <summary>
        /// 恢复延迟内不应回体力。
        /// </summary>
        [Test]
        public void Stamina_DoesNotRegenerateDuringDelay()
        {
            Advance(1f, Vector2F.Right, sprint: true);
            var afterSprint = m_Simulator.State.Stamina;

            // 推进时间少于恢复延迟。
            Advance(m_Profile.StaminaRegenDelay * 0.5f, Vector2F.Right);

            Assert.AreEqual(afterSprint, m_Simulator.State.Stamina, 1e-3f, "延迟期内不应恢复体力。");
        }

        [Test]
        public void Stamina_DoesNotExceedMaximum()
        {
            Advance(10f, Vector2F.Right);

            Assert.AreEqual(m_Profile.MaxStamina, m_Simulator.State.Stamina, 1e-3f);
        }

        [Test]
        public void SetStamina_ClampsToValidRange()
        {
            m_Simulator.SetStamina(999f);
            Assert.AreEqual(m_Profile.MaxStamina, m_Simulator.State.Stamina, 1e-3f);

            m_Simulator.SetStamina(-50f);
            Assert.AreEqual(0f, m_Simulator.State.Stamina, 1e-3f);
        }

        [Test]
        public void Reset_RestoresInitialState()
        {
            Advance(3f, Vector2F.Right, sprint: true);

            m_Simulator.Reset(new Vector2F(10f, 20f), Vector2F.Up);

            Assert.AreEqual(10f, m_Simulator.State.Position.X, 1e-3f);
            Assert.AreEqual(20f, m_Simulator.State.Position.Y, 1e-3f);
            Assert.AreEqual(m_Profile.MaxStamina, m_Simulator.State.Stamina, 1e-3f);
            Assert.IsFalse(m_Simulator.State.IsExhausted);
        }

        /// <summary>配置校验应当能发现互相矛盾的参数。</summary>
        [Test]
        public void Profile_Validate_RejectsInconsistentValues()
        {
            Assert.IsNull(new PlayerMovementProfile().Validate(), "默认配置应当通过校验。");

            var invalid = new PlayerMovementProfile { SprintSpeed = 1f };
            Assert.IsNotNull(invalid.Validate(), "奔跑速度低于步行速度时应报错。");

            var badThreshold = new PlayerMovementProfile { SprintSpeedThreshold = 99f };
            Assert.IsNotNull(badThreshold.Validate(), "奔跑判定阈值超出速度范围时应报错。");
        }
    }
}
