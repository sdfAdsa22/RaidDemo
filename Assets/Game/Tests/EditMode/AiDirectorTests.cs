using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// AI 调度器集成测试：噪音分发范围、目标快照更新、时钟推进与目标消失的处理。
    /// </summary>
    /// <remarks>
    /// 单个 AI 的行为由 <see cref="AiBrainTests"/> 覆盖；这里验证的是"多个 AI 同处一个世界"
    /// 才会出现的问题：噪音该传给谁、目标状态怎么同步、时钟是否唯一。
    /// </remarks>
    [TestFixture]
    public sealed class AiDirectorTests
    {
        private AiTestFixture m_Fixture;

        [SetUp]
        public void SetUp()
        {
            m_Fixture = new AiTestFixture(playerPosition: new Vector2F(5f, 0f));
            m_Fixture.Probe.Miss();
        }

        [Test]
        public void Noise_OnlyReachesAgentsWithinHearingRadius()
        {
            var near = m_Fixture.SpawnAgent(new Vector2F(0f, 0f));
            var far = m_Fixture.SpawnAgent(new Vector2F(25f, 0f));

            // 奔跑档的可听半径默认 18 米：近处 AI 听得到，25 米外的听不到。
            m_Fixture.Director.ReportNoise(new MovementNoiseEvent(
                sourceId: 0,
                position: Vector2F.Zero,
                tier: MovementNoiseTier.Sprint));

            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Investigate, near.CurrentState, "范围内的 AI 应当去查看动静。");
            Assert.AreEqual(AiStateId.Patrol, far.CurrentState, "范围外的 AI 不应当被隔空惊动。");
        }

        [Test]
        public void Noise_OverloadedTravelsFurthest()
        {
            var far = m_Fixture.SpawnAgent(new Vector2F(24f, 0f));

            m_Fixture.Director.ReportNoise(new MovementNoiseEvent(
                sourceId: 0,
                position: Vector2F.Zero,
                tier: MovementNoiseTier.Overloaded));

            m_Fixture.Advance(0.1f);

            Assert.AreEqual(
                AiStateId.Investigate,
                far.CurrentState,
                "超载（26 米）应当能惊动步行（8 米）与奔跑（18 米）都听不到的距离。");
        }

        [Test]
        public void Director_ClockAdvances_AndIsSharedByAgents()
        {
            var agent = m_Fixture.SpawnAgent();

            m_Fixture.Director.Tick(0.5f);
            m_Fixture.Director.Tick(0.5f);

            Assert.AreEqual(1f, m_Fixture.Director.ElapsedSeconds, 1e-4f, "调度器应当累加统一时钟。");
            Assert.AreEqual(1f, agent.Snapshot.Time, 1e-4f, "AI 的时间应当来自同一个时钟。");
        }

        [Test]
        public void TargetDeath_ClearsMemory_AndReturnsToPatrol()
        {
            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(0.1f);
            Assert.AreEqual(AiStateId.Engage, agent.CurrentState, "前置条件：应当先进入交战。");

            // 玩家阵亡（或撤离）：目标不复存在。
            m_Fixture.SetPlayer(new Vector2F(5f, 0f), alive: false);
            m_Fixture.Advance(0.1f);

            Assert.AreEqual(AiStateId.Patrol, agent.CurrentState, "目标消失后应当回到巡逻。");
            Assert.IsFalse(agent.Context.Memory.HasMemory, "目标消失后记忆必须清空，否则 AI 会一直调查一个空位置。");
        }

        [Test]
        public void AliveCount_TracksDeaths()
        {
            var first = m_Fixture.SpawnAgent(new Vector2F(0f, 0f));
            m_Fixture.SpawnAgent(new Vector2F(3f, 0f));

            Assert.AreEqual(2, m_Fixture.Director.AliveCount);

            m_Fixture.World.TryGet(first.CombatantId, out var state);
            state.SetHealth(0f);

            Assert.AreEqual(1, m_Fixture.Director.AliveCount, "死亡的 AI 不应当再计入存活数。");
        }
    }
}
