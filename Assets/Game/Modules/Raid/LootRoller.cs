using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 按掉落表往容器网格里生成物品。
    /// </summary>
    /// <remarks>
    /// <para><b>随机源由外部注入，不在内部 new。</b>同一个种子必然产出同一批物资，
    /// 让「某一局为什么开出这些东西」可以被复现；联机时也只需服务端下发种子。</para>
    ///
    /// <para>放不下的物品直接丢弃而不是抛异常：容器被塞满是正常情况，
    /// 而异常会让整局战局在开箱的瞬间崩掉。</para>
    /// </remarks>
    public sealed class LootRoller
    {
        private readonly ItemCatalog m_Catalog;
        private readonly IRandomProvider m_Random;
        private readonly ItemFactory m_Factory;

        /// <summary>创建抽取器。</summary>
        /// <param name="catalog">物品目录，可为 null（此时抽不出任何东西）。</param>
        /// <param name="random">随机源。</param>
        /// <param name="factory">物品实例工厂。</param>
        public LootRoller(ItemCatalog catalog, IRandomProvider random, ItemFactory factory)
        {
            m_Catalog = catalog;
            m_Random = random;
            m_Factory = factory;
        }

        /// <summary>
        /// 按表抽取并尽量放入目标网格。
        /// </summary>
        /// <param name="table">掉落表，为 null 时不做任何事。</param>
        /// <param name="target">目标网格，为 null 时不做任何事。</param>
        /// <returns>成功放入的件数。</returns>
        public int Roll(LootTable table, InventoryGrid target)
        {
            if (table == null || target == null || m_Catalog == null || m_Random == null)
            {
                return 0;
            }

            var weights = table.BuildWeightArray();
            var placed = 0;
            for (var roll = 0; roll < table.RollCount; roll++)
            {
                var index = m_Random.NextWeightedIndex(weights);
                if (index < 0 || index >= table.Entries.Count)
                {
                    continue;
                }

                var entry = table.Entries[index];
                var definition = m_Catalog.Get(entry.ItemId);
                if (definition == null)
                {
                    // 表里引用了目录中不存在的 ID：跳过而不是中断。
                    // 一个物品配错不应该让整箱物资都消失。
                    continue;
                }

                var count = m_Random.NextInt(entry.MinCount, entry.MaxCount + 1);
                var item = m_Factory.Create(definition, count);
                if (PlaceOrStack(target, item))
                {
                    placed++;
                }
            }

            return placed;
        }

        /// <summary>
        /// 先尝试堆叠到已有同类物品上，再尝试占用新格子。
        /// </summary>
        /// <remarks>
        /// 子弹是最常见的掉落，箱子里往往已经有一堆同型号弹药；
        /// 不做堆叠直接丢的话，一箱三十发的空间会白白浪费。
        /// </remarks>
        private static bool PlaceOrStack(InventoryGrid target, ItemInstance item)
        {
            var items = target.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var existing = items[i];
                if (!existing.CanStackWith(item) || existing.RemainingStackRoom < item.StackCount)
                {
                    continue;
                }

                // 只在能整堆放进去时才合并：部分合并会让剩下的部分需要「减量」，
                // 而物品实例没有减量接口，强行实现就会写出重复计数这种隐蔽错误。
                existing.AddToStack(item.StackCount);
                return true;
            }

            return target.AutoPlace(item).Success;
        }
    }
}
