using System;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 掉落表里的一条：抽中时给哪个物品、给多少、权重多大。
    /// </summary>
    /// <remarks>
    /// <para>权重是相对值而不是百分比，调整时只改一个数字即可，
    /// 不必把整张表重新配平到 100。</para>
    ///
    /// <para>非法参数直接抛异常：掉落表是开发期数据，配错应该在启动时就暴露，
    /// 而不是在玩家开箱的瞬间静默地少给一件东西。</para>
    /// </remarks>
    public sealed class LootTableEntry
    {
        /// <summary>创建一条掉落配置。</summary>
        /// <param name="itemId">物品 ID，必须与物品目录中的一致。</param>
        /// <param name="weight">权重，必须大于 0。</param>
        /// <param name="minCount">抽中时的最小数量。</param>
        /// <param name="maxCount">抽中时的最大数量。</param>
        public LootTableEntry(string itemId, int weight, int minCount = 1, int maxCount = 1)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                throw new ArgumentException("掉落条目的物品 ID 不能为空。", nameof(itemId));
            }

            if (weight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(weight),
                    weight,
                    $"物品 {itemId} 的权重必须大于 0，否则永远不会被抽中。");
            }

            if (minCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minCount),
                    minCount,
                    $"物品 {itemId} 的最小数量必须大于 0。");
            }

            if (maxCount < minCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxCount),
                    maxCount,
                    $"物品 {itemId} 的最大数量不能小于最小数量。");
            }

            ItemId = itemId;
            Weight = weight;
            MinCount = minCount;
            MaxCount = maxCount;
        }

        /// <summary>物品 ID。</summary>
        public string ItemId { get; }

        /// <summary>权重。</summary>
        public int Weight { get; }

        /// <summary>最小数量。</summary>
        public int MinCount { get; }

        /// <summary>最大数量。</summary>
        public int MaxCount { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{ItemId} 权重 {Weight} 数量 {MinCount}~{MaxCount}";
        }
    }
}
