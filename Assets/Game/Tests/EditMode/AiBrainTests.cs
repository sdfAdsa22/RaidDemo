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
                position: new Vector2F(9f, 0f),
                tier: MovementNoiseTier.Sprint));

            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Investigate, agent.CurrentState, "听到动静后应当前往调查。");
            Assert.AreEqual(new Vector2F(9f, 0f), agent.Context.Memory.LastKnownPosition);
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
                position: new Vector2F(6f, 0f),
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

            SetAgentHealth(agent, agent.MaxHealth * 0.1f);
            m_Fixture.Advance(0.1f);
            Assert.AreEqual(AiStateId.Retreat, agent.CurrentState, "前置条件：应当先进入撤退。");

            var healthAtRetreat = agent.Health;
            var healSeconds = (agent.MaxHealth * m_Fixture.Profile.RetreatHealCeilingRatio) / m_Fixture.Profile.HealPerSecondDuringRetreat;
            m_Fixture.Advance(healSeconds + 1f);

            Assert.Greater(agent.Health, healthAtRetreat, "撤退期间应当恢复生命。");
            Assert.AreEqual(AiStateId.Engage, agent.CurrentState, "恢复到阈值后应当重新投入战斗。");
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
