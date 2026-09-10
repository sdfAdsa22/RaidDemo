using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 网格容器规则测试，覆盖设计文档第 8 节列出的全部边界用例。
    /// </summary>
    /// <remarks>
    /// <para><b>守恒断言是本套测试的核心思路</b>：无论操作成功还是失败，
    /// 物品数量与总重量都必须守恒。物品凭空消失的 bug 在界面上极难发现，
    /// 但在守恒断言下会立刻暴露。</para>
    /// </remarks>
    [TestFixture]
    public sealed class InventoryGridTests
    {
        private ItemFactory m_Factory;
        private InventoryGrid m_Bag;

        /// <summary>每发 0.012 千克、可堆叠 120 发的测试弹药。</summary>
        private TestItemDefinition m_Ammo;

        /// <summary>3x1 的长条武器，可旋转。</summary>
        private TestItemDefinition m_Rifle;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Bag = new InventoryGrid(4, 4, "测试背包");
            m_Ammo = new TestItemDefinition(
                "ammo.9x19", ItemCategory.Ammo, weightKg: 0.012f, baseValue: 8, maxStack: 120);
            m_Rifle = new TestItemDefinition(
                "weapon.rifle.ak74", ItemCategory.Weapon, width: 3, height: 1, weightKg: 3.4f);
        }

        /// <summary>把容器的物品数量与总重量打包成便于断言的快照。</summary>
        private static string Snapshot(InventoryGrid grid)
        {
            return $"物品数={grid.Items.Count}, 总重={grid.TotalWeightKg:F3}";
        }

        [Test]
        public void Place_SizeBeyondBounds_Fails_AndKeepsGridUnchanged()
        {
            var item = m_Factory.Create(m_Rifle);
            var before = Snapshot(m_Bag);

            var result = m_Bag.Place(item, new GridPoint(2, 0), false);

            Assert.IsFalse(result.Success, "3 格宽的枪放在第 3 列会越界，应当失败。");
            Assert.AreEqual(InventoryFailure.OutOfBounds, result.Failure, "失败原因应为越界。");
            Assert.AreEqual(before, Snapshot(m_Bag), "失败后容器状态必须与操作前完全一致。");
        }

        [Test]
        public void Place_TargetOccupied_Fails()
        {
            var first = m_Factory.Create(m_Rifle);
            var second = m_Factory.Create(m_Rifle);
            m_Bag.Place(first, new GridPoint(0, 0), false);

            var result = m_Bag.Place(second, new GridPoint(0, 0), false);

            Assert.AreEqual(InventoryFailure.Occupied, result.Failure, "目标格已被占用时应失败。");
            Assert.AreEqual(1, m_Bag.Items.Count, "失败后容器里仍然只应有原来那一件物品。");
        }

        [Test]
        public void Place_PartialOverlap_Fails()
        {
            var first = m_Factory.Create(m_Rifle);
            var second = m_Factory.Create(m_Rifle);
            m_Bag.Place(first, new GridPoint(0, 0), false);

            // 向右挪一格，与已有物品重叠两格。部分重叠同样必须被拒绝。
            var result = m_Bag.Place(second, new GridPoint(1, 0), false);

            Assert.AreEqual(InventoryFailure.Occupied, result.Failure, "部分重叠不允许，不能压一半。");
        }

        [Test]
        public void Place_Rotated_MakesItemFit()
        {
            var item = m_Factory.Create(m_Rifle);
            var column = new InventoryGrid(1, 3, "竖排容器");

            var result = column.Place(item, new GridPoint(0, 0), true);

            Assert.IsTrue(result.Success, "3x1 的枪旋转后应为 1x3，刚好放进竖排容器。");
            Assert.IsTrue(item.Rotated, "放置成功后物品应处于已旋转状态。");
            Assert.AreEqual(new GridSize(1, 3), item.OccupiedSize, "旋转后占地尺寸应宽高互换。");
        }

        [Test]
        public void Place_RotatedOnNonRotatableItem_Fails()
        {
            var square = new TestItemDefinition(
                "loot.crate.small", ItemCategory.Loot, width: 2, height: 2, canRotate: false);
            var item = m_Factory.Create(square);

            var result = m_Bag.Place(item, new GridPoint(0, 0), true);

            Assert.AreEqual(InventoryFailure.RotationNotAllowed, result.Failure, "不允许旋转的物品被要求旋转时应失败。");
        }

        [Test]
        public void Rotate_NonRotatableItem_Fails()
        {
            var square = new TestItemDefinition(
                "loot.crate.small", ItemCategory.Loot, width: 2, height: 2, canRotate: false);
            var item = m_Factory.Create(square);
            m_Bag.Place(item, new GridPoint(0, 0), false);

            var result = m_Bag.Rotate(item);

            Assert.AreEqual(InventoryFailure.RotationNotAllowed, result.Failure, "就地旋转同样要受可旋转标记约束。");
        }

        [Test]
        public void Transfer_StackFits_MergesIntoExistingStack()
        {
            var target = new InventoryGrid(2, 2, "目标容器");
            var existing = m_Factory.Create(m_Ammo, 30);
            target.Place(existing, new GridPoint(0, 0), false);
            var incoming = m_Factory.Create(m_Ammo, 50);
            m_Bag.Place(incoming, new GridPoint(0, 0), false);

            var result = m_Bag.Transfer(incoming, target, new GridPoint(0, 0), false);

            Assert.IsTrue(result.Success, "同类可堆叠物品应当合并成功。");
            Assert.AreEqual(80, existing.StackCount, "目标堆应变为 80 发。");
            Assert.AreEqual(0, m_Bag.Items.Count, "整堆并入后，源容器里不应再有这件物品。");
        }

        [Test]
        public void Transfer_StackOverflows_LeavesRemainderInSource()
        {
            var target = new InventoryGrid(2, 2, "目标容器");
            var existing = m_Factory.Create(m_Ammo, 100);
            target.Place(existing, new GridPoint(0, 0), false);
            var incoming = m_Factory.Create(m_Ammo, 50);
            m_Bag.Place(incoming, new GridPoint(0, 0), false);

            var result = m_Bag.Transfer(incoming, target, new GridPoint(0, 0), false);

            Assert.IsTrue(result.Success, "能合并一部分时操作算成功。");
            Assert.AreEqual(20, result.MovedCount, "只剩 20 发空间，实际应并入 20 发。");
            Assert.AreEqual(120, existing.StackCount, "目标堆应被填满到 120。");
            Assert.AreEqual(30, incoming.StackCount, "并剩下的 30 发应留在原处，不能凭空消失。");
        }

        [Test]
        public void Transfer_WithinSameGrid_IgnoresOwnCells()
        {
            var item = m_Factory.Create(m_Rifle);
            m_Bag.Place(item, new GridPoint(0, 0), false);

            // 向右挪一格：目标区域与原区域重叠两格，但那是物品自己占的格子。
            var result = m_Bag.Transfer(item, m_Bag, new GridPoint(1, 0), false);

            Assert.IsTrue(result.Success, "同容器内挪动时，物品自己占的格子不应被判为冲突。");
            Assert.IsTrue(m_Bag.TryGetOrigin(item, out var origin), "移动后物品应当仍在容器内。");
            Assert.AreEqual(new GridPoint(1, 0), origin, "物品应位于新的坐标上。");
        }

        [Test]
        public void Split_InvalidCount_Fails()
        {
            var stack = m_Factory.Create(m_Ammo, 30);
            m_Bag.Place(stack, new GridPoint(0, 0), false);

            var zero = m_Bag.Split(stack, 0);
            var tooMany = m_Bag.Split(stack, 30);

            Assert.AreEqual(InventoryFailure.InvalidQuantity, zero.Failure, "拆出 0 个应失败。");
            Assert.AreEqual(InventoryFailure.InvalidQuantity, tooMany.Failure, "拆出全部数量应失败。");
            Assert.AreEqual(30, stack.StackCount, "失败后源堆数量必须保持不变。");
        }

        [Test]
        public void Split_ValidCount_PlacesPieceInFreeCell()
        {
            var stack = m_Factory.Create(m_Ammo, 30);
            m_Bag.Place(stack, new GridPoint(0, 0), false);

            var result = m_Bag.Split(stack, 12);

            Assert.IsTrue(result.Success, "背包有空位时拆分应当成功。");
            Assert.AreEqual(2, m_Bag.Items.Count, "拆分后容器里应有两件物品。");
            Assert.AreEqual(18, stack.StackCount, "原堆应剩 18 发。");
            Assert.AreEqual(12, m_Bag.GetAt(new GridPoint(1, 0)).StackCount, "拆分出的 12 发应放在相邻空格上。");
        }

        [Test]
        public void QuickTransfer_WhenTargetFull_FailsAtomically()
        {
            var target = new InventoryGrid(1, 1, "一格容器");
            var blocker = m_Factory.Create(m_Rifle);
            m_Bag.Place(blocker, new GridPoint(0, 0), false);
            var item = m_Factory.Create(m_Rifle);
            m_Bag.Place(item, new GridPoint(0, 1), false);
            target.AutoPlace(m_Factory.Create(m_Ammo, 1));
            var before = Snapshot(m_Bag);

            var result = m_Bag.QuickTransfer(item, target);

            Assert.AreEqual(InventoryFailure.Full, result.Failure, "目标容器放不下时应报告空间不足。");
            Assert.AreEqual(before, Snapshot(m_Bag), "失败时源容器必须完全不变，不能出现搬了一半的状态。");
            Assert.IsTrue(m_Bag.Contains(item), "失败后物品必须还在源容器里。");
        }

        [Test]
        public void QuickTransfer_PrefersMergeOverEmptySlot()
        {
            var target = new InventoryGrid(3, 3, "目标容器");
            var existing = m_Factory.Create(m_Ammo, 10);
            target.Place(existing, new GridPoint(1, 1), false);
            var incoming = m_Factory.Create(m_Ammo, 5);
            m_Bag.Place(incoming, new GridPoint(0, 0), false);

            m_Bag.QuickTransfer(incoming, target);

            Assert.AreEqual(15, existing.StackCount, "快速转移应优先并入已有的同类堆。");
            Assert.AreEqual(1, target.Items.Count, "并入之后不应再占用额外格子。");
        }

        [Test]
        public void Place_ContainerIntoItself_Fails()
        {
            var bagItem = m_Factory.Create(new TestItemDefinition("backpack.small", ItemCategory.Backpack));
            var inner = new InventoryGrid(4, 4, "背包内部", depth: 2, hostItem: bagItem);

            var result = inner.Place(bagItem, new GridPoint(0, 0), false);

            Assert.AreEqual(InventoryFailure.NestingTooDeep, result.Failure, "容器不能放进它自己的内部，否则会形成环。");
        }

        [Test]
        public void Place_ContainerBeyondMaxDepth_Fails()
        {
            var deep = new InventoryGrid(4, 4, "深层容器", depth: ContainerRules.MaxNestingDepth);
            var bagItem = m_Factory.Create(new TestItemDefinition("backpack.small", ItemCategory.Backpack));

            var result = deep.Place(bagItem, new GridPoint(0, 0), false);

            Assert.AreEqual(InventoryFailure.NestingTooDeep, result.Failure, "嵌套超过上限的容器应被拒绝。");
        }

        [Test]
        public void Sort_KeepsItemCountAndWeight()
        {
            var rifle = m_Factory.Create(m_Rifle);
            m_Bag.Place(rifle, new GridPoint(0, 2), false);
            m_Bag.Place(m_Factory.Create(m_Ammo, 60), new GridPoint(3, 3), false);
            m_Bag.Place(m_Factory.Create(m_Ammo, 30), new GridPoint(0, 0), false);
            var weightBefore = m_Bag.TotalWeightKg;
            var countBefore = m_Bag.Items.Count;

            m_Bag.Sort();

            Assert.AreEqual(countBefore, m_Bag.Items.Count, "整理后物品数量必须守恒。");
            Assert.AreEqual(weightBefore, m_Bag.TotalWeightKg, 1e-4f, "整理后总重量必须守恒。");
            Assert.IsTrue(m_Bag.TryGetOrigin(rifle, out var rifleOrigin), "整理后步枪应当仍在容器内。");
            Assert.AreEqual(new GridPoint(0, 0), rifleOrigin,
                "按面积降序整理时，占 3 格的步枪应先落位到左上角。");
        }

        [Test]
        public void Equip_WithMismatchedCategory_Fails()
        {
            var loadout = new EquipmentLoadout();
            var ammo = m_Factory.Create(m_Ammo, 30);

            var result = loadout.Equip(ammo, EquipmentSlot.PrimaryWeapon);

            Assert.AreEqual(InventoryFailure.SlotTypeMismatch, result.Failure, "弹药不能装进主武器槽。");
            Assert.IsNull(loadout.Get(EquipmentSlot.PrimaryWeapon), "失败后槽位必须保持为空。");
        }

        [Test]
        public void Equip_ReplacingItem_ReturnsOldItemToBackpack()
        {
            var loadout = new EquipmentLoadout();
            var oldRifle = m_Factory.Create(m_Rifle);
            var newRifle = m_Factory.Create(m_Rifle);
            loadout.Equip(oldRifle, EquipmentSlot.PrimaryWeapon);

            var result = loadout.Equip(newRifle, EquipmentSlot.PrimaryWeapon, m_Bag);

            Assert.IsTrue(result.Success, "背包放得下时换装应当成功。");
            Assert.AreSame(newRifle, loadout.Get(EquipmentSlot.PrimaryWeapon), "槽位上应是新武器。");
            Assert.IsTrue(m_Bag.Contains(oldRifle), "换下来的旧武器必须回到背包，不能消失。");
        }

        [Test]
        public void Equip_WhenBackpackCannotHoldOldItem_FailsEntirely()
        {
            var loadout = new EquipmentLoadout();
            var oldRifle = m_Factory.Create(m_Rifle);
            var newRifle = m_Factory.Create(m_Rifle);
            loadout.Equip(oldRifle, EquipmentSlot.PrimaryWeapon);
            var tiny = new InventoryGrid(1, 1, "一格容器");
            tiny.AutoPlace(m_Factory.Create(m_Ammo, 1));

            var result = loadout.Equip(newRifle, EquipmentSlot.PrimaryWeapon, tiny);

            Assert.IsFalse(result.Success, "旧武器没有去处时，整个换装操作应当失败。");
            Assert.AreSame(oldRifle, loadout.Get(EquipmentSlot.PrimaryWeapon), "失败后槽位应保持原样。");
        }

        [Test]
        public void PlayerLoadout_TotalWeight_IncludesEquipment()
        {
            var loadout = new EquipmentLoadout();
            var bag = new InventoryGrid(4, 4, "主背包");
            var player = new PlayerLoadout(bag, loadout);
            bag.Place(m_Factory.Create(m_Ammo, 100), new GridPoint(0, 0), false);
            loadout.Equip(m_Factory.Create(m_Rifle), EquipmentSlot.PrimaryWeapon);

            Assert.AreEqual(1.2f + 3.4f, player.TotalWeightKg, 1e-3f,
                "总负重必须同时包含背包里的东西与身上装备的枪。");
        }
    }
}
