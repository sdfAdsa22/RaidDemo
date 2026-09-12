using System;
using System.IO;
using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 局外存档测试：往返一致、未知物品容错与损坏回退。
    /// </summary>
    /// <remarks>
    /// 存档是玩家投入时间的唯一凭证。测试不只验证"能存能读"，
    /// 还要验证"存档有问题时游戏不会崩，并且尽量少丢东西"。
    /// </remarks>
    [TestFixture]
    public sealed class MetaSaveTests
    {
        private ItemFactory m_Factory;
        private TestItemLookup m_Catalog;
        private TestItemDefinition m_Bolt;
        private TestItemDefinition m_Ammo;
        private TestItemDefinition m_Pistol;
        private TestItemDefinition m_Vest;
        private TestItemDefinition m_Backpack;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Bolt = new TestItemDefinition(
                "loot.bolt", ItemCategory.Loot, baseValue: 600, maxStack: 20);
            m_Ammo = new TestItemDefinition(
                "ammo.9x19", ItemCategory.Ammo, baseValue: 8, maxStack: 120, canRotate: false);
            m_Pistol = new TestItemDefinition(
                "weapon.pistol", ItemCategory.Weapon, width: 2, height: 1);
            m_Vest = new TestItemDefinition(
                "armor.vest", ItemCategory.BodyArmor, width: 2, height: 3);
            m_Backpack = new TestItemDefinition(
                "backpack.small", ItemCategory.Backpack, width: 3, height: 3,
                containerWidth: 6, containerHeight: 6);
            m_Catalog = new TestItemLookup()
                .Add(m_Bolt)
                .Add(m_Ammo)
                .Add(m_Pistol)
                .Add(m_Vest)
                .Add(m_Backpack);
        }

        [Test]
        public void 存档往返保留余额仓库装备与任务()
        {
            var progress = BuildProgress();
            var data = MetaSaveMapper.Capture(progress, raidInProgress: false);

            Assert.AreEqual(progress.Money, data.money);
            Assert.AreEqual(6, data.backpackWidth);
            Assert.IsNotNull(data.quests);

            var restored = MetaSaveMapper.Restore(data, m_Catalog, out var problems);

            Assert.IsEmpty(problems);
            Assert.IsNotNull(restored);
            Assert.AreEqual(progress.Money, restored.Money);
            Assert.AreEqual(5, CountStash(restored, m_Bolt.Id));
            Assert.AreEqual(30, restored.Loadout.Backpack.Items.Count > 0
                ? restored.Loadout.Backpack.Items[0].StackCount
                : 0);
            Assert.AreEqual(15, restored.Loadout.AmmoPouch.Items[0].StackCount);
            Assert.IsNotNull(restored.Loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon));
            Assert.IsNotNull(restored.Loadout.Equipment.Get(EquipmentSlot.Body));
            Assert.IsNotNull(restored.Loadout.Equipment.Get(EquipmentSlot.Backpack));
            Assert.AreEqual(
                QuestState.Claimed,
                restored.Quests.Get(QuestCatalog.FirstExtractId).State);
            Assert.AreEqual(
                2,
                restored.Quests.Get(QuestCatalog.ScavengerId).Current);
        }

        [Test]
        public void 未知物品被跳过而不是让整份存档失败()
        {
            var data = new MetaSaveData
            {
                schemaVersion = MetaSaveData.CurrentSchemaVersion,
                money = 1234,
                stash = new[]
                {
                    new ItemStackSave { itemId = "unknown.item", count = 1 },
                    new ItemStackSave { itemId = m_Bolt.Id, count = 3, x = 0, y = 0 },
                },
            };

            var restored = MetaSaveMapper.Restore(data, m_Catalog, out var problems);

            Assert.IsNotNull(restored);
            Assert.IsNotEmpty(problems, "未知物品必须被记录，而不是静默消失。");
            Assert.AreEqual(1234, restored.Money);
            Assert.AreEqual(3, CountStash(restored, m_Bolt.Id), "其它物品仍应正常还原。");
        }

        [Test]
        public void 主存档损坏时回退到上一份备份()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "RaidDemoSaveTests_" + Guid.NewGuid().ToString("N"));
            var store = new SaveFileStore(directory, "test_save.json");

            try
            {
                Assert.IsTrue(store.Save(
                    new MetaSaveData { money = 100, schemaVersion = MetaSaveData.CurrentSchemaVersion },
                    out var error), error);
                Assert.IsTrue(store.Save(
                    new MetaSaveData { money = 200, schemaVersion = MetaSaveData.CurrentSchemaVersion },
                    out error), error);

                File.WriteAllText(store.FilePath, "{ 这不是合法 JSON");

                Assert.IsTrue(store.TryLoad<MetaSaveData>(out var loaded, out error), error);
                Assert.AreEqual(100, loaded.money, "主存档损坏时应回退到备份中的上一份余额。");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private MetaProgress BuildProgress()
        {
            var progress = new MetaProgress(7777);
            progress.AttachCatalog(m_Catalog);

            progress.Stash.AutoPlace(m_Factory.Create(m_Bolt, 5));

            // 模拟装备了 6x6 背包：容量由背包决定，存档必须连尺寸一起记住。
            progress.Loadout.ReplaceBackpack(new InventoryGrid(6, 6, "主背包"));
            progress.Loadout.Backpack.AutoPlace(m_Factory.Create(m_Ammo, 30));
            progress.Loadout.AmmoPouch.AutoPlace(m_Factory.Create(m_Ammo, 15));
            progress.Loadout.Equipment.Equip(m_Factory.Create(m_Pistol), EquipmentSlot.PrimaryWeapon);
            progress.Loadout.Equipment.Equip(m_Factory.Create(m_Vest), EquipmentSlot.Body);
            progress.Loadout.Equipment.Equip(m_Factory.Create(m_Backpack), EquipmentSlot.Backpack);

            progress.Quests.TryAccept(QuestCatalog.FirstExtractId, out _);
            progress.Quests.ReportExtraction(0, tookDamage: true);
            progress.Quests.TryClaim(QuestCatalog.FirstExtractId, out _);
            progress.Quests.TryAccept(QuestCatalog.ScavengerId, out _);
            progress.Quests.NotifyKill();
            progress.Quests.NotifyKill();
            return progress;
        }

        private static int CountStash(MetaProgress progress, string itemId)
        {
            var total = 0;
            var items = progress.Stash.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Definition.Id == itemId)
                {
                    total += items[i].StackCount;
                }
            }

            return total;
        }
    }
}
