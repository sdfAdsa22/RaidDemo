using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 局外进度与存档 DTO 之间的转换。
    /// </summary>
    /// <remarks>
    /// <para><b>转换与文件读写分开。</b>本类只做"对象 ↔ DTO"，不接触磁盘；
    /// 文件读写由 Kernel 的通用存档服务负责。因此可以在 EditMode 测试里
    /// 验证往返一致，而不需要真的写文件。</para>
    ///
    /// <para>还原时对任何一条无法识别的记录都采用"跳过并记录"而不是抛异常：
    /// 一个旧物品 ID 不应该让玩家整份仓库都读不出来。</para>
    /// </remarks>
    public static class MetaSaveMapper
    {
        /// <summary>单条物品记录允许的最大数量，防止损坏存档制造超大对象。</summary>
        private const int MaxItemCountPerRecord = 100000;

        /// <summary>单个容器边长上限。损坏存档不应让游戏尝试创建 100 万格网格。</summary>
        private const int MaxGridSide = 32;

        /// <summary>把局外进度整理成存档数据。</summary>
        public static MetaSaveData Capture(MetaProgress progress, bool raidInProgress)
        {
            if (progress == null)
            {
                return null;
            }

            var backpack = progress.Loadout.Backpack;
            return new MetaSaveData
            {
                schemaVersion = MetaSaveData.CurrentSchemaVersion,
                money = progress.Money,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                raidInProgress = raidInProgress,
                trackedQuestId = progress.Quests.TrackedQuestId,
                selectedCharacterId = progress.SelectedCharacterId,
                backpackWidth = backpack != null ? backpack.Width : 0,
                backpackHeight = backpack != null ? backpack.Height : 0,
                stash = CaptureGrid(progress.Stash),
                backpack = CaptureGrid(backpack),
                ammoPouch = CaptureGrid(progress.Loadout.AmmoPouch),
                equipment = CaptureEquipment(progress.Loadout.Equipment),
                quests = progress.Quests.CaptureState(),
                discoveredItemIds = progress.Codex != null
                    ? progress.Codex.ToSortedArray()
                    : new string[0],
            };
        }

        /// <summary>
        /// 从存档数据还原一份全新的局外进度。
        /// </summary>
        /// <param name="data">已反序列化的存档。</param>
        /// <param name="catalog">物品目录，用于把 ID 还原成定义。</param>
        /// <param name="problems">无法还原的条目说明。返回空列表时表示完全还原。</param>
        /// <returns>还原后的进度；存档不可用时返回 null。</returns>
        public static MetaProgress Restore(
            MetaSaveData data,
            IItemDefinitionLookup catalog,
            out List<string> problems)
        {
            problems = new List<string>();
            if (data == null)
            {
                problems.Add("存档内容为空。");
                return null;
            }

            if (data.schemaVersion > MetaSaveData.CurrentSchemaVersion)
            {
                problems.Add(
                    $"存档版本 {data.schemaVersion} 高于当前游戏支持的 "
                    + $"{MetaSaveData.CurrentSchemaVersion}，无法读取。");
                return null;
            }

            if (data.schemaVersion <= 0)
            {
                // 早期版本没有写版本号，按最初的 1 处理。
                data.schemaVersion = 1;
            }

            Migrate(data, problems);

            var progress = new MetaProgress(0);
            progress.AttachCatalog(catalog);
            progress.RestoreMoney(data.money);
            progress.RestoreSelectedCharacter(data.selectedCharacterId);

            var width = ClampGridSide(data.backpackWidth, MetaProgress.PocketWidth);
            var height = ClampGridSide(data.backpackHeight, MetaProgress.PocketHeight);
            progress.Loadout.ReplaceBackpack(new InventoryGrid(width, height, "主背包"));

            RestoreEquipment(progress.Loadout.Equipment, data.equipment, catalog, problems);
            RestoreGrid(progress.Loadout.AmmoPouch, data.ammoPouch, catalog, problems, "弹药挂");
            RestoreGrid(progress.Loadout.Backpack, data.backpack, catalog, problems, "主背包");
            RestoreGrid(progress.Stash, data.stash, catalog, problems, "仓库");
            progress.Quests.RestoreState(data.quests, data.trackedQuestId, problems);
            progress.Codex.Restore(data.discoveredItemIds);
            return progress;
        }

        /// <summary>把旧版本结构补齐到当前版本。</summary>
        private static void Migrate(MetaSaveData data, List<string> problems)
        {
            // 目前只有版本 1。下一次结构调整时在这里按版本逐级转换。
            if (data.schemaVersion < 1)
            {
                data.schemaVersion = 1;
                problems.Add("存档版本过旧，已按版本 1 的规则读取。");
            }
        }

        /// <summary>把一个网格整理成记录数组。</summary>
        private static ItemStackSave[] CaptureGrid(InventoryGrid grid)
        {
            if (grid == null)
            {
                return new ItemStackSave[0];
            }

            var records = new List<ItemStackSave>(grid.Items.Count);
            var items = grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!grid.TryGetOrigin(item, out var origin))
                {
                    continue;
                }

                records.Add(new ItemStackSave
                {
                    itemId = item.Definition.Id,
                    count = item.StackCount,
                    x = origin.X,
                    y = origin.Y,
                    rotated = item.Rotated,
                });
            }

            return records.ToArray();
        }

        /// <summary>把装备槽整理成记录数组。</summary>
        private static EquipmentItemSave[] CaptureEquipment(EquipmentLoadout equipment)
        {
            if (equipment == null)
            {
                return new EquipmentItemSave[0];
            }

            var slots = new[]
            {
                EquipmentSlot.PrimaryWeapon,
                EquipmentSlot.SecondaryWeapon,
                EquipmentSlot.Head,
                EquipmentSlot.Body,
                EquipmentSlot.Backpack,
            };

            var records = new List<EquipmentItemSave>(slots.Length);
            for (var i = 0; i < slots.Length; i++)
            {
                var item = equipment.Get(slots[i]);
                if (item == null)
                {
                    continue;
                }

                records.Add(new EquipmentItemSave
                {
                    slot = (int)slots[i],
                    itemId = item.Definition.Id,
                    count = item.StackCount,
                });
            }

            return records.ToArray();
        }

        /// <summary>还原装备槽。</summary>
        private static void RestoreEquipment(
            EquipmentLoadout equipment,
            IReadOnlyList<EquipmentItemSave> records,
            IItemDefinitionLookup catalog,
            List<string> problems)
        {
            if (equipment == null || records == null)
            {
                return;
            }

            var factory = new ItemFactory();
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null || string.IsNullOrEmpty(record.itemId))
                {
                    continue;
                }

                if (record.slot < 0 || record.slot > (int)EquipmentSlot.Backpack)
                {
                    problems.Add($"装备记录 {record.itemId} 的槽位非法（{record.slot}），已跳过。");
                    continue;
                }

                if (catalog == null
                    || !catalog.TryGet(record.itemId, out var definition))
                {
                    problems.Add($"装备物品 {record.itemId} 在当前版本中不存在，已跳过。");
                    continue;
                }

                var slot = (EquipmentSlot)record.slot;
                if (!EquipmentLoadout.IsCategoryAllowed(definition.Category, slot))
                {
                    problems.Add($"装备物品 {record.itemId} 与槽位 {slot} 不匹配，已跳过。");
                    continue;
                }

                var item = factory.Create(definition, Math.Max(1, record.count));
                var result = equipment.Equip(item, slot);
                if (!result.Success)
                {
                    problems.Add($"装备物品 {record.itemId} 放回槽位失败：{result.Message}");
                }
            }
        }

        /// <summary>还原一个网格里的物品。</summary>
        private static void RestoreGrid(
            InventoryGrid grid,
            IReadOnlyList<ItemStackSave> records,
            IItemDefinitionLookup catalog,
            List<string> problems,
            string gridName)
        {
            if (grid == null || records == null)
            {
                return;
            }

            var factory = new ItemFactory();
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null || string.IsNullOrEmpty(record.itemId) || record.count <= 0)
                {
                    continue;
                }

                if (catalog == null
                    || !catalog.TryGet(record.itemId, out var definition))
                {
                    problems.Add($"{gridName}里的物品 {record.itemId} 在当前版本中不存在，已跳过。");
                    continue;
                }

                if (record.count > MaxItemCountPerRecord)
                {
                    problems.Add(
                        $"{gridName}里的 {record.itemId} 数量异常（{record.count}），已截断到 "
                        + $"{MaxItemCountPerRecord}。");
                }

                var remaining = Math.Min(record.count, MaxItemCountPerRecord);
                var first = true;
                while (remaining > 0)
                {
                    var take = Math.Min(remaining, Math.Max(1, definition.MaxStack));
                    var item = factory.Create(definition, take);
                    var placed = first
                        ? grid.Place(item, new GridPoint(record.x, record.y), record.rotated)
                        : grid.AutoPlace(item);
                    if (!placed.Success && first)
                    {
                        placed = grid.AutoPlace(item);
                    }

                    if (!placed.Success)
                    {
                        problems.Add($"{gridName}空间不足，{record.itemId} 剩余 {remaining} 个未能还原。");
                        break;
                    }

                    remaining -= take;
                    first = false;
                }
            }
        }

        private static int ClampGridSide(int value, int fallback)
        {
            if (value <= 0)
            {
                return fallback;
            }

            return value > MaxGridSide ? MaxGridSide : value;
        }
    }
}
