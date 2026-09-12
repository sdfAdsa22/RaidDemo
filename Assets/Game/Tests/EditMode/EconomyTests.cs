using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 商人经济测试：买卖的金额、物品与失败路径。
    /// </summary>
    /// <remarks>
    /// 经济系统最怕的不是数值不好，而是"失败时改了钱却没给物品"这类半完成状态。
    /// 因此每个用例都同时断言余额与仓库，而不是只看操作返回值。
    /// </remarks>
    [TestFixture]
    public sealed class EconomyTests
    {
        private ItemFactory m_Factory;
        private MetaProgress m_Progress;
        private ContainerRegistry m_Registry;
        private TestItemLookup m_Catalog;
        private TraderCatalog m_Trader;
        private EventBus m_EventBus;
        private int m_StashId;

        private TestItemDefinition m_Ammo;
        private TestItemDefinition m_Bolt;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Progress = new MetaProgress(10000);
            m_Registry = new ContainerRegistry();
            m_StashId = m_Registry.Register(m_Progress.Stash, ContainerKind.Stash);
            m_EventBus = new EventBus();

            m_Ammo = new TestItemDefinition(
                "ammo.test", ItemCategory.Ammo, baseValue: 25, maxStack: 90, canRotate: false);
            m_Bolt = new TestItemDefinition(
                "loot.bolt", ItemCategory.Loot, baseValue: 600, maxStack: 20);
            m_Catalog = new TestItemLookup().Add(m_Ammo).Add(m_Bolt);
            m_Trader = new TraderCatalog(new[]
            {
                new TraderStockEntry(m_Ammo.Id, 30, 1),
            });
        }

        [Test]
        public void 购买成功时扣钱并把物品放进仓库()
        {
            var handler = new BuyItemCommandHandler(m_Progress, m_Catalog, m_Trader, m_EventBus);

            var result = handler.Execute(new BuyItemIntent(0, m_Ammo.Id, 30));

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(10000 - (25 * 30), m_Progress.Money, "应按单价 × 数量扣款。");
            Assert.AreEqual(1, m_Progress.Stash.Items.Count);
            Assert.AreEqual(30, m_Progress.Stash.Items[0].StackCount);
        }

        [Test]
        public void 余额不足时不扣钱也不给物品()
        {
            var poor = new MetaProgress(100);
            var handler = new BuyItemCommandHandler(poor, m_Catalog, m_Trader, m_EventBus);

            var result = handler.Execute(new BuyItemIntent(0, m_Ammo.Id, 30));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(CommandCodes.MetaInsufficientFunds, result.Code);
            Assert.AreEqual(100, poor.Money);
            Assert.AreEqual(0, poor.Stash.Items.Count);
        }

        [Test]
        public void 仓库放不下时不扣钱()
        {
            var packed = new MetaProgress(10000);
            var filler = new TestItemDefinition("loot.filler", ItemCategory.Loot, maxStack: 1);
            for (var y = 0; y < MetaProgress.StashHeight; y++)
            {
                for (var x = 0; x < MetaProgress.StashWidth; x++)
                {
                    packed.Stash.AutoPlace(m_Factory.Create(filler));
                }
            }

            var handler = new BuyItemCommandHandler(packed, m_Catalog, m_Trader, m_EventBus);
            var result = handler.Execute(new BuyItemIntent(0, m_Ammo.Id, 30));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(CommandCodes.InventoryFull, result.Code);
            Assert.AreEqual(10000, packed.Money, "购买失败不能扣钱。");
        }

        [Test]
        public void 出售仓库物品按收购价结算()
        {
            var bolt = m_Factory.Create(m_Bolt, 5);
            m_Progress.Stash.AutoPlace(bolt);
            m_Progress.Stash.TryGetOrigin(bolt, out var origin);
            var handler = new SellItemCommandHandler(m_Progress, m_Registry, m_EventBus);

            var result = handler.Execute(new SellItemIntent(0, m_StashId, origin.X, origin.Y));

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(0, m_Progress.Stash.Items.Count, "卖掉的物品必须从仓库移除。");
            Assert.AreEqual(10000 + TraderPricing.GetSellPrice(m_Bolt, 5), m_Progress.Money);
        }

        [Test]
        public void 不能出售随身背包里的物品()
        {
            var backpackId = m_Registry.Register(m_Progress.Loadout.Backpack, ContainerKind.PlayerBackpack);
            var bolt = m_Factory.Create(m_Bolt, 1);
            m_Progress.Loadout.Backpack.AutoPlace(bolt);
            m_Progress.Loadout.Backpack.TryGetOrigin(bolt, out var origin);
            var handler = new SellItemCommandHandler(m_Progress, m_Registry, m_EventBus);

            var result = handler.Execute(new SellItemIntent(0, backpackId, origin.X, origin.Y));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(10000, m_Progress.Money);
            Assert.AreEqual(1, m_Progress.Loadout.Backpack.Items.Count);
        }

        [Test]
        public void 批量出售一次结算全部物品()
        {
            var bolt = m_Factory.Create(m_Bolt, 5);
            var ammo = m_Factory.Create(m_Ammo, 30);
            m_Progress.Stash.AutoPlace(bolt);
            m_Progress.Stash.AutoPlace(ammo);
            m_Progress.Stash.TryGetOrigin(bolt, out var boltOrigin);
            m_Progress.Stash.TryGetOrigin(ammo, out var ammoOrigin);

            var handler = new SellItemsCommandHandler(m_Progress, m_Registry, m_EventBus);
            var result = handler.Execute(new SellItemsIntent(0, m_StashId, new[]
            {
                new SellItemRef(boltOrigin.X, boltOrigin.Y),
                new SellItemRef(ammoOrigin.X, ammoOrigin.Y),
            }));

            var expected = TraderPricing.GetSellPrice(m_Bolt, 5)
                + TraderPricing.GetSellPrice(m_Ammo, 30);
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(10000 + expected, m_Progress.Money);
            Assert.AreEqual(0, m_Progress.Stash.Items.Count);
        }

        [Test]
        public void 批量出售包含无效格子时不改钱也不扣物()
        {
            var bolt = m_Factory.Create(m_Bolt, 1);
            m_Progress.Stash.AutoPlace(bolt);
            m_Progress.Stash.TryGetOrigin(bolt, out var origin);

            var handler = new SellItemsCommandHandler(m_Progress, m_Registry, m_EventBus);
            var result = handler.Execute(new SellItemsIntent(0, m_StashId, new[]
            {
                new SellItemRef(origin.X, origin.Y),
                new SellItemRef(9, 7),
            }));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(10000, m_Progress.Money, "有无效格子时不能扣钱。");
            Assert.AreEqual(1, m_Progress.Stash.Items.Count, "有无效格子时不能卖出一部分。");
        }

        [Test]
        public void 批量出售重复引用同一格只结算一次()
        {
            var bolt = m_Factory.Create(m_Bolt, 3);
            m_Progress.Stash.AutoPlace(bolt);
            m_Progress.Stash.TryGetOrigin(bolt, out var origin);

            var handler = new SellItemsCommandHandler(m_Progress, m_Registry, m_EventBus);
            var result = handler.Execute(new SellItemsIntent(0, m_StashId, new[]
            {
                new SellItemRef(origin.X, origin.Y),
                new SellItemRef(origin.X, origin.Y),
            }));

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(10000 + TraderPricing.GetSellPrice(m_Bolt, 3), m_Progress.Money);
            Assert.AreEqual(0, m_Progress.Stash.Items.Count);
        }
    }
}
