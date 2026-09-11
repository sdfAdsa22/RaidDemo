using NUnit.Framework;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Kernel;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 武器运行时测试：射击模式、射速、散布、换弹。
    /// </summary>
    /// <remarks>
    /// <para>这些用例覆盖的是"扣扳机之后会发生什么"，而不是伤害数值。
    /// 两者分开测试的原因很实际：伤害错了要调公式，射速错了要调时序，
    /// 混在一个套件里会让失败信息指向错误的方向。</para>
    /// <para>随机数用固定种子，因此散布相关的断言也是可复现的。</para>
    /// </remarks>
    [TestFixture]
    public sealed class WeaponRuntimeTests
    {
        private const float ShotInterval = 0.1f;

        /// <summary>创建一把可按需配置的测试武器。</summary>
        private static WeaponRuntime CreateWeapon(
            WeaponFireMode mode = WeaponFireMode.Single,
            int burstCount = 3,
            int magazineCapacity = 30,
            bool startLoaded = true,
            float baseSpread = 0f,
            float spreadPerShot = 0f,
            float maxSpread = 0f,
            float spreadRecovery = 0f,
            float reloadSeconds = 1f)
        {
            var stats = new TestWeaponStats(
                roundsPerMinute: 600f,
                fireMode: mode,
                burstCount: burstCount,
                magazineCapacity: magazineCapacity,
                reloadSeconds: reloadSeconds,
                baseSpreadDegrees: baseSpread,
                spreadPerShotDegrees: spreadPerShot,
                maxSpreadDegrees: maxSpread,
                spreadRecoveryPerSecond: spreadRecovery);

            return new WeaponRuntime(stats, new DeterministicRandom(12345u), startLoaded);
        }

        [Test]
        public void SingleFire_FiresOncePerPress()
        {
            var weapon = CreateWeapon(WeaponFireMode.Single);

            var first = weapon.UpdateTrigger(true, out _);
            var held = weapon.UpdateTrigger(true, out _);
            weapon.Tick(ShotInterval * 2f);
            var stillHeld = weapon.UpdateTrigger(true, out _);

            Assert.AreEqual(TriggerState.Fired, first, "按下扳机应当发射一发。");
            Assert.AreEqual(TriggerState.Idle, held, "单发模式按住不放不应当继续发射。");
            Assert.AreEqual(TriggerState.Idle, stillHeld, "冷却结束后也不该发射，因为扳机没有重新按下。");
            Assert.AreEqual(29, weapon.MagazineAmmo, "整个过程中只应当消耗一发。");
        }

        [Test]
        public void SingleFire_FiresAgainAfterRelease()
        {
            var weapon = CreateWeapon(WeaponFireMode.Single);
            weapon.UpdateTrigger(true, out _);
            weapon.Tick(ShotInterval * 2f);
            weapon.UpdateTrigger(false, out _);

            var second = weapon.UpdateTrigger(true, out _);

            Assert.AreEqual(TriggerState.Fired, second, "松开再按下应当再次发射。");
            Assert.AreEqual(28, weapon.MagazineAmmo, "两次射击应当消耗两发。");
        }

        [Test]
        public void AutoFire_KeepsFiringWhileHeld()
        {
            var weapon = CreateWeapon(WeaponFireMode.Auto);

            var first = weapon.UpdateTrigger(true, out _);
            weapon.Tick(0.05f);
            var tooSoon = weapon.UpdateTrigger(true, out _);
            weapon.Tick(0.06f);
            var afterCooldown = weapon.UpdateTrigger(true, out _);

            Assert.AreEqual(TriggerState.Fired, first, "全自动按下即发射。");
            Assert.AreEqual(TriggerState.Idle, tooSoon, "射击间隔未到时不应发射。");
            Assert.AreEqual(TriggerState.Fired, afterCooldown, "间隔走完后按住应当继续发射。");
            Assert.AreEqual(28, weapon.MagazineAmmo, "三次判定中应当只成功发射两发。");
        }

        [Test]
        public void BurstFire_FiresConfiguredBurstThenStops()
        {
            var weapon = CreateWeapon(WeaponFireMode.Burst, burstCount: 3);
            var fired = 0;

            if (weapon.UpdateTrigger(true, out _) == TriggerState.Fired)
            {
                fired++;
            }

            for (var i = 0; i < 5; i++)
            {
                weapon.Tick(ShotInterval * 1.1f);
                if (weapon.UpdateTrigger(true, out _) == TriggerState.Fired)
                {
                    fired++;
                }
            }

            Assert.AreEqual(3, fired, "连发模式一次按下应当恰好打出配置的发数。");
            Assert.AreEqual(27, weapon.MagazineAmmo, "连发三发应当消耗三发弹药。");
        }

        [Test]
        public void EmptyMagazine_ReportsBlockedAndNeverGoesNegative()
        {
            var weapon = CreateWeapon(startLoaded: false);

            var state = weapon.UpdateTrigger(true, out _);
            // 单发模式下必须松开再按下才算再次尝试开火；
            // 一直按着不放本来就不该重复发射，那是武器该有的行为，不是空弹匣的表现。
            weapon.UpdateTrigger(false, out _);
            var again = weapon.UpdateTrigger(true, out _);

            Assert.AreEqual(TriggerState.BlockedEmpty, state, "空弹匣开火应当明确报告被阻挡。");
            Assert.AreEqual(TriggerState.BlockedEmpty, again, "再次尝试仍然报告被阻挡。");
            Assert.AreEqual(0, weapon.MagazineAmmo, "弹匣数量不应当变成负数。");
        }

        [Test]
        public void Spread_GrowsPerShotAndRecoversAfterRelease()
        {
            var weapon = CreateWeapon(
                WeaponFireMode.Auto,
                baseSpread: 1f,
                spreadPerShot: 1f,
                maxSpread: 3f,
                spreadRecovery: 2f);

            weapon.UpdateTrigger(true, out _);
            var afterFirst = weapon.CurrentSpreadDegrees;

            // 两发之间必须推进时间，否则射速限制会把第二发挡掉，
            // 那样测的就是射速而不是散布了。
            weapon.Tick(ShotInterval * 1.1f);
            weapon.UpdateTrigger(true, out _);
            var afterSecond = weapon.CurrentSpreadDegrees;

            Assert.AreEqual(2f, afterFirst, 1e-3f, "第一发之后散布应当从基础值抬升一档。");
            Assert.AreEqual(3f, afterSecond, 1e-3f, "第二发之后达到上限。");

            weapon.UpdateTrigger(false, out _);
            weapon.Tick(0.5f);

            Assert.AreEqual(2f, weapon.CurrentSpreadDegrees, 1e-3f,
                "停火后散布应当按恢复速度回落（每秒 2 度，半秒回落 1 度）。");
        }

        [Test]
        public void Spread_NeverExceedsConfiguredMaximum()
        {
            var weapon = CreateWeapon(
                WeaponFireMode.Auto,
                baseSpread: 2f,
                spreadPerShot: 5f,
                maxSpread: 4f);

            weapon.UpdateTrigger(true, out _);
            weapon.Tick(ShotInterval * 1.1f);
            weapon.UpdateTrigger(true, out _);

            Assert.AreEqual(4f, weapon.CurrentSpreadDegrees, 1e-3f, "散布不应当超过上限。");
        }

        [Test]
        public void Reload_FullMagazineIsRejected()
        {
            var weapon = CreateWeapon();

            var started = weapon.TryBeginReload(out var failureCode);

            Assert.IsFalse(started, "弹匣已满时不应开始换弹。");
            Assert.AreEqual("combat_magazine_full", failureCode, "失败原因应当是弹匣已满。");
        }

        [Test]
        public void Reload_BlocksFiringUntilFinished()
        {
            var weapon = CreateWeapon(startLoaded: false);
            weapon.TryBeginReload(out _);

            var state = weapon.UpdateTrigger(true, out _);

            Assert.AreEqual(TriggerState.BlockedReloading, state, "换弹期间开火应当被阻挡。");
        }

        [Test]
        public void Reload_FillsMagazineFromAvailableAmmo()
        {
            var weapon = CreateWeapon(startLoaded: false, magazineCapacity: 30, reloadSeconds: 1f);
            weapon.TryBeginReload(out _);
            weapon.Tick(1.1f);

            var loaded = weapon.CompleteReload(100);

            Assert.AreEqual(30, loaded, "弹药充足时应当把弹匣装满。");
            Assert.AreEqual(30, weapon.MagazineAmmo, "弹匣数量应当等于容量。");
        }

        [Test]
        public void Reload_WithScarceAmmo_PartiallyFills()
        {
            var weapon = CreateWeapon(startLoaded: false, magazineCapacity: 30, reloadSeconds: 1f);
            weapon.TryBeginReload(out _);
            weapon.Tick(1.1f);

            var loaded = weapon.CompleteReload(7);

            Assert.AreEqual(7, loaded, "弹药不足时应当把能装的都装上，而不是拒绝。");
            Assert.AreEqual(7, weapon.MagazineAmmo, "弹匣里应当恰好有 7 发。");
        }

        [Test]
        public void Reload_WithoutEnoughTime_DoesNotLoadAnything()
        {
            var weapon = CreateWeapon(startLoaded: false, reloadSeconds: 2f);
            weapon.TryBeginReload(out _);
            weapon.Tick(0.5f);

            var loaded = weapon.CompleteReload(100);

            Assert.AreEqual(0, loaded, "换弹计时未走完时不应当装入弹药。");
            Assert.AreEqual(0, weapon.MagazineAmmo, "弹匣应当仍是空的。");

            weapon.Tick(1.6f);
            Assert.AreEqual(30, weapon.CompleteReload(100), "计时走完之后应当可以正常装入。");
        }

        [Test]
        public void Reload_ResetsSpreadToBase()
        {
            var weapon = CreateWeapon(
                WeaponFireMode.Auto,
                baseSpread: 1f,
                spreadPerShot: 1f,
                maxSpread: 5f,
                reloadSeconds: 0.5f);

            weapon.UpdateTrigger(true, out _);
            Assert.Greater(weapon.CurrentSpreadDegrees, 1f, "开火后散布应当高于基础值。");

            weapon.UpdateTrigger(false, out _);
            weapon.TryBeginReload(out _);
            weapon.Tick(0.6f);
            weapon.CompleteReload(30);

            Assert.AreEqual(1f, weapon.CurrentSpreadDegrees, 1e-3f, "换弹后重新握枪，散布应当回到基础值。");
        }
    }
}
