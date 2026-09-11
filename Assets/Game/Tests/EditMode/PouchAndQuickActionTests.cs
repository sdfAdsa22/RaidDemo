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
    /// 弹药挂与双击优先级测试。
    /// </summary>
    /// <remarks>
    /// <para>这一组覆盖三件事：弹药挂的分类过滤、双击的目标决策、以及"换弹只从弹药挂取弹"。</para>
    /// <para>三者的共同点是**都有多条分支与回退路径**——写成界面代码就只能靠手点验证，
    /// 写进规则层之后可以用精确的用例逐条锁死。</para>
    /// </remarks>
    [TestFixture]
    public sealed class PouchAndQuickActionTests
    {
        private ItemFactory m_Factory;
        private TestItemDefinition m_Ammo;
        private TestItemDefinition m_Rifle;
        private TestItemDefinition m_Helmet;
        private TestItemDefinition m_Loot;

        /// <summary>可控的射线检测替身：始终打空。</summary>
        private sealed class MissProbe : IHitProbe
        {
            /// <inheritdoc />
            public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
            {
                hit = default;
                return false;
            }
        }

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Ammo = new TestItemDefinition(
                "ammo.5.45", ItemCategory.Ammo, maxStack: 90,
                ammoStats: new TestAmmoStats("5.45", 22f));
            m_Rifle = new TestItemDefinition(
                "weapon.rifle", ItemCategory.Weapon, width: 3, height: 1);
            m_Helmet = new TestItemDefinition(
                "armor.helmet", ItemCategory.Helmet, width: 2, height: 2);
            m_Loot = new TestItemDefinition("loot.watch", ItemCategory.Loot);
        }

        // ---------- 弹药挂的分类过滤 ----------

        [Test]
        public void AmmoPouch_AcceptsAmmo()
        {
            var pouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);

            var result = pouch.AutoPlace(m_Factory.Create(m_Ammo, 30));

            Assert.IsTrue(result.Success, "弹药挂应当接受弹药。");
            Assert.AreEqual(1, pouch.Items.Count, "弹药挂里应当有一堆弹药。");
        }

        [Test]
        public void AmmoPouch_RejectsNonAmmo()
        {
            var pouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);

            var weapon = pouch.AutoPlace(m_Factory.Create(m_Rifle));
            var loot = pouch.AutoPlace(m_Factory.Create(m_Loot));

            Assert.AreEqual(InventoryFailure.CategoryNotAllowed, weapon.Failure,
                "弹药挂不应当接受武器。");
            Assert.AreEqual(InventoryFailure.CategoryNotAllowed, loot.Failure,
                "弹药挂不应当接受值钱小物件。");
            Assert.AreEqual(0, pouch.Items.Count, "被拒绝的物品不应留在弹药挂里。");
        }

        [Test]
        public void AmmoPouch_IsOneRowOfFiveCells()
        {
            var pouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);

            Assert.AreEqual(5, pouch.Width, "弹药挂应为一行五格。");
            Assert.AreEqual(1, pouch.Height, "弹药挂应为一行五格。");
            Assert.AreEqual(ItemCategory.Ammo, pouch.AcceptedCategory, "弹药挂只接受弹药。");
        }

        // ---------- 双击优先级 ----------

        /// <summary>搭一套"背包 + 弹药挂 + 战利品箱 + 装备栏"的上下文。</summary>
        private QuickActionContext CreateContext(
            out InventoryGrid backpack,
            out InventoryGrid pouch,
            out InventoryGrid loot)
        {
            backpack = new InventoryGrid(4, 4, "主背包");
            pouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);
            loot = new InventoryGrid(4, 4, "战利品箱");

            return new QuickActionContext
            {
                Backpack = backpack,
                AmmoPouch = pouch,
                Loot = loot,
                Equipment = new EquipmentLoadout(),
                BackpackContainerId = 1,
                AmmoPouchContainerId = 2,
                LootContainerId = 3,
            };
        }

        [Test]
        public void DoubleClick_AmmoInLoot_GoesToPouchFirst()
        {
            var context = CreateContext(out _, out _, out var loot);
            var ammo = m_Factory.Create(m_Ammo, 30);
            loot.AutoPlace(ammo);

            var action = QuickActionResolver.Resolve(context, loot, ammo);

            Assert.AreEqual(QuickActionKind.MoveToContainer, action.Kind, "双击弹药应当移动，而不是装备。");
            Assert.AreEqual(context.AmmoPouchContainerId, action.TargetContainerId,
                "弹药应当优先进入弹药挂，而不是背包。");
        }

        [Test]
        public void DoubleClick_AmmoInBackpack_GoesToPouchFirst()
        {
            var context = CreateContext(out var backpack, out _, out _);
            var ammo = m_Factory.Create(m_Ammo, 30);
            backpack.AutoPlace(ammo);

            var action = QuickActionResolver.Resolve(context, backpack, ammo);

            Assert.AreEqual(context.AmmoPouchContainerId, action.TargetContainerId,
                "背包里的弹药双击后应当进入弹药挂。");
        }

        [Test]
        public void DoubleClick_AmmoWhenPouchFull_FallsBackToBackpack()
        {
            var context = CreateContext(out _, out var pouch, out var loot);

            // 把弹药挂填满：五格各堆一批，装到上限。
            for (var i = 0; i < pouch.Width; i++)
            {
                pouch.AutoPlace(m_Factory.Create(m_Ammo, m_Ammo.MaxStack));
            }

            var ammo = m_Factory.Create(m_Ammo, 10);
            loot.AutoPlace(ammo);

            var action = QuickActionResolver.Resolve(context, loot, ammo);

            Assert.AreEqual(context.BackpackContainerId, action.TargetContainerId,
                "弹药挂装满时应当退回背包，而不是什么都不做。");
        }

        [Test]
        public void DoubleClick_Weapon_EquipsToPrimaryWeapon()
        {
            var context = CreateContext(out var backpack, out _, out _);
            var rifle = m_Factory.Create(m_Rifle);
            backpack.AutoPlace(rifle);

            var action = QuickActionResolver.Resolve(context, backpack, rifle);

            Assert.AreEqual(QuickActionKind.Equip, action.Kind, "双击武器应当是装备动作。");
            Assert.AreEqual(EquipmentSlot.PrimaryWeapon, action.Slot, "主武器槽为空时应当装到主武器槽。");
        }

        [Test]
        public void DoubleClick_Weapon_WithPrimaryTaken_UsesSecondary()
        {
            var context = CreateContext(out var backpack, out _, out _);
            context.Equipment.Equip(m_Factory.Create(m_Rifle), EquipmentSlot.PrimaryWeapon);
            var rifle = m_Factory.Create(m_Rifle);
            backpack.AutoPlace(rifle);

            var action = QuickActionResolver.Resolve(context, backpack, rifle);

            Assert.AreEqual(EquipmentSlot.SecondaryWeapon, action.Slot, "主武器槽被占时应当装到副武器槽。");
        }

        [Test]
        public void DoubleClick_Weapon_WithBothSlotsTaken_ReplacesPrimary()
        {
            var context = CreateContext(out var backpack, out _, out _);
            context.Equipment.Equip(m_Factory.Create(m_Rifle), EquipmentSlot.PrimaryWeapon);
            context.Equipment.Equip(m_Factory.Create(m_Rifle), EquipmentSlot.SecondaryWeapon);
            var rifle = m_Factory.Create(m_Rifle);
            backpack.AutoPlace(rifle);

            var action = QuickActionResolver.Resolve(context, backpack, rifle);

            Assert.AreEqual(EquipmentSlot.PrimaryWeapon, action.Slot,
                "两个武器槽都满时应当替换主武器——双击表达的意图是'我要用这个'。");
        }

        [Test]
        public void DoubleClick_Helmet_EquipsToHead()
        {
            var context = CreateContext(out var backpack, out _, out _);
            var helmet = m_Factory.Create(m_Helmet);
            backpack.AutoPlace(helmet);

            var action = QuickActionResolver.Resolve(context, backpack, helmet);

            Assert.AreEqual(QuickActionKind.Equip, action.Kind, "双击头盔应当是装备动作。");
            Assert.AreEqual(EquipmentSlot.Head, action.Slot, "头盔应当装到头盔槽。");
        }

        [Test]
        public void DoubleClick_LootItem_MovesBetweenBackpackAndLoot()
        {
            var context = CreateContext(out var backpack, out _, out var loot);
            var watch = m_Factory.Create(m_Loot);
            loot.AutoPlace(watch);

            var action = QuickActionResolver.Resolve(context, loot, watch);

            Assert.AreEqual(QuickActionKind.MoveToContainer, action.Kind, "值钱小物件应当是移动动作。");
            Assert.AreEqual(context.BackpackContainerId, action.TargetContainerId,
                "战利品箱里的普通物品应当进背包。");
        }

        // ---------- 换弹只从弹药挂取弹 ----------

        [Test]
        public void Reload_WithAmmoOnlyInBackpack_Fails()
        {
            var backpack = new InventoryGrid(4, 4, "主背包");
            var pouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);
            var loadout = new PlayerLoadout(backpack, new EquipmentLoadout(), pouch);
            var bus = new EventBus();
            var weaponStats = new TestWeaponStats(
                magazineCapacity: 30, caliberId: "5.45", reloadSeconds: 0.1f);
            var controller = new PlayerWeaponController(
                new PlayerWeapon(new DeterministicRandom(1u)), loadout, new MissProbe(),
                new CombatWorld(), CombatTuning.Default, bus);
            controller.SyncEquippedWeapon(weaponStats);

            // 弹药只放进背包，弹药挂是空的。
            backpack.AutoPlace(m_Factory.Create(m_Ammo, 90));
            EmptyMagazine(controller, bus);

            var accepted = controller.TryRequestReload(0, 1u, out var failureCode);

            Assert.IsFalse(accepted, "弹药挂为空时换弹应当失败，即使背包里有弹药。");
            Assert.AreEqual(CommandCodes.CombatNoAmmo, failureCode, "失败原因应当是弹药挂里没有匹配弹药。");
        }

        [Test]
        public void Reload_WithAmmoInPouch_ConsumesFromPouchOnly()
        {
            var backpack = new InventoryGrid(4, 4, "主背包");
            var pouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);
            var loadout = new PlayerLoadout(backpack, new EquipmentLoadout(), pouch);
            var bus = new EventBus();
            var weaponStats = new TestWeaponStats(
                magazineCapacity: 30, caliberId: "5.45", reloadSeconds: 0.1f);
            var controller = new PlayerWeaponController(
                new PlayerWeapon(new DeterministicRandom(1u)), loadout, new MissProbe(),
                new CombatWorld(), CombatTuning.Default, bus);
            controller.SyncEquippedWeapon(weaponStats);

            backpack.AutoPlace(m_Factory.Create(m_Ammo, 90));
            pouch.AutoPlace(m_Factory.Create(m_Ammo, 30));
            EmptyMagazine(controller, bus);

            var accepted = controller.TryRequestReload(0, 1u, out _);
            for (var i = 0; i < 10 && controller.Runtime.IsReloading; i++)
            {
                controller.Tick(0.05f);
            }

            Assert.IsTrue(accepted, "弹药挂里有弹药时换弹应当被接受。");
            Assert.AreEqual(30, controller.Runtime.MagazineAmmo, "弹匣应当被补满。");
            Assert.AreEqual(0, AmmoReserve.CountAvailable(pouch, "5.45"), "弹药挂里的弹药应当被取空。");
            Assert.AreEqual(90, AmmoReserve.CountAvailable(backpack, "5.45"),
                "背包里的弹药不应当被动用——换弹只从弹药挂取弹。");
        }

        /// <summary>把弹匣打空，便于测试换弹。</summary>
        private static void EmptyMagazine(PlayerWeaponController controller, EventBus bus)
        {
            controller.SetMuzzlePosition(Vector3.up);
            controller.SetAimDirection(Vector2F.Right);
            controller.SetTriggerHeld(true);

            var guard = 0;
            while (controller.Runtime.MagazineAmmo > 0 && guard < 200)
            {
                guard++;
                controller.Tick(0.2f);
            }

            controller.SetTriggerHeld(false);
            Assert.AreEqual(0, controller.Runtime.MagazineAmmo, "开火循环应当把弹匣打空。");
        }
    }
}
