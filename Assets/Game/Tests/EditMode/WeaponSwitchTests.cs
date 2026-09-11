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
    /// 切换武器与"每把武器各自保留弹匣"的测试。
    /// </summary>
    /// <remarks>
    /// 这一组里最重要的是 <see cref="SwitchWeapon_KeepsEachMagazineSeparately"/>：
    /// 它锁住的是一个**能被玩家立刻利用的漏洞**——如果切枪会重建武器运行时，
    /// 那么"打空 A、切到 B、再切回 A"就能白得一满弹匣，弹药系统形同虚设。
    /// </remarks>
    [TestFixture]
    public sealed class WeaponSwitchTests
    {
        /// <summary>始终打空的射线替身。</summary>
        private sealed class MissProbe : IHitProbe
        {
            /// <inheritdoc />
            public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
            {
                hit = default;
                return false;
            }
        }

        private ItemFactory m_Factory;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
        }

        /// <summary>搭一套最小可用的武器控制器。</summary>
        private PlayerWeaponController CreateController(
            EquipmentLoadout equipment,
            out EventBus bus,
            out PlayerLoadout loadout)
        {
            var backpack = new InventoryGrid(5, 5, "主背包");
            loadout = new PlayerLoadout(backpack, equipment);
            bus = new EventBus();

            return new PlayerWeaponController(
                new PlayerWeapon(new DeterministicRandom(9u)),
                loadout,
                new MissProbe(),
                new CombatWorld(),
                CombatTuning.Default,
                bus);
        }

        [Test]
        public void Swap_ExchangesTwoSlots()
        {
            var equipment = new EquipmentLoadout();
            var rifle = m_Factory.Create(new TestItemDefinition("rifle", Data.ItemCategory.Weapon));
            var pistol = m_Factory.Create(new TestItemDefinition("pistol", Data.ItemCategory.Weapon));
            equipment.Equip(rifle, EquipmentSlot.PrimaryWeapon);
            equipment.Equip(pistol, EquipmentSlot.SecondaryWeapon);

            var swapped = equipment.Swap(EquipmentSlot.PrimaryWeapon, EquipmentSlot.SecondaryWeapon);

            Assert.IsTrue(swapped, "两个不同的槽位应当可以交换。");
            Assert.AreSame(pistol, equipment.Get(EquipmentSlot.PrimaryWeapon), "主武器槽应当变成手枪。");
            Assert.AreSame(rifle, equipment.Get(EquipmentSlot.SecondaryWeapon), "副武器槽应当变成步枪。");
        }

        [Test]
        public void Swap_SameSlot_DoesNothing()
        {
            var equipment = new EquipmentLoadout();

            Assert.IsFalse(
                equipment.Swap(EquipmentSlot.PrimaryWeapon, EquipmentSlot.PrimaryWeapon),
                "与自身交换应当返回 false。");
        }

        [Test]
        public void SwitchCommand_ExchangesWeaponSlots()
        {
            var equipment = new EquipmentLoadout();
            var rifle = m_Factory.Create(new TestItemDefinition("rifle", Data.ItemCategory.Weapon));
            var pistol = m_Factory.Create(new TestItemDefinition("pistol", Data.ItemCategory.Weapon));
            equipment.Equip(rifle, EquipmentSlot.PrimaryWeapon);
            equipment.Equip(pistol, EquipmentSlot.SecondaryWeapon);

            var bus = new EventBus();
            var router = new CommandRouter();
            router.Register(new WeaponSwitchCommandHandler(equipment, bus));

            var result = router.Dispatch(new PlayerSwitchWeaponIntent(0, 1));

            Assert.IsTrue(result.Success, "有武器时切换命令应当成功。");
            Assert.AreSame(pistol, equipment.Get(EquipmentSlot.PrimaryWeapon), "切换后手持的应当是副武器槽里的枪。");
        }

        [Test]
        public void SwitchCommand_WithNoWeapons_Fails()
        {
            var equipment = new EquipmentLoadout();
            var bus = new EventBus();
            var router = new CommandRouter();
            router.Register(new WeaponSwitchCommandHandler(equipment, bus));

            var result = router.Dispatch(new PlayerSwitchWeaponIntent(0, 1));

            Assert.IsFalse(result.Success, "两个武器槽都空时切换应当失败。");
            Assert.AreEqual(CommandCodes.CombatNoWeapon, result.Code, "失败原因应当是主武器槽为空。");
        }

        [Test]
        public void SwitchWeapon_KeepsEachMagazineSeparately()
        {
            var equipment = new EquipmentLoadout();
            var controller = CreateController(equipment, out _, out _);

            var rifle = new TestWeaponStats(magazineCapacity: 30, caliberId: "5.45");
            var pistol = new TestWeaponStats(magazineCapacity: 8, caliberId: "9x19");

            // 用步枪打掉 5 发。
            controller.SyncEquippedWeapon(rifle);
            FireShots(controller, 5);
            Assert.AreEqual(25, controller.Runtime.MagazineAmmo, "步枪弹匣应当剩 25 发。");

            // 切到手枪，打掉 3 发。
            controller.SyncEquippedWeapon(pistol);
            FireShots(controller, 3);
            Assert.AreEqual(5, controller.Runtime.MagazineAmmo, "手枪弹匣应当剩 5 发。");

            // 切回步枪：弹匣必须还是 25 发，而不是被重置成一满匣。
            controller.SyncEquippedWeapon(rifle);

            Assert.AreEqual(25, controller.Runtime.MagazineAmmo,
                "切回旧武器时弹匣数量必须保持，否则切枪等于免费换弹。");

            // 再切回手枪，验证两把枪的状态都各自保留着。
            controller.SyncEquippedWeapon(pistol);

            Assert.AreEqual(5, controller.Runtime.MagazineAmmo, "手枪的弹匣同样应当保持。");
        }

        /// <summary>连续开火指定次数（每次开火之间推进足够的时间）。</summary>
        private static void FireShots(PlayerWeaponController controller, int shots)
        {
            controller.SetMuzzlePosition(Vector3.up);
            controller.SetAimDirection(Vector2F.Right);
            controller.SetTriggerHeld(true);

            var fired = 0;
            var before = controller.Runtime.MagazineAmmo;
            var guard = 0;
            while (fired < shots && guard < 200)
            {
                guard++;
                controller.Tick(0.5f);
                var now = controller.Runtime.MagazineAmmo;
                if (now < before)
                {
                    fired += before - now;
                    before = now;
                }
            }

            controller.SetTriggerHeld(false);
        }
    }
}
