using System.Collections.Generic;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 商人货架上的一项商品。
    /// </summary>
    /// <remarks>
    /// 只保存"卖什么、一次卖多少、按什么顺序排"，价格由
    /// <see cref="TraderPricing"/> 根据物品基础价值计算。
    /// 这样改物价时不需要同时维护两张价格表。
    /// </remarks>
    public readonly struct TraderStockEntry
    {
        /// <summary>创建商品项。</summary>
        /// <param name="itemId">物品稳定 ID。</param>
        /// <param name="bundleCount">一次购买的数量。弹药按 30 发一组，其余为 1。</param>
        /// <param name="sortOrder">货架排序值，小的在前。</param>
        public TraderStockEntry(string itemId, int bundleCount, int sortOrder)
        {
            ItemId = itemId;
            BundleCount = bundleCount;
            SortOrder = sortOrder;
        }

        /// <summary>物品稳定 ID。</summary>
        public string ItemId { get; }

        /// <summary>一次购买的数量。</summary>
        public int BundleCount { get; }

        /// <summary>货架排序值。</summary>
        public int SortOrder { get; }
    }

    /// <summary>
    /// 商人的固定货架。
    /// </summary>
    /// <remarks>
    /// <para>批次 3 采用固定价格与无限库存：动态价格、库存刷新会显著增加经济系统
    /// 的验证面，而对"让钱有意义"这个目标没有决定性的帮助，因此明确推迟。</para>
    ///
    /// <para>货架只写物品 ID，不直接引用资产。拾荒者模式（不引用场景与资产）
    /// 让本类可以在 EditMode 测试里直接构造与校验。</para>
    /// </remarks>
    public sealed class TraderCatalog
    {
        private readonly List<TraderStockEntry> m_Entries;
        private readonly Dictionary<string, TraderStockEntry> m_Lookup;

        /// <summary>创建货架。</summary>
        public TraderCatalog(IReadOnlyList<TraderStockEntry> entries)
        {
            m_Entries = new List<TraderStockEntry>(entries ?? new TraderStockEntry[0]);
            m_Entries.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

            m_Lookup = new Dictionary<string, TraderStockEntry>(m_Entries.Count);
            for (var i = 0; i < m_Entries.Count; i++)
            {
                var entry = m_Entries[i];
                if (!string.IsNullOrEmpty(entry.ItemId))
                {
                    m_Lookup[entry.ItemId] = entry;
                }
            }
        }

        /// <summary>按货架顺序排列的商品。</summary>
        public IReadOnlyList<TraderStockEntry> Entries
        {
            get { return m_Entries; }
        }

        /// <summary>查询某个物品是否在货架上，并取出它的分组数量。</summary>
        public bool TryGetEntry(string itemId, out TraderStockEntry entry)
        {
            entry = default;
            return !string.IsNullOrEmpty(itemId) && m_Lookup.TryGetValue(itemId, out entry);
        }

        /// <summary>
        /// 生成默认货架。
        /// </summary>
        /// <remarks>
        /// 顺序按"新玩家最可能先买什么"排：先手枪与弹药，再医疗与背包，
        /// 最后才是步枪与高等级护甲。货架上没有战利品小物件——
        /// 那些东西是拿来卖的，不是拿来买的。
        /// </remarks>
        public static TraderCatalog CreateDefault()
        {
            return new TraderCatalog(new[]
            {
                new TraderStockEntry("weapon.pistol.pm", 1, 10),
                new TraderStockEntry("ammo.9x19.standard", 30, 20),
                new TraderStockEntry("medical.bandage.small", 1, 30),
                new TraderStockEntry("backpack.small", 1, 40),
                new TraderStockEntry("weapon.rifle.ak74", 1, 50),
                new TraderStockEntry("ammo.5.45.standard", 30, 60),
                new TraderStockEntry("armor.helmet.steel", 1, 70),
                new TraderStockEntry("medical.kit.field", 1, 80),
                new TraderStockEntry("backpack.raider", 1, 90),
                new TraderStockEntry("armor.vest.plate", 1, 100),
                // M8 批次 2：冲锋枪与霰弹枪按"近战选择"排在步枪前后，
                // 12 号霰弹与它们配套；四级套放在最后——它是最贵的一档，
                // 摆在货架末尾本身就是一句"这是给攒够钱的人准备的"。
                new TraderStockEntry("weapon.smg.uzi", 1, 55),
                new TraderStockEntry("weapon.shotgun.pump", 1, 65),
                new TraderStockEntry("ammo.12ga.buck", 20, 66),
                new TraderStockEntry("armor.helmet.heavy", 1, 110),
                new TraderStockEntry("armor.vest.heavy", 1, 120),
            });
        }
    }
}
