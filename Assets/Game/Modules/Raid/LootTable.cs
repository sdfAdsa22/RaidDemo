using System.Collections.Generic;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 一张掉落表：抽几次、每次从哪些条目里按权重挑一个。
    /// </summary>
    /// <remarks>
    /// <para><b>抽固定次数，而不是每件判一次概率。</b>固定次数让每个箱子的产出数量稳定，
    /// 而独立概率会同时产生「空箱子」与「一口气给五件」两种极端，
    /// 空箱子对玩家来说只是被浪费时间。</para>
    ///
    /// <para>抽中的条目之间相互独立，同一个物品可能被抽中多次。
    /// 这是刻意的：一箱三十发与一箱两百发子弹的差别本身就是搜刮的乐趣。</para>
    /// </remarks>
    public sealed class LootTable
    {
        private readonly List<LootTableEntry> m_Entries;

        /// <summary>创建掉落表。</summary>
        /// <param name="id">表的稳定标识。</param>
        /// <param name="displayName">显示名，用于调试与文档。</param>
        /// <param name="rollCount">抽取次数，必须大于 0。</param>
        /// <param name="entries">条目列表，不能为空。</param>
        public LootTable(string id, string displayName, int rollCount, IReadOnlyList<LootTableEntry> entries)
        {
            Id = id;
            DisplayName = displayName;
            RollCount = rollCount;
            m_Entries = entries != null
                ? new List<LootTableEntry>(entries)
                : new List<LootTableEntry>(0);
        }

        /// <summary>表的稳定标识。</summary>
        public string Id { get; }

        /// <summary>显示名。</summary>
        public string DisplayName { get; }

        /// <summary>抽取次数。</summary>
        public int RollCount { get; }

        /// <summary>全部条目。</summary>
        public IReadOnlyList<LootTableEntry> Entries
        {
            get { return m_Entries; }
        }

        /// <summary>校验表是否可用。</summary>
        /// <returns>合法返回 null，否则返回中文问题描述。</returns>
        public string Validate()
        {
            if (string.IsNullOrEmpty(Id))
            {
                return "掉落表的 ID 不能为空。";
            }

            if (RollCount <= 0)
            {
                return $"{Id}：抽取次数必须大于 0，当前为 {RollCount}。";
            }

            if (m_Entries.Count == 0)
            {
                return $"{Id}：至少要有一条掉落条目。";
            }

            return null;
        }

        /// <summary>取出按顺序排列的权重数组，供随机源做加权抽取。</summary>
        /// <remarks>
        /// 每次抽取都新建数组会产生垃圾，但一个箱子只抽一次，
        /// 代价可以忽略，换来的是接口简单、不易出错。
        /// </remarks>
        public int[] BuildWeightArray()
        {
            var weights = new int[m_Entries.Count];
            for (var i = 0; i < m_Entries.Count; i++)
            {
                weights[i] = m_Entries[i].Weight;
            }

            return weights;
        }
    }
}
