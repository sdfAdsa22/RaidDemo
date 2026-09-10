using System;
using NUnit.Framework;
using RaidDemo.Data;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 物品实例的行为测试：堆叠、拆分、重量与价值换算、旋转。
    /// </summary>
    /// <remarks>
    /// 这些用例全部在 EditMode 下运行，不加载场景、不创建资产。
    /// </remarks>
    [TestFixture]
    public sealed class ItemInstanceTests
    {
        private ItemFactory m_Factory;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
        }

        [Test]
        public void CanStackWith_SameDefinition_ReturnsTrue()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var left = m_Factory.Create(ammo, 30);
            var right = m_Factory.Create(ammo, 60);

            Assert.IsTrue(left.CanStackWith(right), "同一型号且都未满的可堆叠物品应当能合并。");
        }

        [Test]
        public void CanStackWith_DifferentDefinition_ReturnsFalse()
        {
            var nine = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var five = new TestItemDefinition("ammo.5.45", ItemCategory.Ammo, maxStack: 120);
            var left = m_Factory.Create(nine, 30);
            var right = m_Factory.Create(five, 30);

            Assert.IsFalse(left.CanStackWith(right), "不同型号的弹药不能合并。");
        }

        [Test]
        public void CanStackWith_DifferentState_ReturnsFalse()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var left = m_Factory.Create(ammo, 30);
            var right = m_Factory.Create(ammo, 30);
            left.SetState(new TestItemState(1));
            right.SetState(new TestItemState(2));

            Assert.IsFalse(left.CanStackWith(right),
                "状态不同的物品不能合并。这条规则在 M2 恒为真，但必须已经生效，M3 才不用改合并逻辑。");
        }

        [Test]
        public void CanStackWith_EqualState_ReturnsTrue()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var left = m_Factory.Create(ammo, 30);
            var right = m_Factory.Create(ammo, 30);
            left.SetState(new TestItemState(7));
            right.SetState(new TestItemState(7));

            Assert.IsTrue(left.CanStackWith(right), "状态内容相同的物品应当能合并。");
        }

        [Test]
        public void CanStackWith_NonStackableItem_ReturnsFalse()
        {
            var rifle = new TestItemDefinition("weapon.rifle.ak74", ItemCategory.Weapon, width: 3, maxStack: 1);
            var left = m_Factory.Create(rifle);
            var right = m_Factory.Create(rifle);

            Assert.IsFalse(left.CanStackWith(right), "堆叠上限为 1 的物品不能与任何东西合并。");
        }

        [Test]
        public void CanStackWith_Self_ReturnsFalse()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var stack = m_Factory.Create(ammo, 30);

            Assert.IsFalse(stack.CanStackWith(stack), "物品不能与自己合并。");
        }

        [Test]
        public void AddToStack_BeyondLimit_ReturnsActuallyAdded()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var stack = m_Factory.Create(ammo, 100);

            var added = stack.AddToStack(50);

            Assert.AreEqual(20, added, "只剩 20 发空间时，请求加入 50 发应只加入 20。");
            Assert.AreEqual(120, stack.StackCount, "加入后应恰好达到堆叠上限。");
        }

        [Test]
        public void AddToStack_WhenFull_ReturnsZero()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var stack = m_Factory.Create(ammo, 120);

            Assert.AreEqual(0, stack.AddToStack(10), "已满的堆不能再加入任何数量。");
        }

        [Test]
        public void Split_InvalidCount_Throws()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var stack = m_Factory.Create(ammo, 30);

            Assert.Throws<ArgumentOutOfRangeException>(() => stack.Split(0), "拆出 0 个是非法的。");
            Assert.Throws<ArgumentOutOfRangeException>(() => stack.Split(30), "拆出全部数量应当用移动而不是拆分。");
            Assert.Throws<ArgumentOutOfRangeException>(() => stack.Split(31), "拆出超过持有量的数量是非法。");
        }

        [Test]
        public void Split_ValidCount_DividesStack()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);
            var stack = m_Factory.Create(ammo, 30);

            var piece = stack.Split(12);

            Assert.AreEqual(18, stack.StackCount, "拆出 12 个后，原堆应剩 18 个。");
            Assert.AreEqual(12, piece.StackCount, "拆出的新堆应有 12 个。");
            Assert.AreNotEqual(stack.InstanceId, piece.InstanceId, "拆出的新堆必须有独立的实例号。");
        }

        [Test]
        public void Split_DoesNotInheritRotation()
        {
            var rifle = new TestItemDefinition(
                "weapon.rifle.ak74", ItemCategory.Weapon, width: 3, height: 1, maxStack: 5);
            var stack = m_Factory.Create(rifle, 4);
            stack.Rotated = true;

            var piece = stack.Split(2);

            Assert.IsFalse(piece.Rotated,
                "拆分出的新堆不应继承朝向。它摆在哪里由自己的放置过程决定。");
        }

        [Test]
        public void WeightKg_And_TotalValue_ScaleWithCount()
        {
            var ammo = new TestItemDefinition(
                "ammo.9x19", ItemCategory.Ammo, weightKg: 0.012f, baseValue: 8, maxStack: 120);
            var stack = m_Factory.Create(ammo, 100);

            Assert.AreEqual(1.2f, stack.WeightKg, 1e-4f, "100 发、每发 0.012 千克应为 1.2 千克。");
            Assert.AreEqual(800, stack.TotalValue, "100 发、每发 8 块应为 800 块。");
        }

        [Test]
        public void OccupiedSize_SwapsWidthAndHeight_WhenRotated()
        {
            var rifle = new TestItemDefinition(
                "weapon.rifle.ak74", ItemCategory.Weapon, width: 3, height: 1);
            var item = m_Factory.Create(rifle);

            Assert.AreEqual(new GridSize(3, 1), item.OccupiedSize, "未旋转时应为 3x1。");

            item.Rotated = true;
            Assert.AreEqual(new GridSize(1, 3), item.OccupiedSize, "旋转后占地应宽高互换。");
        }

        [Test]
        public void Create_CountAboveMaxStack_IsClamped()
        {
            var ammo = new TestItemDefinition("ammo.9x19", ItemCategory.Ammo, maxStack: 120);

            var stack = m_Factory.Create(ammo, 500);

            Assert.AreEqual(120, stack.StackCount, "超过堆叠上限的创建请求应被截断到上限，而不是凭空多给。");
        }
    }
}
