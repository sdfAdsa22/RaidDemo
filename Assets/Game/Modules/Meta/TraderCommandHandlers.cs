using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 处理向商人购买商品的意图。
    /// </summary>
    /// <remarks>
    /// 顺序是"先校验、再扣钱、最后放物品"：任何一步失败都返回失败且不改动状态。
    /// 余额与仓库分别由 <see cref="MetaProgress"/> 与 <see cref="InventoryGrid"/> 持有，
    /// 处理器只负责把两者串成一个原子操作。
    /// </remarks>
    public sealed class BuyItemCommandHandler : ICommandHandler<BuyItemIntent>
    {
        private readonly MetaProgress m_Progress;
        private readonly IItemDefinitionLookup m_Catalog;
        private readonly TraderCatalog m_Trader;
        private readonly EventBus m_EventBus;
        private readonly ItemFactory m_Factory = new ItemFactory();

        /// <summary>创建处理器。</summary>
        public BuyItemCommandHandler(
            MetaProgress progress,
            IItemDefinitionLookup catalog,
            TraderCatalog trader,
            EventBus eventBus)
        {
            m_Progress = progress;
            m_Catalog = catalog;
            m_Trader = trader;
            m_EventBus = eventBus;
        }

        /// <inheritdoc />
        public CommandResult Execute(in BuyItemIntent command)
        {
            if (m_Progress == null || m_Catalog == null || m_Trader == null)
            {
                return CommandResult.Fail(CommandCodes.InternalError, "商人系统尚未完成装配。");
            }

            if (!m_Trader.TryGetEntry(command.ItemId, out _))
            {
                return CommandResult.Fail(
                    CommandCodes.MetaItemNotSold,
                    $"商人不出售 {command.ItemId}。");
            }

            if (!m_Catalog.TryGet(command.ItemId, out var definition))
            {
                return CommandResult.Fail(
                    CommandCodes.MetaItemNotSold,
                    $"物品 {command.ItemId} 在当前版本中不存在。");
            }

            if (command.Count <= 0 || command.Count > definition.MaxStack)
            {
                return CommandResult.Fail(
                    CommandCodes.MetaInvalidQuantity,
                    $"一次最多购买 {definition.MaxStack} 个 {definition.DisplayName}。");
            }

            var price = TraderPricing.GetBuyPrice(definition, command.Count);
            if (m_Progress.Money < price)
            {
                return CommandResult.Fail(
                    CommandCodes.MetaInsufficientFunds,
                    $"金币不足：需要 {price:N0}，当前 {m_Progress.Money:N0}。");
            }

            var item = m_Factory.Create(definition, command.Count);
            if (!m_Progress.Stash.CanAutoPlace(item))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryFull,
                    "仓库空间不足，先腾出空间再购买。");
            }

            if (!m_Progress.TrySpend(price))
            {
                return CommandResult.Fail(CommandCodes.MetaInsufficientFunds, "金币不足。");
            }

            var placed = m_Progress.Stash.AutoPlace(item);
            if (!placed.Success)
            {
                // 放回失败时退回金币，避免出现"钱扣了、东西没拿到"。
                m_Progress.AddMoney(price);
                return CommandResult.Fail(
                    CommandCodes.InventoryFull,
                    "仓库空间不足，购买已取消。");
            }

            m_Progress.NotifyChanged();
            m_EventBus?.Publish(new WalletChangedEvent(
                m_Progress.Money,
                -price,
                "buy:" + definition.Id));
            return CommandResult.Ok();
        }
    }

    /// <summary>
    /// 处理把仓库物品卖给商人的意图。
    /// </summary>
    /// <remarks>
    /// 单件出售只是批量出售的一个特例：两条入口共用同一份校验、扣物与结算逻辑，
    /// 避免"右键出售"和"批量出售"在某个边界条件下出现两套不同结果。
    /// </remarks>
    public sealed class SellItemCommandHandler : ICommandHandler<SellItemIntent>
    {
        private readonly SellItemsCommandHandler m_BatchHandler;

        /// <summary>创建处理器。</summary>
        public SellItemCommandHandler(
            MetaProgress progress,
            ContainerRegistry registry,
            EventBus eventBus)
        {
            m_BatchHandler = new SellItemsCommandHandler(progress, registry, eventBus);
        }

        /// <inheritdoc />
        public CommandResult Execute(in SellItemIntent command)
        {
            var items = new[]
            {
                new SellItemRef(command.CellX, command.CellY),
            };
            var batch = new SellItemsIntent(
                command.PlayerId,
                command.ContainerId,
                items,
                command.Sequence);
            return m_BatchHandler.Execute(in batch);
        }
    }

    /// <summary>
    /// 处理批量出售仓库物品的意图。
    /// </summary>
    /// <remarks>
    /// <b>只接受仓库容器。</b>随身装备与背包里的东西不参与交易，
    /// 从规则层就杜绝"手滑卖掉正在穿的护甲"。
    /// 批量结算的顺序是"全部校验 → 全部扣除 → 一次加钱"：
    /// 中途任何一件扣除失败都会把已扣除的物品放回原格，余额不变。
    /// </remarks>
    public sealed class SellItemsCommandHandler : ICommandHandler<SellItemsIntent>
    {
        /// <summary>已扣除物品的暂存记录，用于失败回滚。</summary>
        private readonly struct SoldStack
        {
            public SoldStack(ItemInstance item, GridPoint origin, bool rotated)
            {
                Item = item;
                Origin = origin;
                Rotated = rotated;
            }

            public ItemInstance Item { get; }

            public GridPoint Origin { get; }

            public bool Rotated { get; }
        }

        private readonly MetaProgress m_Progress;
        private readonly ContainerRegistry m_Registry;
        private readonly EventBus m_EventBus;

        /// <summary>创建处理器。</summary>
        public SellItemsCommandHandler(
            MetaProgress progress,
            ContainerRegistry registry,
            EventBus eventBus)
        {
            m_Progress = progress;
            m_Registry = registry;
            m_EventBus = eventBus;
        }

        /// <inheritdoc />
        public CommandResult Execute(in SellItemsIntent command)
        {
            if (m_Progress == null || m_Registry == null)
            {
                return CommandResult.Fail(CommandCodes.InternalError, "商人系统尚未完成装配。");
            }

            if (command.Items == null || command.Items.Length == 0)
            {
                return CommandResult.Fail(CommandCodes.MetaInvalidQuantity, "没有选择要出售的物品。");
            }

            if (!m_Registry.TryGetGrid(command.ContainerId, out var grid)
                || !m_Registry.TryGetKind(command.ContainerId, out var kind)
                || kind != ContainerKind.Stash)
            {
                return CommandResult.Fail(
                    CommandCodes.Rejected,
                    "只能出售仓库里的物品。");
            }

            var picked = CollectItems(grid, command.Items);
            if (!picked.Success)
            {
                return picked.Result;
            }

            var total = 0;
            for (var i = 0; i < picked.Stacks.Count; i++)
            {
                var stack = picked.Stacks[i];
                total += TraderPricing.GetSellPrice(stack.Item.Definition, stack.Item.StackCount);
            }

            var removed = new List<SoldStack>(picked.Stacks.Count);
            for (var i = 0; i < picked.Stacks.Count; i++)
            {
                var stack = picked.Stacks[i];
                var result = grid.Remove(stack.Item);
                if (!result.Success)
                {
                    Rollback(grid, removed);
                    return CommandResult.Fail(
                        CommandCodes.Rejected,
                        "批量出售失败，已还原全部物品：" + result.Message);
                }

                removed.Add(stack);
            }

            m_Progress.AddMoney(total);
            m_Progress.NotifyChanged();
            m_EventBus?.Publish(new WalletChangedEvent(
                m_Progress.Money,
                total,
                "sell_batch:" + removed.Count));
            m_EventBus?.Publish(new InventoryChangedEvent(
                command.ContainerId,
                InventoryChangeTypes.Remove,
                command.PlayerId,
                0d,
                command.Sequence));
            return CommandResult.Ok();
        }

        /// <summary>收集并校验全部待售物品，不修改任何状态。</summary>
        private static CollectionResult CollectItems(InventoryGrid grid, SellItemRef[] refs)
        {
            var stacks = new List<SoldStack>(refs.Length);
            var seen = new HashSet<ItemInstance>();

            for (var i = 0; i < refs.Length; i++)
            {
                var item = grid.GetAt(new GridPoint(refs[i].CellX, refs[i].CellY));
                if (item == null)
                {
                    return new CollectionResult(
                        false,
                        CommandResult.Fail(
                            CommandCodes.InventoryNotFound,
                            $"格子 ({refs[i].CellX},{refs[i].CellY}) 上已经没有物品。"),
                        null);
                }

                // 同一件物品可能被多个格子引用（多格物品占多格）。只结算一次。
                if (!seen.Add(item))
                {
                    continue;
                }

                grid.TryGetOrigin(item, out var origin);
                stacks.Add(new SoldStack(item, origin, item.Rotated));
            }

            if (stacks.Count == 0)
            {
                return new CollectionResult(
                    false,
                    CommandResult.Fail(CommandCodes.MetaInvalidQuantity, "没有可出售的物品。"),
                    null);
            }

            return new CollectionResult(true, CommandResult.Ok(), stacks);
        }

        /// <summary>把已扣除的物品放回原格，恢复交易前状态。</summary>
        private static void Rollback(InventoryGrid grid, List<SoldStack> removed)
        {
            for (var i = 0; i < removed.Count; i++)
            {
                var stack = removed[i];
                if (!grid.Place(stack.Item, stack.Origin, stack.Rotated).Success)
                {
                    // 原格被刚放回的另一件物品占用时才走自动放置；正常情况下不会发生。
                    grid.AutoPlace(stack.Item);
                }
            }
        }

        /// <summary>物品收集结果。</summary>
        private readonly struct CollectionResult
        {
            public CollectionResult(bool success, CommandResult result, List<SoldStack> stacks)
            {
                Success = success;
                Result = result;
                Stacks = stacks;
            }

            public bool Success { get; }

            public CommandResult Result { get; }

            public List<SoldStack> Stacks { get; }
        }
    }
}
