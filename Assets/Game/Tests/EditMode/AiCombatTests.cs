using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// AI 交火测试：AI 用与玩家同一条链路开火、结算伤害并广播事件。
    /// </summary>
    /// <remarks>
    /// <para>这组用例的价值在于验证"复用"确实成立：如果 AI 走了一条独立的伤害路径，
    /// 那么护甲减伤、暴击、护甲磨损这些规则在 AI 身上就会悄悄失效，
    /// 而这种不一致在实机上极难发现——AI 打人"感觉差不多疼"。</para>
    ///
    /// <para>射线用替身，因此"打中谁"是被精确指定的。</para>
    /// </remarks>
    [TestFixture]
    public sealed class AiCombatTests
    {
        private AiTestFixture m_Fixture;

        [SetUp]
        public void SetUp()
        {
            m_Fixture = new AiTestFixture(playerPosition: new Vector2F(5f, 0f));
        }

        [Test]
        public void EngagedAgent_FiresAndDamagesPlayer()
        {
            var shots = 0;
            var damageEvents = 0;
            m_Fixture.Bus.Subscribe<WeaponFiredEvent>(evt =>
            {
                if (evt.ShooterId != 0)
                {
                    shots++;
                }
            });

            m_Fixture.Bus.Subscribe<DamageAppliedEvent>(evt =>
            {
                if (evt.TargetId == m_Fixture.PlayerId)
                {
                    damageEvents++;
                }
            });

            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            // 推进足够长的时间以越过反应时间并打完一轮点射。
            m_Fixture.Advance(2f);

            Assert.AreEqual(AiStateId.Engage, agent.CurrentState, "前置条件：AI 应当进入交战。");
            Assert.Greater(shots, 0, "交战中的 AI 应当真的开枪——否则玩家面对的是不会还击的靶子。");
            Assert.Greater(damageEvents, 0, "AI 的子弹应当走玩家同一套伤害结算。");
            Assert.Less(
                m_Fixture.GetPlayer().Health,
                100f,
                "玩家应当真的掉血，而不是只有视觉反馈。");
        }

        [Test]
        public void AgentWithoutLineOfSight_StopsFiring()
        {
            var shots = 0;
            m_Fixture.Bus.Subscribe<WeaponFiredEvent>(evt =>
            {
                if (evt.ShooterId != 0)
                {
                    shots++;
                }
            });

            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(1f);

            var shotsBeforeLoss = shots;

            // 目标躲进掩体：射线不再命中，但记忆仍然新鲜。
            m_Fixture.Probe.Miss();
            m_Fixture.Advance(m_Fixture.Profile.MemorySeconds * 0.5f);

            Assert.AreEqual(shotsBeforeLoss, shots, "看不见目标时不应当继续射击，否则等于透视穿墙。");
        }

        [Test]
        public void Agent_ReloadsFromReserve_WhenMagazineIsEmpty()
        {
            // 弹匣很小、换弹很快，便于在少量推进内观察到完整循环。
            var weapon = new AiWeaponProfile
            {
                MagazineCapacity = 4,
                ReloadSeconds = 0.3f,
                RoundsPerMinute = 600f,
            };

            var agent = m_Fixture.SpawnAgent(weapon: weapon, reserveAmmo: 40);
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            m_Fixture.Advance(3f);

            // 把状态一并写进断言消息：这个用例失败时最难回答的问题是
            // "它到底没开枪，还是开了枪但没换弹"，而这两者的排查方向完全不同。
            var diagnosis = $"状态={agent.CurrentState}，弹匣={agent.MagazineAmmo}，备弹={agent.ReserveAmmo}";

            Assert.Less(agent.ReserveAmmo, 40, $"换弹应当消耗 AI 自带的备弹。{diagnosis}");
            Assert.Greater(agent.MagazineAmmo, 0, $"换弹完成后弹匣里应当有子弹。{diagnosis}");
        }

        [Test]
        public void KilledAgent_StopsFiring()
        {
            var shots = 0;
            m_Fixture.Bus.Subscribe<WeaponFiredEvent>(evt =>
            {
                if (evt.ShooterId != 0)
                {
                    shots++;
                }
            });

            var agent = m_Fixture.SpawnAgent();
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);
            m_Fixture.Advance(1f);

            m_Fixture.World.TryGet(agent.CombatantId, out var state);
            state.SetHealth(0f);
            var shotsAtDeath = shots;

            m_Fixture.Advance(3f);

            Assert.IsFalse(agent.IsAlive, "生命归零后 AI 应当被判定为死亡。");
            Assert.AreEqual(shotsAtDeath, shots, "尸体不应当继续开枪。");
        }
    }
}
