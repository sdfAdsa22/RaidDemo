using NUnit.Framework;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 战斗命令链路测试：命令 → 路由 → 武器与伤害 → 事件。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="DamageCalculatorTests"/> 的分工：那一组测公式，
    /// 这一组测"射击意图能否正确抵达武器、结果能否正确回到调用方与背包"。</para>
    /// <para>射线能力用假实现替换，因此可以精确指定"这一枪打中了谁、打在哪个位置"，
    /// 而这些都是真实场景里难以稳定构造的条件。</para>
    /// </remarks>
    [TestFixture]
    public sealed class CombatCommandTests
    {
        /// <summary>可控的射线检测替身。</summary>
        private sealed class FakeHitProbe : IHitProbe
        {
            /// <summary>是否命中。</summary>
            public bool ReturnsHit { get; set; }

            /// <summary>命中结果。</summary>
            public HitInfo Result { get; set; }

            /// <summary>最近一次投射使用的最大距离，用于验证射程是否被正确传递。</summary>
            public float LastMaxDistance { get; private set; }

            /// <inheritdoc />
            public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
            {
                LastMaxDistance = maxDistance;
                hit = Result;
                return ReturnsHit;
            }
        }

        private ItemFactory m_Factory;
        private EventBus m_Bus;
        private CommandRouter m_Router;
        private CombatWorld m_World;
        private FakeHitProbe m_Probe;
        private CombatTuning m_Tuning;
        private PlayerWeaponController m_Controller;
        private InventoryGrid m_Backpack;
        private InventoryGrid m_AmmoPouch;
        private TestWeaponStats m_WeaponStats;
        private TestItemDefinition m_AmmoDefinition;
        private int m_TargetId;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Bus = new EventBus();
            m_Router = new CommandRouter();
            m_World = new CombatWorld();
            m_Probe = new FakeHitProbe();
            m_Tuning = new CombatTuning { CriticalOffsetMeters = 0.25f };

            m_Backpack = new InventoryGrid(6, 6, "主背包");
            m_AmmoPouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);
            var loadout = new PlayerLoadout(m_Backpack, new EquipmentLoadout(), m_AmmoPouch);

            var playerWeapon = new PlayerWeapon(new DeterministicRandom(777u));
            m_Controller = new PlayerWeaponController(
                playerWeapon, loadout, m_Probe, m_World, m_Tuning, m_Bus);

            m_Router.Register<PlayerFireIntent>(new FireCommandHandler(m_Controller));
            m_Router.Register<PlayerReloadIntent>(new ReloadCommandHandler(m_Controller));

            m_WeaponStats = new TestWeaponStats(
                baseDamage: 25f,
                roundsPerMinute: 600f,
                magazineCapacity: 30,
                caliberId: "9x19",
                reloadSeconds: 1f,
                rangeMeters: 40f);

            m_AmmoDefinition = new TestItemDefinition(
                "ammo.9x19",
                ItemCategory.Ammo,
                maxStack: 120,
                ammoStats: new TestAmmoStats("9x19", 15f));

            m_TargetId = m_World.Create(100f, new TestArmorStats(2, 60f, 0.35f));
            m_Controller.SetMuzzlePosition(Vector3.zero);
        }

        /// <summary>
        /// 把弹药放进弹药挂。
        /// </summary>
        /// <remarks>
        /// 换弹只从弹药挂取弹，因此测试必须把弹药放在这里——
        /// 放在背包里会让"换弹失败"看起来像功能坏了，其实是规则如此。
        /// </remarks>
        private void PutAmmo(int count)
        {
            m_AmmoPouch.AutoPlace(m_Factory.Create(m_AmmoDefinition, count));
        }

        /// <summary>让假射线返回一次命中。</summary>
        private void ArrangeHit(bool critical)
        {
            m_Probe.ReturnsHit = true;
            m_Tuning.CriticalAxis = new Vector3(0f, 0f, 1f);

            var center = new Vector3(10f, 0.9f, 0f);
            var point = critical
                ? center + (m_Tuning.CriticalAxis * 0.5f)
                : center;

            m_Probe.Result = new HitInfo(m_TargetId, point, center, 10f);
        }

        [Test]
        public void FireWithoutWeapon_ReturnsNoWeaponCode()
        {
            var result = m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));

            Assert.IsFalse(result.Success, "没有装备武器时开火应当失败。");
            Assert.AreEqual(CommandCodes.CombatNoWeapon, result.Code, "失败原因应当是主武器槽为空。");
        }

        [Test]
        public void FireWithWeapon_ConsumesAmmoAndRaisesFiredEvent()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            var firedCount = 0;
            using (m_Bus.Subscribe<WeaponFiredEvent>(_ => firedCount++))
            {
                m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
                m_Controller.Tick(0.016f);
            }

            Assert.AreEqual(1, firedCount, "成功发射应当广播开火事件。");
            Assert.AreEqual(29, m_Controller.Runtime.MagazineAmmo, "开火应当消耗一发弹药。");
        }

        [Test]
        public void Fire_UseWeaponRangeAsRaycastLimit()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);

            m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
            m_Controller.Tick(0.016f);

            Assert.AreEqual(40f, m_Probe.LastMaxDistance, 1e-3f,
                "射线检测距离应当取武器的射程，而不是一个写死的值。");
        }

        [Test]
        public void Fire_HittingTarget_AppliesDamage()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            ArrangeHit(critical: false);
            DamageAppliedEvent? observed = null;

            using (m_Bus.Subscribe<DamageAppliedEvent>(evt => observed = evt))
            {
                m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
                m_Controller.Tick(0.016f);
            }

            Assert.IsTrue(observed.HasValue, "命中目标应当广播伤害事件。");
            Assert.AreEqual(m_TargetId, observed.Value.TargetId, "伤害事件应当指向被命中的目标。");
            Assert.IsFalse(observed.Value.IsCritical, "正中目标中心不构成暴击。");
            Assert.Less(observed.Value.RemainingHealth, 100f, "目标的生命值应当下降。");
        }

        [Test]
        public void Fire_HittingUpperPart_CountsAsCritical()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            ArrangeHit(critical: true);
            DamageAppliedEvent? observed = null;

            using (m_Bus.Subscribe<DamageAppliedEvent>(evt => observed = evt))
            {
                m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
                m_Controller.Tick(0.016f);
            }

            Assert.IsTrue(observed.HasValue && observed.Value.IsCritical,
                "命中点偏向暴击轴正方向时应当判定为暴击。");
        }

        [Test]
        public void Fire_MissingTarget_RaisesEventWithoutDamage()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            m_Probe.ReturnsHit = false;
            var firedCount = 0;
            var damageCount = 0;

            using (m_Bus.Subscribe<WeaponFiredEvent>(_ => firedCount++))
            using (m_Bus.Subscribe<DamageAppliedEvent>(_ => damageCount++))
            {
                m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
                m_Controller.Tick(0.016f);
            }

            Assert.AreEqual(1, firedCount, "打空也应当广播开火事件，否则玩家会以为枪没响。");
            Assert.AreEqual(0, damageCount, "没有命中时不应产生伤害事件。");
        }

        [Test]
        public void EmptyMagazine_DoesNotConsumeAmmoOrDamageTarget()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            ArrangeHit(critical: false);

            EmptyMagazine();

            Assert.AreEqual(0, m_Controller.Runtime.MagazineAmmo, "连续射击应当打空弹匣。");

            m_World.TryGet(m_TargetId, out var target);
            var healthBefore = target.Health;
            m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
            m_Controller.Tick(0.11f);

            Assert.AreEqual(0, m_Controller.Runtime.MagazineAmmo, "空弹匣继续开火不应把数量减成负数。");
            Assert.AreEqual(healthBefore, target.Health, 1e-3f, "空弹匣开火不应当造成伤害。");
        }

        [Test]
        public void Reload_WithoutMatchingAmmo_ReturnsNoAmmoCode()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);

            // 先把弹匣打空，此时背包里没有任何弹药，换弹应当因为找不到匹配口径而失败。
            EmptyMagazine();

            var result = m_Router.Dispatch(new PlayerReloadIntent(0));

            Assert.IsFalse(result.Success, "没有弹药时换弹应当失败。");
            Assert.AreEqual(CommandCodes.CombatNoAmmo, result.Code, "失败原因应当是背包里没有匹配口径的弹药。");
        }

        [Test]
        public void Reload_FullMagazine_ReturnsFullCode()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            PutAmmo(60);

            var result = m_Router.Dispatch(new PlayerReloadIntent(0));

            Assert.IsFalse(result.Success, "弹匣已满时换弹应当失败。");
            Assert.AreEqual(CommandCodes.CombatMagazineFull, result.Code, "失败原因应当是弹匣已满。");
        }

        [Test]
        public void Reload_ConsumesAmmoFromBackpackAndFillsMagazine()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            PutAmmo(60);
            EmptyMagazine();
            var ammoBefore = AmmoReserve.CountAvailable(m_AmmoPouch, "9x19");

            var result = m_Router.Dispatch(new PlayerReloadIntent(0));
            m_Controller.Tick(1.1f);

            Assert.IsTrue(result.Success, "有弹药时换弹应当被接受。");
            Assert.AreEqual(30, m_Controller.Runtime.MagazineAmmo, "换弹完成后弹匣应当装满。");
            Assert.AreEqual(ammoBefore - 30, AmmoReserve.CountAvailable(m_AmmoPouch, "9x19"),
                "弹药挂里的弹药应当减少恰好一个弹匣的量。");
        }

        [Test]
        public void Reload_WithoutEnoughAmmo_PartiallyFillsAndConsumesEverything()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            PutAmmo(8);
            EmptyMagazine();

            m_Router.Dispatch(new PlayerReloadIntent(0));
            m_Controller.Tick(1.1f);

            Assert.AreEqual(8, m_Controller.Runtime.MagazineAmmo, "弹药不足时应当部分装填。");
            Assert.AreEqual(0, AmmoReserve.CountAvailable(m_AmmoPouch, "9x19"),
                "弹药挂里的弹药应当被全部取走，不应有剩余。");
        }

        [Test]
        public void Reload_Failure_LeavesBackpackUntouched()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            PutAmmo(60);
            var ammoBefore = AmmoReserve.CountAvailable(m_AmmoPouch, "9x19");
            var magazineBefore = m_Controller.Runtime.MagazineAmmo;

            // 弹匣是满的，这次换弹请求必然失败。
            m_Router.Dispatch(new PlayerReloadIntent(0));
            m_Controller.Tick(1.1f);

            Assert.AreEqual(ammoBefore, AmmoReserve.CountAvailable(m_AmmoPouch, "9x19"),
                "失败的换弹不应当消耗任何弹药。");
            Assert.AreEqual(magazineBefore, m_Controller.Runtime.MagazineAmmo,
                "失败的换弹不应当改变弹匣数量。");
        }

        [Test]
        public void Equip_NullWeapon_ClearsRuntime()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            Assert.IsTrue(m_Controller.Weapon.IsEquipped, "装备后应当持有武器。");

            m_Controller.SyncEquippedWeapon(null);

            Assert.IsFalse(m_Controller.Weapon.IsEquipped, "卸下武器后运行时应被清空。");
        }

        /// <summary>
        /// 一直开火直到弹匣打空。
        /// </summary>
        /// <remarks>
        /// 用"打到空为止"而不是"循环固定次数"：固定次数会把射速与首发时机等细节
        /// 一并写进测试，武器参数一调整测试就失败，而失败信息看起来像是换弹坏了。
        /// 循环上界只是防止死循环的保险。
        /// </remarks>
        private void EmptyMagazine()
        {
            var guard = 0;
            while (m_Controller.Runtime.MagazineAmmo > 0 && guard < 200)
            {
                guard++;
                m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
                m_Controller.Tick(0.11f);
            }

            Assert.AreEqual(0, m_Controller.Runtime.MagazineAmmo, "开火循环应当把弹匣打空。");

            // 松开扳机：否则换弹完成的那一帧会立刻续上一发（这是刻意的设计，
            // 见 WeaponRuntime.UpdateTrigger 的说明），测试里的换弹断言会被它干扰。
            m_Controller.SetTriggerHeld(false);
        }

        [Test]
        public void Reload_WithTriggerHeld_ResumesFireImmediately()
        {
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            PutAmmo(60);
            EmptyMagazine();

            // 重新按住扳机再换弹，验证"换完弹立刻恢复火力"是刻意保留的行为。
            m_Router.Dispatch(new PlayerFireIntent(0, Vector2F.Right));
            m_Router.Dispatch(new PlayerReloadIntent(0));
            m_Controller.Tick(1.1f);

            Assert.AreEqual(29, m_Controller.Runtime.MagazineAmmo,
                "按住扳机时换弹完成应当立刻续上一发，这是全自动武器的预期行为。");
        }
    }
}
