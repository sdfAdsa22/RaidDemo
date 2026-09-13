using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 收集图鉴测试：点亮的去重与排序、扫描持有的物品、存档往返，以及事件驱动的增量点亮。
    /// </summary>
    /// <remarks>
    /// 图鉴的所有分支都是纯数据逻辑，因此全部在 EditMode 里直接断言，
    /// 不需要场景、不需要界面，也不需要真的写盘。
    /// </remarks>
    [TestFixture]
    public sealed class MetaCodexTests
    {
        private ItemFactory m_Factory;
        private TestItemDefinition m_Bolt;
        private TestItemDefinition m_Ammo;
        private TestItemDefinition m_Vest;
        private TestItemLookup m_Catalog;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Bolt = new TestItemDefinition(
                "loot.bolt", ItemCategory.Loot, baseValue: 600, maxStack: 20);
            m_Ammo = new TestItemDefinition(
                "ammo.9x19", ItemCategory.Ammo, baseValue: 8, maxStack: 120, canRotate: false);
            m_Vest = new TestItemDefinition(
                "armor.vest", ItemCategory.BodyArmor, width: 2, height: 3);
            m_Catalog = new TestItemLookup()
                .Add(m_Bolt)
                .Add(m_Ammo)
                .Add(m_Vest);
        }

        [Test]
        public void 点亮去重且写盘顺序稳定()
        {
            var codex = new MetaCodex();

            Assert.IsTrue(codex.Mark("weapon.rifle"));
            Assert.IsFalse(codex.Mark("weapon.rifle"), "同一件物品第二次点亮必须是空操作。");
            Assert.IsFalse(codex.Mark(null), "空 ID 不能被记入图鉴。");
            Assert.IsFalse(codex.Mark(string.Empty));
            codex.Mark("ammo.9x19");
            codex.Mark("armor.vest");

            Assert.AreEqual(3, codex.Count);
            Assert.IsTrue(codex.Contains("weapon.rifle"));

            var sorted = codex.ToSortedArray();
            CollectionAssert.AreEqual(
                new[] { "ammo.9x19", "armor.vest", "weapon.rifle" },
                sorted,
                "写盘顺序必须按 ID 排序，否则每次保存的字节都不一样。");
        }

        [Test]
        public void 存档往返保留图鉴条目()
        {
            var progress = new MetaProgress(0);
            progress.Codex.Mark("loot.bolt");
            progress.Codex.Mark("weapon.rifle");

            var data = MetaSaveMapper.Capture(progress, raidInProgress: false);
            Assert.AreEqual(2, data.discoveredItemIds.Length);

            var restored = MetaSaveMapper.Restore(data, m_Catalog, out var problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(2, restored.Codex.Count);
            Assert.IsTrue(restored.Codex.Contains("loot.bolt"));
            Assert.IsTrue(restored.Codex.Contains("weapon.rifle"));
        }

        [Test]
        public void 旧存档没有图鉴字段时按空图鉴还原()
        {
            var data = new MetaSaveData
            {
                schemaVersion = MetaSaveData.CurrentSchemaVersion,
                money = 100,
                discoveredItemIds = null,
            };

            var restored = MetaSaveMapper.Restore(data, m_Catalog, out var problems);

            Assert.IsEmpty(problems);
            Assert.IsNotNull(restored);
            Assert.AreEqual(0, restored.Codex.Count, "旧存档应按「一条都没点亮」处理，而不是报错。");
        }

        [Test]
        public void 扫描持有物点亮仓库背包与装备槽()
        {
            var progress = new MetaProgress(0);
            progress.Stash.AutoPlace(m_Factory.Create(m_Bolt, 3));
            progress.Loadout.Backpack.AutoPlace(m_Factory.Create(m_Ammo, 30));
            Assert.IsTrue(progress.Loadout.Equipment.Equip(
                m_Factory.Create(m_Vest), EquipmentSlot.Body).Success);

            var changedCount = 0;
            progress.Changed += () => changedCount++;

            var discovered = progress.RefreshCodex();

            Assert.AreEqual(3, discovered);
            Assert.IsTrue(progress.Codex.Contains(m_Bolt.Id));
            Assert.IsTrue(progress.Codex.Contains(m_Ammo.Id));
            Assert.IsTrue(progress.Codex.Contains(m_Vest.Id));
            Assert.AreEqual(1, changedCount, "发现新条目时应广播一次变化事件，供存档与界面刷新。");
        }

        [Test]
        public void 重复扫描是幂等的且不再广播()
        {
            var progress = new MetaProgress(0);
            progress.Stash.AutoPlace(m_Factory.Create(m_Bolt));
            progress.RefreshCodex();

            var changedCount = 0;
            progress.Changed += () => changedCount++;

            Assert.AreEqual(0, progress.RefreshCodex());
            Assert.AreEqual(0, changedCount, "没有新条目时不应广播变化。");
        }

        [Test]
        public void 物品被卖掉或丢弃后图鉴保持点亮()
        {
            var progress = new MetaProgress(0);
            var item = m_Factory.Create(m_Bolt);
            progress.Stash.AutoPlace(item);
            progress.RefreshCodex();

            Assert.IsTrue(progress.Stash.Remove(item).Success);
            progress.RefreshCodex();

            Assert.IsTrue(
                progress.Codex.Contains(m_Bolt.Id),
                "图鉴记录的是「曾经拿到过」，卖掉或阵亡都不应该让条目熄灭。");
        }

        [Test]
        public void 事件驱动只响应玩家名下容器()
        {
            var progress = new MetaProgress(0);
            var registry = new ContainerRegistry();
            var bus = new EventBus();
            var lootContainerId = registry.Register(
                new InventoryGrid(4, 4, "战利品箱"), ContainerKind.Loot);
            var backpackContainerId = registry.Register(
                progress.Loadout.Backpack, ContainerKind.PlayerBackpack);

            var marker = new CodexMarker(progress, registry, bus);
            try
            {
                progress.Loadout.Backpack.AutoPlace(m_Factory.Create(m_Bolt));

                // 箱子（战利品容器）的变化与"我拥有过什么"无关，不应点亮。
                bus.Publish(new InventoryChangedEvent(
                    lootContainerId, InventoryChangeTypes.Place));
                Assert.AreEqual(0, progress.Codex.Count);

                // 背包变化说明有物品进了玩家手里，应立刻点亮。
                bus.Publish(new InventoryChangedEvent(
                    backpackContainerId, InventoryChangeTypes.Place));
                Assert.IsTrue(progress.Codex.Contains(m_Bolt.Id));
            }
            finally
            {
                marker.Dispose();
            }
        }

        [Test]
        public void 释放后不再响应事件()
        {
            var progress = new MetaProgress(0);
            var registry = new ContainerRegistry();
            var bus = new EventBus();
            var backpackContainerId = registry.Register(
                progress.Loadout.Backpack, ContainerKind.PlayerBackpack);

            var marker = new CodexMarker(progress, registry, bus);
            marker.Dispose();
            progress.Loadout.Backpack.AutoPlace(m_Factory.Create(m_Bolt));

            bus.Publish(new InventoryChangedEvent(
                backpackContainerId, InventoryChangeTypes.Place));

            Assert.AreEqual(0, progress.Codex.Count, "释放后的订阅不应再触发扫描。");
        }
    }
}
