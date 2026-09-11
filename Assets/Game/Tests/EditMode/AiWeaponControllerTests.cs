using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// AI 武器控制器的直接驱动测试：开火消耗、弹匣打空后的换弹、备弹耗尽。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="AiCombatTests"/> 的分工：那一组从状态机出发，验证"AI 会开枪"；
    /// 这一组直接把扳机按到底，验证武器本身的时序与账目。</para>
    ///
    /// <para>之所以要单独测：换弹与开火共用同一个 <c>WeaponRuntime</c>，两者的调用顺序错了
    /// 也不会报错，只会表现为"AI 打空之后再也不开枪"。这种问题在整机测试里很难定位，
    /// 在直接驱动的用例里却是一眼可见的。</para>
    /// </remarks>
    [TestFixture]
    public sealed class AiWeaponControllerTests
    {
        /// <summary>每帧推进的时长。取 0.1 秒，与 600 发/分钟的射击间隔一致。</summary>
        private const float StepSeconds = 0.1f;

        private ScriptedHitProbe m_Probe;
        private CombatWorld m_World;
        private EventBus m_Bus;
        private AiDirector m_Director;

        [SetUp]
        public void SetUp()
        {
            m_Probe = new ScriptedHitProbe();
            m_World = new CombatWorld();
            m_Bus = new EventBus();
            m_Director = new AiDirector(m_Probe, m_World, new CombatTuning(), m_Bus, new AIPerceptionProfile());
        }

        [Test]
        public void TriggerHeld_ConsumesOneRoundPerShotInterval()
        {
            var weapon = CreateWeapon(magazineCapacity: 30, reserveAmmo: 60);

            for (var i = 0; i < 10; i++)
            {
                weapon.Tick(StepSeconds, wantsToFire: true, position: Vector2F.Zero, facingDegrees: 0f);
            }

            Assert.AreEqual(20, weapon.MagazineAmmo, "600 发/分钟在 1 秒内应当打出 10 发。");
            Assert.AreEqual(60, weapon.ReserveAmmo, "弹匣没打空时不应当动备弹。");
        }

        [Test]
        public void TriggerReleased_DoesNotConsumeAmmo()
        {
            var weapon = CreateWeapon(magazineCapacity: 30, reserveAmmo: 60);

            for (var i = 0; i < 10; i++)
            {
                weapon.Tick(StepSeconds, wantsToFire: false, position: Vector2F.Zero, facingDegrees: 0f);
            }

            Assert.AreEqual(30, weapon.MagazineAmmo);
            Assert.AreEqual(60, weapon.ReserveAmmo);
        }

        [Test]
        public void EmptyMagazine_Reloads_AndTakesAmmoFromReserve()
        {
            var weapon = CreateWeapon(magazineCapacity: 4, reserveAmmo: 40, reloadSeconds: 0.3f);

            // 四发打空。
            for (var i = 0; i < 4; i++)
            {
                weapon.Tick(StepSeconds, wantsToFire: true, position: Vector2F.Zero, facingDegrees: 0f);
            }

            Assert.AreEqual(0, weapon.MagazineAmmo, "前置条件：弹匣应当已经打空。");

            // 换弹开始，准备阶段只是等待。
            weapon.Tick(StepSeconds, wantsToFire: true, position: Vector2F.Zero, facingDegrees: 0f);
            Assert.IsTrue(weapon.IsReloading, "弹匣打空后应当自动开始换弹。");

            // 换弹计时走完之后，弹匣必须真的补上、备弹必须真的减少。
            for (var i = 0; i < 5; i++)
            {
                weapon.Tick(StepSeconds, wantsToFire: true, position: Vector2F.Zero, facingDegrees: 0f);
            }

            Assert.Greater(weapon.MagazineAmmo, 0, "换弹完成后弹匣里应当有子弹。");
            Assert.AreEqual(36, weapon.ReserveAmmo, "换弹应当从备弹里扣掉恰好四发。");
        }

        [Test]
        public void EmptyMagazineWithoutReserve_StopsFiring()
        {
            var weapon = CreateWeapon(magazineCapacity: 2, reserveAmmo: 0);

            for (var i = 0; i < 10; i++)
            {
                weapon.Tick(StepSeconds, wantsToFire: true, position: Vector2F.Zero, facingDegrees: 0f);
            }

            Assert.AreEqual(0, weapon.MagazineAmmo, "没有备弹时弹匣应当保持空。");
            Assert.IsFalse(weapon.IsReloading, "没有备弹时不应当进入换弹状态——那会让 AI 一直停在换弹动画里。");
        }

        [Test]
        public void Firing_PublishesTracerAndDamageEvents()
        {
            var playerId = m_World.Create(100f);
            m_Probe.HitTarget(playerId);

            var shots = 0;
            var hits = 0;
            m_Bus.Subscribe<WeaponFiredEvent>(_ => shots++);
            m_Bus.Subscribe<DamageAppliedEvent>(evt =>
            {
                if (evt.TargetId == playerId)
                {
                    hits++;
                }
            });

            var weapon = CreateWeapon(magazineCapacity: 30, reserveAmmo: 60, shooterCombatantId: 7);
            weapon.Tick(StepSeconds, wantsToFire: true, position: Vector2F.Zero, facingDegrees: 0f);

            Assert.AreEqual(1, shots, "开火应当广播弹道事件，表现层依赖它画出子弹轨迹。");
            Assert.AreEqual(1, hits, "命中应当广播伤害事件。");
        }

        /// <summary>创建一把参数确定的测试武器：零散布，因此命中完全可预期。</summary>
        private AiWeaponController CreateWeapon(
            int magazineCapacity,
            int reserveAmmo,
            float reloadSeconds = 2f,
            int shooterCombatantId = 99)
        {
            var profile = new AiWeaponProfile
            {
                MagazineCapacity = magazineCapacity,
                RoundsPerMinute = 600f,
                ReloadSeconds = reloadSeconds,
                BaseSpreadDegrees = 0f,
                SpreadPerShotDegrees = 0f,
                MaxSpreadDegrees = 0f,
                SpreadRecoveryPerSecond = 0f,
            };

            return new AiWeaponController(m_Director, shooterCombatantId, profile, 4242u, reserveAmmo);
        }
    }
}
