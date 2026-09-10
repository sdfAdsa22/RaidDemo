using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 背包命令链路测试：命令 → 路由 → 规则 → 事件。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="InventoryGridTests"/> 的分工是：那一组测规则本身，
    /// 这一组测"意图能否正确抵达规则，结果能否正确回到调用方"。</para>
    ///
    /// <para>这里刻意使用真实的 <see cref="CommandRouter"/> 与真实的 <see cref="EventBus"/>，
    /// 而不是替身。因为要验证的正是这条链路的接线正确性——
    /// 用替身把链路换掉，测的就不是这条链路了。</para>
    /// </remarks>
    [TestFixture]
    public sealed class InventoryCommandTests
    {
        private ItemFactory m_Factory;
        private EventBus m_Bus;
        private ContainerRegistry m_Registry;
        private CommandRouter m_Router;
        private InventoryGrid m_Backpack;
        private InventoryGrid m_Loot;
        private int m_BackpackId;
        private int m_LootId;
        private TestItemDefinition m_Ammo;
        private TestItemDefinition m_Armor;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Bus = new EventBus();
            m_Registry = new ContainerRegistry();
            m_Router = new CommandRouter();

            m_Backpack = new InventoryGrid(4, 4, "主背包");
            m_Loot = new InventoryGrid(3, 3, "战利品箱");
            m_BackpackId = m_Registry.Register(m_Backpack, ContainerKind.PlayerBackpack);
            m_LootId = m_Registry.Register(m_Loot, ContainerKind.Loot);

            var loadout = new PlayerLoadout(m_Backpack, new EquipmentLoadout());
            var context = new InventoryContext(m_Registry, loadout, m_Bus);
            m_Router.Register<InventoryMoveIntent>(new InventoryMoveCommandHandler(context));
            m_Router.Register<InventoryQuickTransferIntent>(new InventoryQuickTransferCommandHandler(context));
            m_Router.Register<InventoryRotateIntent>(new InventoryRotateCommandHandler(context));
            m_Router.Register<InventorySplitIntent>(new InventorySplitCommandHandler(context));
            m_Router.Register<InventorySortIntent>(new InventorySortCommandHandler(context));
            m_Router.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(context));
            m_Router.Register<InventoryUnequipIntent>(new InventoryUnequipCommandHandler(context));

            m_Ammo = new TestItemDefinition(
                "ammo.9x19", ItemCategory.Ammo, weightKg: 0.012f, baseValue: 8, maxStack: 120);
            m_Armor = new TestItemDefinition(
                "armor.vest.plate", ItemCategory.BodyArmor, width: 2, height: 2, weightKg: 6.8f);
        }

        [Test]
        public void Move_ValidCells_MovesItem()
        {
            var stack = m_Factory.Create(m_Ammo, 30);
            m_Loot.Place(stack, new GridPoint(0, 0), false);

            var result = m_Router.Dispatch(new InventoryMoveIntent(
                0, m_LootId, m_BackpackId, 0, 0, 2, 2));

            Assert.IsTrue(result.Success, "把战利品箱里的弹药移到背包空格应当成功。");
            Assert.AreSame(stack, m_Backpack.GetAt(new GridPoint(2, 2)), "背包目标格上应当是那堆弹药。");
            Assert.IsNull(m_Loot.GetAt(new GridPoint(0, 0)), "战利品箱的源格子应已空出。");
        }

        [Test]
        public void Move_OutOfBounds_ReturnsOutOfBoundsCode()
        {
            m_Loot.Place(m_Factory.Create(m_Ammo, 30), new GridPoint(0, 0), false);

            var result = m_Router.Dispatch(new InventoryMoveIntent(
                0, m_LootId, m_BackpackId, 0, 0, 9, 9));

            Assert.IsFalse(result.Success, "目标越界时命令应当失败。");
            Assert.AreEqual(CommandCodes.InventoryOutOfBounds, result.Code, "越界应回报专门的结果码，便于客户端提示。");
        }

        [Test]
        public void Move_UnknownContainer_ReturnsNotFoundCode()
        {
            var result = m_Router.Dispatch(new InventoryMoveIntent(
                0, 999, m_BackpackId, 0, 0, 0, 0));

            Assert.AreEqual(CommandCodes.InventoryNotFound, result.Code, "容器 ID 不存在时应回报找不到容器。");
        }

        [Test]
        public void Move_EmptySourceCell_ReturnsNotFoundCode()
        {
            var result = m_Router.Dispatch(new InventoryMoveIntent(
                0, m_LootId, m_BackpackId, 1, 1, 0, 0));

            Assert.AreEqual(CommandCodes.InventoryNotFound, result.Code, "源格子上没有物品时也应回报找不到。");
        }

        [Test]
        public void Split_InvalidCount_ReturnsInvalidQuantityCode()
        {
            m_Backpack.Place(m_Factory.Create(m_Ammo, 30), new GridPoint(0, 0), false);

            var result = m_Router.Dispatch(new InventorySplitIntent(0, m_BackpackId, 0, 0, 30));

            Assert.AreEqual(CommandCodes.InventoryInvalidQuantity, result.Code, "拆分数量等于持有量应被拒绝。");
        }

        [Test]
        public void Equip_MismatchedCategory_ReturnsSlotMismatchCode()
        {
            m_Backpack.Place(m_Factory.Create(m_Ammo, 30), new GridPoint(0, 0), false);

            var result = m_Router.Dispatch(new InventoryEquipIntent(
                0, m_BackpackId, 0, 0, EquipmentSlot.PrimaryWeapon));

            Assert.AreEqual(CommandCodes.InventorySlotMismatch, result.Code, "弹药不能装进武器槽。");
        }

        [Test]
        public void Equip_ValidArmor_LeavesBackpackAndFillsSlot()
        {
            var vest = m_Factory.Create(m_Armor);
            m_Backpack.Place(vest, new GridPoint(0, 0), false);

            var result = m_Router.Dispatch(new InventoryEquipIntent(
                0, m_BackpackId, 0, 0, EquipmentSlot.Body));

            Assert.IsTrue(result.Success, "护甲装进护甲槽应当成功。");
            Assert.IsFalse(m_Backpack.Contains(vest), "装备后物品不应继续留在背包里。");
        }

        [Test]
        public void SuccessfulMove_PublishesInventoryChangedEvent()
        {
            m_Loot.Place(m_Factory.Create(m_Ammo, 30), new GridPoint(0, 0), false);
            var received = 0;
            using (m_Bus.Subscribe<InventoryChangedEvent>(_ => received++))
            {
                m_Router.Dispatch(new InventoryMoveIntent(0, m_LootId, m_BackpackId, 0, 0, 0, 0));
            }

            Assert.Greater(received, 0, "成功的背包操作必须广播变更事件，否则界面不会刷新。");
        }

        [Test]
        public void FailedMove_DoesNotPublishEvent()
        {
            var received = 0;
            using (m_Bus.Subscribe<InventoryChangedEvent>(_ => received++))
            {
                m_Router.Dispatch(new InventoryMoveIntent(0, m_LootId, m_BackpackId, 1, 1, 0, 0));
            }

            Assert.AreEqual(0, received, "失败的操作用户什么都没做成，不应触发界面刷新。");
        }

        [Test]
        public void Sort_ThroughCommand_KeepsItemCount()
        {
            m_Backpack.Place(m_Factory.Create(m_Armor), new GridPoint(0, 3), false);
            m_Backpack.Place(m_Factory.Create(m_Ammo, 40), new GridPoint(3, 3), false);
            var before = m_Backpack.Items.Count;

            var result = m_Router.Dispatch(new InventorySortIntent(0, m_BackpackId));

            Assert.IsTrue(result.Success, "整理命令应当总是成功。");
            Assert.AreEqual(before, m_Backpack.Items.Count, "整理后物品数量必须守恒。");
        }

        [Test]
        public void QuickTransfer_ThroughCommand_MovesItem()
        {
            m_Loot.Place(m_Factory.Create(m_Ammo, 25), new GridPoint(0, 0), false);

            var result = m_Router.Dispatch(new InventoryQuickTransferIntent(
                0, m_LootId, m_BackpackId, 0, 0));

            Assert.IsTrue(result.Success, "一键转移应当成功。");
            Assert.AreEqual(0, m_Loot.Items.Count, "转移后源容器应当空了。");
            Assert.AreEqual(1, m_Backpack.Items.Count, "目标容器应当收到这件物品。");
        }
    }
}
