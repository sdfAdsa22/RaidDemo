using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// AI 决策测试：四个状态之间的迁移条件。
    /// </summary>
    /// <remarks>
    /// <para>这组用例是 M4 的核心验收内容。它验证的是"喂入感知输入 → 状态按预期迁移"，
    /// 完全不加载场景、不等待真实时间：一次 Tick 就是一次决策。</para>
    ///
    /// <para>每个用例只验证一条迁移边。一条用例覆盖多条边时，失败信息无法指出是哪条规则坏了。</para>
    /// </remarks>
    [TestFixture]
    public sealed class AiBrainTests
    {
        /// <summary>玩家在 AI 正前方 5 米处，处于视野锥内。</summary>
        private const float PlayerDistance = 5f;

        private AiTestFixture m_Fixture;

        [SetUp]
        public void SetUp()
        {
            m_Fixture = new AiTestFixture(
                playerPosition: new Vector2F(PlayerDistance, 0f));
        }

        [Test]
        public void Patrol_OnSeeingTarget_EntersEngage()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Engage, agent.CurrentState, "看见目标后应当进入交战。");
            Assert.IsTrue(agent.Snapshot.SeesTarget, "快照应当记录到本帧确实看见了目标。");
            Assert.IsTrue(agent.Context.Memory.HasMemory, "看到目标应当写入记忆。");
        }

        [Test]
        public void Patrol_OnNoise_EntersInvestigate()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.Miss();

            m_Fixture.Director.ReportNoise(new MovementNoiseEvent(
                sourceId: 0,
                position: new Vector2F(7f, 0f),
                tier: MovementNoiseTier.Sprint));

            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Investigate, agent.CurrentState, "听到动静后应当前往调查。");
            Assert.AreEqual(new Vector2F(7f, 0f), agent.Context.Memory.LastKnownPosition);
        }

        [Test]
        public void Patrol_OnDamage_EntersEngage()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.Miss();

            agent.NotifyDamaged(new Vector2F(PlayerDistance, 0f), m_Fixture.PlayerId);
            m_Fixture.Advance(0.1f);

            Assert.AreEqual(
                AiStateId.Engage,
                agent.CurrentState,
                "挨打之后必须反击：否则玩家可以站在 AI 背后安全地把它打死。");
        }

        [Test]
        public void Investigate_TimesOut_ReturnsToPatrol()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.Miss();

            m_Fixture.Director.ReportNoise(new MovementNoiseEvent(
                sourceId: 0,
                position: new Vector2F(3f, 0f),
                tier: MovementNoiseTier.Walk));
            m_Fixture.Advance(0.1f);
            Assert.AreEqual(AiStateId.Investigate, agent.CurrentState, "前置条件：应当先进入调查。");

            // 调查超时时长默认为 9 秒，这里多推进一点以确保越过边界。
            m_Fixture.Advance(m_Fixture.Profile.InvestigateSeconds + 1f);

            Assert.AreEqual(AiStateId.Patrol, agent.CurrentState, "调查超时后应当回到巡逻。");
        }

        [Test]
        public void Engage_OnLowHealth_EntersRetreat()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(0.1f);
            Assert.AreEqual(AiStateId.Engage, agent.CurrentState, "前置条件：应当先进入交战。");

            SetAgentHealth(agent, agent.MaxHealth * 0.1f);
            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Retreat, agent.CurrentState, "生命过低时应当撤退。");
        }

        [Test]
        public void Retreat_HealsAndReturnsToEngage()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(0.1f);

            // 取"刚好低于撤退阈值"的血量：治疗到上限只需 5 秒出头，短于记忆时长（6 秒），
            // 因此恢复完成时还知道目标在哪，应当直接回到交战。
            // 血更低时撤退会持续 8 秒以上、记忆过期，AI 会改回巡逻——那是另一条行为路径，
            // 由 Engage_LosingTargetAfterMemoryExpiry_ReturnsToPatrol 覆盖。
            SetAgentHealth(agent, agent.MaxHealth * (m_Fixture.Profile.RetreatHealthRatio - 0.02f));
            m_Fixture.Advance(0.1f);
            Assert.AreEqual(AiStateId.Retreat, agent.CurrentState, "前置条件：应当先进入撤退。");

            var healthAtRetreat = agent.Health;
            var neededHealth = (agent.MaxHealth * m_Fixture.Profile.RetreatHealCeilingRatio) - healthAtRetreat;
            var healSeconds = neededHealth / m_Fixture.Profile.HealPerSecondDuringRetreat;

            // 只推进到"刚好治满阈值"再多一点：撤退结束时记忆还有效，因此应当回到交战。
            // 继续往后拖会变成另一条路径——AI 追向最后已知位置、记忆过期后改回巡逻，
            // 那条路径由 Engage_LosingTargetAfterMemoryExpiry_ReturnsToPatrol 覆盖。
            m_Fixture.Advance(healSeconds + 0.2f);

            Assert.Greater(agent.Health, healthAtRetreat, "撤退期间应当恢复生命。");

            var memory = agent.Context.Memory;
            var memoryAge = memory.HasMemory
                ? memory.TimeSinceLastKnown(m_Fixture.Director.ElapsedSeconds)
                : float.PositiveInfinity;
            var diagnosis =
                $"生命={agent.Health:F1} 比例={agent.HealthRatio:F2} 状态时长={agent.TimeInState:F1}s " +
                $"记忆年龄={memoryAge:F1}s 距离={Vector2F.Distance(agent.Position, agent.Context.Snapshot.Target.Position):F1}m";

            Assert.AreEqual(AiStateId.Engage, agent.CurrentState, $"恢复到阈值后应当重新投入战斗。{diagnosis}");
        }

        [Test]
        public void Engage_LosingTargetAfterMemoryExpiry_ReturnsToPatrol()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(0.1f);
            Assert.AreEqual(AiStateId.Engage, agent.CurrentState, "前置条件：应当先进入交战。");

            // 目标从视野中消失，且不再有任何动静。
            m_Fixture.Probe.Miss();
            m_Fixture.Advance(m_Fixture.Profile.MemorySeconds + 1f);

            Assert.AreEqual(
                AiStateId.Patrol,
                agent.CurrentState,
                "记忆过期后应当彻底放弃追踪：否则 AI 会永远围着一个空位置打转。");
        }

        [Test]
        public void Engage_KeepsApproaching_WhileMemoryIsFresh()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(0.1f);

            m_Fixture.Probe.Miss();
            m_Fixture.Advance(m_Fixture.Profile.MemorySeconds * 0.5f);

            Assert.AreEqual(
                AiStateId.Engage,
                agent.CurrentState,
                "记忆仍然新鲜时应当继续逼近最后已知位置，这正是玩家绕后时最紧张的一段。");
        }

        [Test]
        public void Patrol_OnSuspiciousTarget_EntersAlertInsteadOfEngage()
        {
            // 7.5 米落在警惕区间：看得见但还不够近，AI 应当只是起疑。
            m_Fixture.SetPlayer(new Vector2F(7.5f, 0f), alive: true);
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Alert, agent.CurrentState, "警惕区间的目标只应当引起怀疑，不是直接交战。");
            Assert.IsTrue(agent.Snapshot.SuspectedTarget);
            Assert.IsFalse(agent.Snapshot.SeesTarget);
        }

        [Test]
        public void GuaranteedDetection_IgnoresFacing()
        {
            // 目标在背后 5 米：必定发现距离内忽略朝向。
            m_Fixture.SetPlayer(new Vector2F(-5f, 0f), alive: true);
            var agent = m_Fixture.SpawnAgent(facingDegrees: 0f);
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            m_Fixture.Advance(0.1f);

            Assert.AreEqual(
                AiStateId.Engage,
                agent.CurrentState,
                "5 米内的目标即使站在背后也必须发现——否则玩家可以贴着敌人背身通过。");
        }

        [Test]
        public void SuspiciousTarget_OutsideViewCone_IsNotNoticed()
        {
            // 背后 7.5 米：落在警惕区间但不在 60 度视野锥内，应当什么都没发生。
            m_Fixture.SetPlayer(new Vector2F(-7.5f, 0f), alive: true);
            var agent = m_Fixture.SpawnAgent(facingDegrees: 0f);
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            m_Fixture.Advance(0.5f);

            Assert.AreEqual(AiStateId.Patrol, agent.CurrentState, "警惕区间仍受视野锥限制。");
        }

        [Test]
        public void Alert_AfterConfirmSeconds_EntersEngage()
        {
            m_Fixture.SetPlayer(new Vector2F(7.5f, 0f), alive: true);
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(0.1f);
            Assert.AreEqual(AiStateId.Alert, agent.CurrentState, "前置条件：应当先进入警惕。");

            m_Fixture.Advance(m_Fixture.Profile.AlertConfirmSeconds + 1f);

            Assert.AreEqual(
                AiStateId.Engage,
                agent.CurrentState,
                "持续被盯着超过确认时长之后应当升级为交战。");
        }

        [Test]
        public void Alert_DoesNotFire_BeforeConfirm()
        {
            var shots = 0;
            m_Fixture.Bus.Subscribe<RaidDemo.Combat.WeaponFiredEvent>(_ => shots++);

            m_Fixture.SetPlayer(new Vector2F(7.5f, 0f), alive: true);
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            // 确认时长（5 秒）之内不应该开火：这段时间是留给玩家反应的窗口。
            m_Fixture.Advance(m_Fixture.Profile.AlertConfirmSeconds - 1f);

            Assert.AreEqual(AiStateId.Alert, agent.CurrentState);
            Assert.AreEqual(0, shots, "警惕期间不允许开火。");
        }

        [Test]
        public void Alert_TargetLeavesSight_InvestigatesLastKnownPosition()
        {
            m_Fixture.SetPlayer(new Vector2F(7.5f, 0f), alive: true);
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(0.5f);
            Assert.AreEqual(AiStateId.Alert, agent.CurrentState, "前置条件：应当先进入警惕。");

            // 目标侧身躲进掩体：AI 应当走过去查看，而不是当无事发生。
            m_Fixture.Probe.Miss();
            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Investigate, agent.CurrentState, "起疑之后跟丢，应当去最后位置查看。");
        }

        /// <summary>直接改写 AI 的生命值，用于构造"挨了很多枪"的处境。</summary>
        private void SetAgentHealth(AiAgent agent, float health)
        {
            Assert.IsTrue(
                m_Fixture.World.TryGet(agent.CombatantId, out var state),
                "AI 应当已经登记在战斗世界里。");

            state.SetHealth(health);
        }
    }
}
