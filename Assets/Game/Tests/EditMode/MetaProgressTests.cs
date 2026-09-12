using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Meta;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 局外结算测试：撤离入库与阵亡丢弃。
    /// </summary>
    /// <remarks>
    /// 这两条是 M6 的核心规则（已确认）：撤离成功全部带出进仓库；
    /// 阵亡与超时则随身携带物全部丢失，而**仓库绝对安全**。
    /// 规则本身是纯逻辑，因此可以脱离场景断言——这正是把它写成纯 C# 的意义。
    /// </remarks>
    [TestFixture]
    public sealed class MetaProgressTests
    {
        private ItemFactory m_Factory;
        private MetaProgress m_Progress;
        private TestItemDefinition m_Rifle;
        private TestItemDefinition m_Vest;
        private TestItemDefinition m_Bolt;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Progress = new MetaProgress();
            m_Rifle = new TestItemDefinition("weapon.rifle", ItemCategory.Weapon, width: 3, height: 1);
            m_Vest = new TestItemDefinition("armor.vest", ItemCategory.BodyArmor, width: 2, height: 3);
            m_Bolt = new TestItemDefinition("loot.bolt", ItemCategory.Loot, baseValue: 600, maxStack: 20);
        }

        [Test]
        public void 撤离成功时随身物品全部入库()
        {
            var loadout = m_Progress.Loadout;
            loadout.Equipment.Equip(m_Factory.Create(m_Rifle), EquipmentSlot.PrimaryWeapon);
            loadout.Equipment.Equip(m_Factory.Create(m_Vest), EquipmentSlot.Body);
            loadout.Backpack.AutoPlace(m_Factory.Create(m_Bolt, 5));

            var moved = m_Progress.DepositLoadoutToStash();

            Assert.AreEqual(3, moved, "武器、护甲、背包里的一堆物品都应入库。");
            Assert.AreEqual(3, m_Progress.Stash.Items.Count);
            Assert.AreEqual(0, loadout.Backpack.Items.Count, "入库后随身背包应为空。");
            Assert.IsNull(loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon));
            Assert.IsNull(loadout.Equipment.Get(EquipmentSlot.Body));
            Assert.AreEqual(0, m_Progress.LastDepositFailures);
        }

        [Test]
        public void 阵亡丢弃随身物品但不影响仓库()
        {
            // 先往仓库里放一件「家底」
            m_Progress.Stash.AutoPlace(m_Factory.Create(m_Bolt, 3));

            var loadout = m_Progress.Loadout;
            loadout.Equipment.Equip(m_Factory.Create(m_Rifle), EquipmentSlot.PrimaryWeapon);
            loadout.Backpack.AutoPlace(m_Factory.Create(m_Vest));

            m_Progress.ClearLoadout();

            Assert.AreEqual(0, loadout.Backpack.Items.Count, "阵亡后随身背包必须清空。");
            Assert.IsNull(loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon));
            Assert.AreEqual(1, m_Progress.Stash.Items.Count, "仓库是绝对安全区，不受阵亡影响。");
        }

        [Test]
        public void 仓库放不下时如实计数而不是静默消失()
        {
            // 用小仓库把每一格塞满，再尝试入库一件大件
            var packed = new MetaProgress();
            // 填充物必须**不可堆叠**，否则 80 件只会叠成几堆，仓库根本没满。
            var filler = new TestItemDefinition("loot.filler", ItemCategory.Loot, maxStack: 1);
            for (var y = 0; y < MetaProgress.StashHeight; y++)
            {
                for (var x = 0; x < MetaProgress.StashWidth; x++)
                {
                    packed.Stash.AutoPlace(m_Factory.Create(filler, 1));
                }
            }

            packed.Loadout.Equipment.Equip(m_Factory.Create(m_Rifle), EquipmentSlot.PrimaryWeapon);
            var moved = packed.DepositLoadoutToStash();

            Assert.AreEqual(0, moved, "仓库已满时不应有物品入库。");
            Assert.AreEqual(1, packed.LastDepositFailures, "放不下的件数必须被如实记录。");
        }
    }
}
