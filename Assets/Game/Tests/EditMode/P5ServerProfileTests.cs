using System.IO;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// P5：服务端进度存档（共享仓库 + 账号进度）的规则测试。
    /// </summary>
    /// <remarks>
    /// <para>这些用例回答的是验收里那几条"玩家能看见的事实"：仓库是房间共用的、
    /// 两个账号各算各的、服务器重启后东西还在、账号档案里不会重复写一份仓库。</para>
    ///
    /// <para>它们全部在临时目录里跑，不碰真实存档（<see cref="SaveFileStore"/> 支持传入目录）。</para>
    /// </remarks>
    [TestFixture]
    public sealed class P5ServerProfileTests
    {
        private string m_Directory;
        private TestItemLookup m_Catalog;

        /// <summary>每个用例一份独立目录：互不影响，也不会污染开发机上的存档。</summary>
        [SetUp]
        public void SetUp()
        {
            m_Directory = Path.Combine(Path.GetTempPath(), "raid_demo_p5_" + Path.GetRandomFileName());
            Directory.CreateDirectory(m_Directory);

            m_Catalog = new TestItemLookup()
                .Add(new TestItemDefinition("test.rifle", baseValue: 1000, width: 2, height: 1))
                .Add(new TestItemDefinition(
                    "test.ammo", category: ItemCategory.Ammo, baseValue: 10, maxStack: 60));
        }

        /// <summary>临时目录用完即清（否则测试机上的临时目录会攒一堆存档）。</summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Directory))
            {
                Directory.Delete(m_Directory, recursive: true);
            }
        }

        [Test]
        public void 共享仓库在两个账号之间是同一个网格()
        {
            var store = new ServerProfileStore(m_Directory);
            Assert.IsTrue(store.Configure(m_Catalog), "注入目录后存档应当可用。");

            var first = store.GetOrCreate("甲");
            var second = store.GetOrCreate("乙");

            Assert.AreSame(store.SharedStash, first.Stash);
            Assert.AreSame(store.SharedStash, second.Stash);
            Assert.AreNotSame(first.Loadout.Backpack, second.Loadout.Backpack);
        }

        [Test]
        public void 账号进度在重启之后仍然读得回来()
        {
            var factory = new ItemFactory();

            var store = new ServerProfileStore(m_Directory);
            store.Configure(m_Catalog);

            var profile = store.GetOrCreate("甲");
            profile.AttachCatalog(m_Catalog);
            profile.AddMoney(777);
            Assert.IsTrue(profile.Stash.AutoPlace(factory.Create(m_Catalog.GetForTest("test.rifle"))).Success);
            Assert.IsTrue(store.SaveAll(), "落盘应当成功。");

            // 模拟"服务器重启"：换一个实例读同一个目录。
            var reopened = new ServerProfileStore(m_Directory);
            reopened.Configure(m_Catalog);
            var restored = reopened.GetOrCreate("甲");

            // 新账号自带启动资金，因此这里断言的是"启动资金 + 本次增加"。
            Assert.AreEqual(MetaProgress.StartingMoney + 777, restored.Money, "金币必须落库。");
            Assert.AreEqual(1, restored.Stash.Items.Count, "共享仓库里的物品必须落库。");
            Assert.AreEqual("test.rifle", restored.Stash.Items[0].Definition.Id);
        }

        [Test]
        public void 两个账号的金币互不影响()
        {
            var store = new ServerProfileStore(m_Directory);
            store.Configure(m_Catalog);

            var first = store.GetOrCreate("甲");
            var second = store.GetOrCreate("乙");

            first.AddMoney(500);

            Assert.AreEqual(MetaProgress.StartingMoney + 500, first.Money);
            Assert.AreEqual(MetaProgress.StartingMoney, second.Money, "另一个账号的余额不该被影响。");

            store.SaveAll();

            var reopened = new ServerProfileStore(m_Directory);
            reopened.Configure(m_Catalog);
            Assert.AreEqual(MetaProgress.StartingMoney + 500, reopened.GetOrCreate("甲").Money);
            Assert.AreEqual(MetaProgress.StartingMoney, reopened.GetOrCreate("乙").Money);
        }

        [Test]
        public void 账号记录里不重复保存共享仓库()
        {
            var factory = new ItemFactory();

            var store = new ServerProfileStore(m_Directory);
            store.Configure(m_Catalog);

            var profile = store.GetOrCreate("甲");
            profile.AttachCatalog(m_Catalog);
            Assert.IsTrue(profile.Stash.AutoPlace(factory.Create(m_Catalog.GetForTest("test.rifle"))).Success);
            store.SaveAll();

            // 直接读盘：仓库物品只应存在于文档级的 sharedStash 里。
            var file = new SaveFileStore(m_Directory, ServerProfileStore.DefaultFileName);
            Assert.IsTrue(file.TryLoad<ServerProfilesDocument>(out var document, out var error), error);
            Assert.AreEqual(1, document.sharedStash.Length, "共享仓库写在文档级。");
            Assert.AreEqual(1, document.profiles.Length);
            Assert.AreEqual(0, document.profiles[0].progress.stash.Length, "账号记录里不该再写一份仓库。");
        }

        [Test]
        public void 基础装备每个账号只发一次()
        {
            // AR-08：基础装备是"从零开始"的一次性兜底，撤离入库之后不得再发一套。
            var catalog = new TestItemLookup()
                .Add(new TestItemDefinition(
                    "weapon.rifle.ak74", category: ItemCategory.Weapon,
                    width: 2, height: 1, baseValue: 1000, weaponStats: new TestWeaponStats()))
                .Add(new TestItemDefinition(
                    "ammo.5.45.standard", category: ItemCategory.Ammo, baseValue: 10, maxStack: 60));

            var store = new ServerProfileStore(m_Directory);
            Assert.IsTrue(store.Configure(catalog));

            var profile = store.GetOrCreate("新兵");
            Assert.IsTrue(store.WasStarterKitIssued("新兵"), "建号时应当记为已发。");
            Assert.IsNotNull(
                profile.Loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon), "建号应配发一把武器。");

            // 模拟撤离：随身装备全部进共享仓库，随身变空。
            var deposited = profile.DepositLoadoutToStash();
            Assert.Greater(deposited, 0, "先模拟撤离入库。");

            var again = store.GetOrCreate("新兵");
            Assert.AreSame(profile, again);
            Assert.IsNull(
                again.Loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon),
                "撤离之后不得再发一套基础装备。");

            // 重启后标记也要读得回来（否则重建进度时会再发一套）。
            Assert.IsTrue(store.SaveAll(), "落盘应成功。");
            var reopened = new ServerProfileStore(m_Directory);
            Assert.IsTrue(reopened.Configure(catalog));
            reopened.GetOrCreate("新兵");
            Assert.IsTrue(reopened.WasStarterKitIssued("新兵"), "重启后仍应记得已经发过。");
        }
    }
}
