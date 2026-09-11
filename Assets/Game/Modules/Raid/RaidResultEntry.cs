using RaidDemo.Data;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 结算清单里的一行：某种物品带了多少、单价多少、合计多少。
    /// </summary>
    /// <remarks>
    /// <para>同一件物品在结算里合并成一行，而不是每件实例各占一行：
    /// 玩家关心的是「我拿到了多少发 5.45 弹」，不是「哪一发是从哪个箱子拿的」。
    /// 合并发生在生成结算数据的时候，物品实例本身不受影响。</para>
    ///
    /// <para>它是一个只读的数据载体，不含任何计算：所有数值在构造时就固定下来，
    /// 因此界面反复刷新也不会得到不同的结果。</para>
    /// </remarks>
    public sealed class RaidResultEntry
    {
        /// <summary>创建一行结算数据。</summary>
        /// <param name="displayName">显示名。</param>
        /// <param name="count">数量。</param>
        /// <param name="unitValue">单价。</param>
        /// <param name="rarity">稀有度，界面用来上色。</param>
        public RaidResultEntry(string displayName, int count, int unitValue, RarityTier rarity)
        {
            DisplayName = displayName;
            Count = count > 0 ? count : 0;
            UnitValue = unitValue > 0 ? unitValue : 0;
            Rarity = rarity;
            TotalValue = UnitValue * Count;
        }

        /// <summary>显示名。</summary>
        public string DisplayName { get; }

        /// <summary>数量。</summary>
        public int Count { get; }

        /// <summary>单价。</summary>
        public int UnitValue { get; }

        /// <summary>合计价值。</summary>
        public int TotalValue { get; }

        /// <summary>稀有度。</summary>
        public RarityTier Rarity { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{DisplayName} x{Count} = {TotalValue}";
        }
    }
}
