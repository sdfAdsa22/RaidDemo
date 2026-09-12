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

            if (!m_Trader.TryGetEntry(command.ItemId, out var entry))
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
    /// <b>只接受仓库容器。</b>随身装备与背包里的东西不参与交易，
    /// 从规则层就杜绝"手滑卖掉正在穿的护甲"。
    /// </remarks>
    public sealed class SellItemCommandHandler : ICommandHandler<SellItemIntent>
    {
        private readonly MetaProgress m_Progress;
        private readonly ContainerRegistry m_Registry;
        private readonly EventBus m_EventBus;

        /// <summary>创建处理器。</summary>
        public SellItemCommandHandler(
            MetaProgress progress,
            ContainerRegistry registry,
            EventBus eventBus)
        {
            m_Progress = progress;
            m_Registry = registry;
            m_EventBus = eventBus;
        }

        /// <inheritdoc />
        public CommandResult Execute(in SellItemIntent command)
        {
            if (m_Progress == null || m_Registry == null)
            {
                return CommandResult.Fail(CommandCodes.InternalError, "商人系统尚未完成装配。");
            }

            if (!m_Registry.TryGetGrid(command.ContainerId, out var grid)
                || !m_Registry.TryGetKind(command.ContainerId, out var kind)
                || kind != ContainerKind.Stash)
            {
                return CommandResult.Fail(
                    CommandCodes.Rejected,
                    "只能出售仓库里的物品。");
            }

            var item = grid.GetAt(new GridPoint(command.CellX, command.CellY));
            if (item == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "该格子上没有物品。");
            }

            var price = TraderPricing.GetSellPrice(item.Definition, item.StackCount);
            var removed = grid.Remove(item);
            if (!removed.Success)
            {
                return CommandResult.Fail(CommandCodes.Rejected, "物品出售失败：" + removed.Message);
            }

            m_Progress.AddMoney(price);
            m_Progress.NotifyChanged();
            m_EventBus?.Publish(new WalletChangedEvent(
                m_Progress.Money,
                price,
                "sell:" + item.Definition.Id));
            return CommandResult.Ok();
        }
    }
}
