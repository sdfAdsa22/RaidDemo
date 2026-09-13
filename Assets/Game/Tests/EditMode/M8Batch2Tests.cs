using NUnit.Framework;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Raid;
using RaidDemo.Shared;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// M8 批次 2 的战斗侧测试：霰弹枪的多弹丸发射与 12 号口径换弹。
    /// </summary>
    /// <remarks>
    /// 弹丸机制改变了"一次扳机 = 一条射线"这个从 M3 起就没变过的假设，
    /// 因此这里既验证数量与消耗，也验证几何形状（扇面分布）与事件序号——
    /// 表现层靠 <c>PelletIndex == 0</c> 决定枪声与枪口火焰只播一次。
    /// </remarks>
    [TestFixture]
    public sealed class M8Batch2CombatTests
    {
        /// <summary>记录每次射线方向的可控探针。</summary>
        private sealed class RecordingHitProbe : IHitProbe
        {
            public readonly System.Collections.Generic.List<Vector3> Directions =
                new System.Collections.Generic.List<Vector3>();

            public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
            {
                Directions.Add(direction);
                hit = default;
                return false;
            }
        }

        private ItemFactory m_Factory;
        private EventBus m_Bus;
        private CommandRouter m_Router;
        private CombatWorld m_World;
        private RecordingHitProbe m_Probe;
        private PlayerWeaponController m_Controller;
        private InventoryGrid m_Backpack;
        private InventoryGrid m_AmmoPouch;
        private TestItemDefinition m_ShotgunAmmo;
        private TestItemDefinition m_PistolAmmo;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Bus = new EventBus();
            m_Router = new CommandRouter();
            m_World = new CombatWorld();
            m_Probe = new RecordingHitProbe();

            m_Backpack = new InventoryGrid(6, 6, "主背包");
            m_AmmoPouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);
            var loadout = new PlayerLoadout(m_Backpack, new EquipmentLoadout(), m_AmmoPouch);

            m_Controller = new PlayerWeaponController(
                new PlayerWeapon(new DeterministicRandom(2028u)),
                loadout,
                m_Probe,
                m_World,
                new CombatTuning(),
                m_Bus);
            m_Router.Register<PlayerReloadIntent>(new ReloadCommandHandler(m_Controller));
            m_Controller.SetMuzzlePosition(Vector3.zero);

            m_ShotgunAmmo = new TestItemDefinition(
                "ammo.12ga", ItemCategory.Ammo, maxStack: 30,
                ammoStats: new TestAmmoStats("12ga", 12f));
            m_PistolAmmo = new TestItemDefinition(
                "ammo.9x19", ItemCategory.Ammo, maxStack: 120,
                ammoStats: new TestAmmoStats("9x19", 15f));
        }

        private static TestWeaponStats BuildShotgun()
        {
            return new TestWeaponStats(
                baseDamage: 11f,
                roundsPerMinute: 75f,
                fireMode: WeaponFireMode.Single,
                burstCount: 1,
                magazineCapacity: 6,
                caliberId: "12ga",
                reloadSeconds: 3f,
                rangeMeters: 6f,
                pelletCount: 6,
                pelletSpreadDegrees: 7f);
        }

        [Test]
        public void 霰弹枪一发打出六颗弹丸且只消耗一发弹药()
        {
            m_Controller.SyncEquippedWeapon(BuildShotgun());
            m_AmmoPouch.AutoPlace(m_Factory.Create(m_ShotgunAmmo, 6));

            var events = new System.Collections.Generic.List<WeaponFiredEvent>();
            using (m_Bus.Subscribe<WeaponFiredEvent>(evt => events.Add(evt)))
            {
                m_Controller.SetTriggerHeld(true);
                m_Controller.Tick(0.016f);
            }

            Assert.AreEqual(6, events.Count, "六颗弹丸应当广播六条开火事件。");
            Assert.AreEqual(5, m_Controller.Runtime.MagazineAmmo, "一次扳机只消耗一发弹药。");
        }

        [Test]
        public void 弹丸序号从零开始且第一颗标记为每发一次()
        {
            m_Controller.SyncEquippedWeapon(BuildShotgun());
            var indexes = new System.Collections.Generic.List<int>();
            using (m_Bus.Subscribe<WeaponFiredEvent>(evt => indexes.Add(evt.PelletIndex)))
            {
                m_Controller.SetTriggerHeld(true);
                m_Controller.Tick(0.016f);
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, indexes,
                "弹丸序号必须从 0 连续排列：表现层按 PelletIndex == 0 过滤出『每发一次』的表现。");
        }

        [Test]
        public void 弹丸在扇面内均匀分布且方向对称()
        {
            m_Controller.SyncEquippedWeapon(BuildShotgun());
            m_Controller.SetAimWorldPoint(new Vector2F(10f, 0f));

            m_Controller.SetTriggerHeld(true);
            m_Controller.Tick(0.016f);

            Assert.AreEqual(6, m_Probe.Directions.Count);

            // 瞄准正右方（+X），因此每颗弹丸的方向角就是它的偏移角。
            var angles = new float[m_Probe.Directions.Count];
            for (var i = 0; i < angles.Length; i++)
            {
                angles[i] = Mathf.Atan2(m_Probe.Directions[i].z, m_Probe.Directions[i].x) * Mathf.Rad2Deg;
            }

            // 六颗弹丸分布在 -3.5° ~ +3.5° 之间，步长约 1.4°（扇面宽度 7° 五等分）。
            // 注意符号：Unity 绕 +Y 的正角把 +X 转向 -Z，因此扇面内的负偏移
            // 在 atan2(z, x) 读出来是正角——这里断言的是同一个扇面，只是坐标读数相反。
            for (var i = 0; i < angles.Length; i++)
            {
                var expected = 3.5f - (i * (7f / 5f));
                Assert.AreEqual(expected, angles[i], 0.05f,
                    $"第 {i} 颗弹丸的角度应当落在扇面的第 {i} 个等分点上。");
            }
        }

        [Test]
        public void 单弹丸武器的表现与旧版一致()
        {
            var rifle = new TestWeaponStats(
                baseDamage: 25f, roundsPerMinute: 600f, magazineCapacity: 30,
                caliberId: "5.45", rangeMeters: 12f);
            m_Controller.SyncEquippedWeapon(rifle);

            var count = 0;
            using (m_Bus.Subscribe<WeaponFiredEvent>(_ => count++))
            {
                m_Controller.SetTriggerHeld(true);
                m_Controller.Tick(0.016f);
            }

            Assert.AreEqual(1, count, "没有配置弹丸数的武器仍然是一发一条弹道。");
        }

        [Test]
        public void 霰弹枪换弹只接受十二号弹药()
        {
            m_Controller.SyncEquippedWeapon(BuildShotgun());

            // 先打空弹匣：满弹匣时换弹会被"弹匣是满的"挡下，测不到口径匹配这条路径。
            // 泵动霰弹枪的射速是 75 发/分（0.8 秒一发），每次先松开扳机空转冷却、再按下开火。
            for (var i = 0; i < 6; i++)
            {
                m_Controller.SetTriggerHeld(false);
                m_Controller.Tick(0.85f);
                m_Controller.SetTriggerHeld(true);
                m_Controller.Tick(0.016f);
            }

            m_Controller.SetTriggerHeld(false);
            Assert.AreEqual(0, m_Controller.Runtime.MagazineAmmo, "前六发应当打空弹匣。");

            // 弹药挂里只有 9x19：换弹必须失败，且失败原因是"没有匹配弹药"。
            m_AmmoPouch.AutoPlace(m_Factory.Create(m_PistolAmmo, 30));
            var wrongCaliber = m_Router.Dispatch(new PlayerReloadIntent(0));
            Assert.IsFalse(wrongCaliber.Success, "9x19 不能装进 12 号霰弹枪。");
            Assert.AreEqual(CommandCodes.CombatNoAmmo, wrongCaliber.Code);

            // 放入 12 号弹药后换弹成功，并从弹药挂扣掉。
            m_AmmoPouch.AutoPlace(m_Factory.Create(m_ShotgunAmmo, 6));
            var rightCaliber = m_Router.Dispatch(new PlayerReloadIntent(0));
            Assert.IsTrue(rightCaliber.Success, "弹药挂里有 12 号弹药时应当能换弹。");

            m_Controller.Tick(3.1f);
            Assert.AreEqual(6, m_Controller.Runtime.MagazineAmmo, "霰弹枪应当补满 6 发弹匣。");
            Assert.AreEqual(0, AmmoReserve.CountAvailable(m_AmmoPouch, "12ga"), "换弹必须消耗弹药挂里的 12 号弹药。");
            Assert.AreEqual(30, AmmoReserve.CountAvailable(m_AmmoPouch, "9x19"), "不匹配口径的弹药不能被消耗。");
        }
    }

    /// <summary>
    /// M8 批次 2 的内容资产测试：新物品、战斗参数、商人货架与掉落表的接线。
    /// </summary>
    /// <remarks>这些断言读的是生成出来的资产，因此它们同时是"内容生成器跑过没有"的守门人。</remarks>
    [TestFixture]
    public sealed class M8Batch2ContentTests
    {
        private const string CatalogPath = "Assets/Game/Content/Items/ItemCatalog.asset";

        private ItemCatalog m_Catalog;

        [SetUp]
        public void SetUp()
        {
            m_Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.IsNotNull(m_Catalog, $"物品目录缺失：{CatalogPath}，请先执行「RaidDemo/生成初始物品资产」。");
        }

        [Test]
        public void 批次二的新物品全部入目录()
        {
            foreach (var id in new[]
                     {
                         "weapon.smg.uzi", "weapon.shotgun.pump", "ammo.12ga.buck",
                         "armor.helmet.heavy", "armor.vest.heavy"
                     })
            {
                Assert.IsNotNull(m_Catalog.Get(id), $"{id} 不在物品目录里。");
            }

            Assert.AreEqual(18, m_Catalog.All.Count, "目录总数应当是原 13 件 + 新 5 件。");
        }

        [Test]
        public void 霰弹枪的弹丸参数与设计一致()
        {
            var shotgun = m_Catalog.Get("weapon.shotgun.pump");
            var stats = shotgun.WeaponStats;

            Assert.IsNotNull(stats, "霰弹枪没有战斗参数。");
            Assert.AreEqual(6, stats.PelletCount, "霰弹枪一发六颗弹丸。");
            Assert.Greater(stats.PelletSpreadDegrees, 0f, "多弹丸武器必须给出扇面宽度。");
            Assert.AreEqual("12ga", stats.CaliberId);
            Assert.AreEqual(6f, stats.RangeMeters, 0.01f, "霰弹枪射程按设计取 6 米。");
        }

        [Test]
        public void 四级护甲的等级与耐久符合设计()
        {
            var vest = m_Catalog.Get("armor.vest.heavy").ArmorStats;
            var helmet = m_Catalog.Get("armor.helmet.heavy").ArmorStats;

            Assert.IsNotNull(vest, "重型背心没有护甲参数。");
            Assert.AreEqual(4, vest.ProtectionLevel, "重型背心是四级甲。");
            Assert.AreEqual(90f, vest.MaxDurability, 0.01f);

            Assert.IsNotNull(helmet, "重型头盔没有护甲参数。");
            Assert.AreEqual(4, helmet.ProtectionLevel, "重型头盔是四级甲。");
            Assert.AreEqual(60f, helmet.MaxDurability, 0.01f);
        }

        [Test]
        public void 商人货架包含全部新商品()
        {
            var trader = TraderCatalog.CreateDefault();

            foreach (var id in new[]
                     {
                         "weapon.smg.uzi", "weapon.shotgun.pump", "ammo.12ga.buck",
                         "armor.helmet.heavy", "armor.vest.heavy"
                     })
            {
                Assert.IsTrue(trader.TryGetEntry(id, out var entry), $"{id} 不在商人货架上。");
                Assert.Greater(entry.BundleCount, 0);
            }
        }

        [Test]
        public void 掉落表合法且武器架会掉落新枪()
        {
            Assert.IsNull(LootContainerCatalog.Validate(m_Catalog),
                "掉落表引用了不存在或不合法的物品。");

            var weaponRack = LootContainerCatalog.Get("crate.weapon");
            var hasShotgun = false;
            foreach (var entry in weaponRack.Table.Entries)
            {
                if (entry.ItemId == "weapon.shotgun.pump")
                {
                    hasShotgun = true;
                }
            }

            Assert.IsTrue(hasShotgun, "武器架应当有概率掉落霰弹枪。");
        }
    }
}
