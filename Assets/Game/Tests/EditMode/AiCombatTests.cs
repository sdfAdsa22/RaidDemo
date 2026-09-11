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

            // 推进足够长的时间：必定发现后要等 2 秒才开火，之后才轮到点射节奏与射速。
            m_Fixture.Advance(4f);

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
            m_Fixture.Advance(4f);

            var shotsBeforeLoss = shots;

            // 目标躲进掩体：射线不再命中，但记忆仍然新鲜。
            m_Fixture.Probe.Miss();
            m_Fixture.Advance(m_Fixture.Profile.MemorySeconds * 0.5f);

            Assert.Greater(shotsBeforeLoss, 0, "前置条件：失去视线之前应当已经开过枪，否则这条用例等于没测。");
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

            var shots = 0;
            m_Fixture.Bus.Subscribe<WeaponFiredEvent>(evt =>
            {
                if (evt.ShooterId != 0)
                {
                    shots++;
                }
            });

            var agent = m_Fixture.SpawnAgent(weapon: weapon, reserveAmmo: 40);
            m_Fixture.Probe.HitTarget(m_Fixture.PlayerId);

            // 2 秒必定发现反应 + 点射节奏：给足时间打完弹匣并完成一次换弹。
            m_Fixture.Advance(10f);

            // 不直接断言"此刻弹匣里有子弹"：那一刻可能刚好又打空。
            // 改成断言**弹药守恒**——打出的每一发都只能来自初始弹匣或某次换弹，
            // 这个等式一旦不成立就说明换弹账目错了（正是 M4 期间踩过的那个坑）。
            var remaining = agent.MagazineAmmo + agent.ReserveAmmo;
            var expected = 4 + 40 - shots;

            Assert.Greater(shots, 4, $"至少要打出超过一个弹匣的子弹，才能证明换弹发生过。状态={agent.CurrentState}");
            Assert.Less(agent.ReserveAmmo, 40, "换弹应当消耗 AI 自带的备弹。");
            Assert.AreEqual(
                expected,
                remaining,
                $"弹药守恒被破坏：射出 {shots} 发后应当还剩 {expected} 发，实际 {remaining} 发。");
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
            m_Fixture.Advance(4f);

            m_Fixture.World.TryGet(agent.CombatantId, out var state);
            state.SetHealth(0f);
            var shotsAtDeath = shots;

            m_Fixture.Advance(3f);

            Assert.Greater(shotsAtDeath, 0, "前置条件：阵亡之前应当已经开过枪。");
            Assert.IsFalse(agent.IsAlive, "生命归零后 AI 应当被判定为死亡。");
            Assert.AreEqual(shotsAtDeath, shots, "尸体不应当继续开枪。");
        }
    }
}
