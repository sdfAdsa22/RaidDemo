using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 一局战局的结算数据。
    /// </summary>
    /// <remarks>
    /// <para><b>它要回答一个问题：这一局我贪对了还是贪错了。</b>
    /// 因此核心字段是「带入价值」与「带出价值」的对比，而不是物品清单本身。</para>
    ///
    /// <para><b>关于阵亡损失：</b>M5 阶段没有仓库，丢掉的东西没有地方真正「丢」。
    /// 当前做法是如实列出损失的物品与价值，让玩家理解规则；
    /// 等 M6 的仓库落地后，这份数据会直接接上去，本类不需要改动。</para>
    ///
    /// <para>纯 C# 实现：结算的规则要能在测试里断言，而不是只能靠肉眼看界面。</para>
    /// </remarks>
    public sealed class RaidResult
    {
        /// <summary>装备槽的遍历顺序。写成固定数组以保证结算清单的顺序稳定。</summary>
        private static readonly EquipmentSlot[] s_EquipmentSlots =
        {
            EquipmentSlot.PrimaryWeapon,
            EquipmentSlot.SecondaryWeapon,
            EquipmentSlot.Head,
            EquipmentSlot.Body,
            EquipmentSlot.Backpack,
        };

        private readonly List<RaidResultEntry> m_ExtractedItems;
        private readonly List<RaidResultEntry> m_LostItems;

        private RaidResult(
            RaidOutcome outcome,
            int kills,
            float elapsedSeconds,
            int broughtInValue,
            List<RaidResultEntry> extractedItems,
            List<RaidResultEntry> lostItems)
        {
            Outcome = outcome;
            Kills = kills;
            ElapsedSeconds = elapsedSeconds;
            BroughtInValue = broughtInValue;
            m_ExtractedItems = extractedItems;
            m_LostItems = lostItems;
            ExtractedValue = Sum(m_ExtractedItems);
            LostValue = Sum(m_LostItems);
        }

        /// <summary>结局。</summary>
        public RaidOutcome Outcome { get; }

        /// <summary>击杀数。</summary>
        public int Kills { get; }

        /// <summary>存活时长（秒）。</summary>
        public float ElapsedSeconds { get; }

        /// <summary>带入装备的总价值。</summary>
        public int BroughtInValue { get; }

        /// <summary>带出的物品清单。阵亡或超时时为空。</summary>
        public IReadOnlyList<RaidResultEntry> ExtractedItems
        {
            get { return m_ExtractedItems; }
        }

        /// <summary>带出物品的总价值。</summary>
        public int ExtractedValue { get; }

        /// <summary>损失的物品清单。撤离成功时为空。</summary>
        public IReadOnlyList<RaidResultEntry> LostItems
        {
            get { return m_LostItems; }
        }

        /// <summary>损失物品的总价值。</summary>
        public int LostValue { get; }

        /// <summary>
        /// 生成一份结算数据。
        /// </summary>
        /// <param name="outcome">结局。</param>
        /// <param name="kills">击杀数。</param>
        /// <param name="elapsedSeconds">存活时长（秒）。</param>
        /// <param name="broughtInValue">带入价值。</param>
        /// <param name="loadout">玩家当前的携带物。为 null 时清单为空。</param>
        /// <returns>结算数据。</returns>
        /// <remarks>
        /// 撤离成功时携带物算「带出」，阵亡与超时算「损失」。
        /// 两者互斥而不是同时列出：结算界面要给的是一句明确结论，不是一本流水账。
        /// </remarks>
        public static RaidResult Create(
            RaidOutcome outcome,
            int kills,
            float elapsedSeconds,
            int broughtInValue,
            PlayerLoadout loadout)
        {
            var carried = CollectCarriedItems(loadout);
            var extracted = outcome == RaidOutcome.Extracted
                ? carried
                : new List<RaidResultEntry>(0);
            var lost = outcome == RaidOutcome.Extracted
                ? new List<RaidResultEntry>(0)
                : carried;

            return new RaidResult(outcome, kills, elapsedSeconds, broughtInValue, extracted, lost);
        }

        /// <summary>
        /// 统计当前携带物（背包 + 弹药挂 + 全部已装备物品）的总价值。
        /// </summary>
        /// <param name="loadout">玩家携带物。</param>
        /// <returns>总价值。</returns>
        /// <remarks>
        /// 装配层在开战前用它记录「带入价值」。必须与结算走同一套遍历口径，
        /// 否则会出现「带入 8000、原地撤离却带出 8500」这种对不上的数字。
        /// </remarks>
        public static int ComputeCarriedValue(PlayerLoadout loadout)
        {
            return Sum(CollectCarriedItems(loadout));
        }

        /// <summary>把携带物汇总成结算清单。</summary>
        /// <remarks>
        /// 同名物品合并成一行：玩家关心拿到了多少发子弹，不关心哪一发来自哪个箱子。
        /// 用物品 ID 而不是显示名做合并键——显示名可能被改动或重复，ID 才是稳定标识。
        /// </remarks>
        private static List<RaidResultEntry> CollectCarriedItems(PlayerLoadout loadout)
        {
            var entries = new List<RaidResultEntry>(16);
            if (loadout == null)
            {
                return entries;
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            AddGrid(loadout.Backpack, entries, counts, indexById);
            AddGrid(loadout.AmmoPouch, entries, counts, indexById);

            for (var i = 0; i < s_EquipmentSlots.Length; i++)
            {
                AddItem(loadout.Equipment?.Get(s_EquipmentSlots[i]), entries, counts, indexById);
            }

            return entries;
        }

        /// <summary>把一个网格里的物品加入清单。</summary>
        private static void AddGrid(
            InventoryGrid grid,
            List<RaidResultEntry> entries,
            Dictionary<string, int> counts,
            Dictionary<string, int> indexById)
        {
            if (grid == null)
            {
                return;
            }

            var items = grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                AddItem(items[i], entries, counts, indexById);
            }
        }

        /// <summary>把一件物品加入清单，与已有同 ID 的行合并数量。</summary>
        private static void AddItem(
            ItemInstance item,
            List<RaidResultEntry> entries,
            Dictionary<string, int> counts,
            Dictionary<string, int> indexById)
        {
            if (item == null || item.Definition == null)
            {
                return;
            }

            var id = item.Definition.Id;
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (indexById.TryGetValue(id, out var index))
            {
                counts[id] += item.StackCount;
                var merged = counts[id];
                var existing = entries[index];
                entries[index] = new RaidResultEntry(
                    existing.DisplayName,
                    merged,
                    existing.UnitValue,
                    existing.Rarity);
                return;
            }

            indexById[id] = entries.Count;
            counts[id] = item.StackCount;
            entries.Add(new RaidResultEntry(
                item.Definition.DisplayName,
                item.StackCount,
                item.Definition.BaseValue,
                item.Definition.Rarity));
        }

        /// <summary>累加一行行的合计价值。</summary>
        private static int Sum(List<RaidResultEntry> entries)
        {
            var total = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                total += entries[i].TotalValue;
            }

            return total;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"RaidResult({Outcome}, 击杀 {Kills}, 带入 {BroughtInValue}, 带出 {ExtractedValue}, 损失 {LostValue})";
        }
    }
}
