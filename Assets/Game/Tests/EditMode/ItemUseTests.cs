using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Raid;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 医疗品使用测试：读条、受伤打断、完成后才算用掉。
    /// </summary>
    /// <remarks>
    /// 「受伤打断且不消耗」是负责人明确要求的规则，因此这里既断言事件，
    /// 也断言物品数量没有变化——只测事件会漏掉「扣了东西又没回血」这种实现错误。
    /// </remarks>
    [TestFixture]
    public sealed class ItemUseTests
    {
        private EventBus m_Bus;
        private ItemUseInteraction m_Use;
        private InventoryGrid m_Bag;
        private ItemInstance m_Bandage;

        [SetUp]
        public void SetUp()
        {
            m_Bus = new EventBus();
            m_Use = new ItemUseInteraction(m_Bus);
            m_Bag = new InventoryGrid(3, 3, "测试背包");
            var factory = new ItemFactory();
            var definition = new TestItemDefinition(
                "medical.bandage", ItemCategory.Medical, maxStack: 3);
            m_Bandage = factory.Create(definition, 2);
            m_Bag.AutoPlace(m_Bandage);
        }

        [Test]
        public void 读条未满时不会完成()
        {
            Assert.IsTrue(m_Use.TryBegin(m_Bandage, "小绷带", 1.5f));
            m_Use.Tick(1f, playerAlive: true, tookDamage: false);

            Assert.AreEqual(0.66f, m_Use.Progress01, 0.02f);
            Assert.IsTrue(m_Use.IsUsing);
            Assert.AreEqual(2, m_Bandage.StackCount, "读条没走完就不该扣物品。");
        }

        [Test]
        public void 受伤打断且不消耗物品()
        {
            var canceled = string.Empty;
            using (m_Bus.Subscribe<ItemUseCanceledEvent>(evt => canceled = evt.Reason))
            {
                m_Use.TryBegin(m_Bandage, "小绷带", 1.5f);
                m_Use.Tick(0.5f, playerAlive: true, tookDamage: false);
                m_Use.Tick(0.1f, playerAlive: true, tookDamage: true);
            }

            Assert.IsFalse(m_Use.IsUsing, "受伤必须打断读条。");
            Assert.AreEqual("受伤", canceled);
            Assert.AreEqual(2, m_Bandage.StackCount, "被打断时物品不能被消耗。");
        }

        [Test]
        public void 读满后完成并清空状态()
        {
            ItemInstance used = null;
            using (m_Bus.Subscribe<ItemUseCompletedEvent>(evt => used = evt.Item))
            {
                m_Use.TryBegin(m_Bandage, "小绷带", 1.5f);
                m_Use.Tick(1.5f, playerAlive: true, tookDamage: false);
            }

            Assert.AreSame(m_Bandage, used);
            Assert.IsFalse(m_Use.IsUsing);
            Assert.IsNull(m_Use.TargetItem, "完成后不应留下目标物品。");
        }
    }
}
