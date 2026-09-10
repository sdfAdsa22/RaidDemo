using RaidDemo.Data;
using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 处理装备意图：把背包里的物品装到装备槽上。
    /// </summary>
    /// <remarks>
    /// <para>本处理器比其他背包处理器多一个步骤——它必须先做一次"取出"，
    /// 因为物品要从容器搬到槽位。这就带来了两个容易出错的点，代码里都做了显式处理：</para>
    /// <list type="number">
    /// <item><description>装备失败时，物品必须回到它原来的格子，否则玩家的东西会凭空消失。
    /// 因此取出的坐标与朝向要先记下来。</description></item>
    /// <item><description>槽位上原有装备的去处由装备栏负责确认（传入主背包作为退路），
    /// 确认不了就整体失败，不会出现"新装备没装上、旧装备也没了"。</description></item>
    /// </list>
    /// </remarks>
    public sealed class InventoryEquipCommandHandler : ICommandHandler<InventoryEquipIntent>
    {
        private readonly InventoryContext m_Context;

        /// <summary>创建处理器。</summary>
        /// <param name="context">背包运行时上下文。</param>
        public InventoryEquipCommandHandler(InventoryContext context)
        {
            m_Context = context;
        }

        /// <inheritdoc />
        public CommandResult Execute(in InventoryEquipIntent command)
        {
            if (!m_Context.TryResolve(command.ContainerId, out var grid))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到容器 {command.ContainerId}。");
            }

            var cell = new GridPoint(command.CellX, command.CellY);
            var item = grid.GetAt(cell);
            if (item == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "该格子上没有物品。");
            }

            var wasRotated = item.Rotated;
            var removed = grid.Remove(item);
            if (!removed.Success)
            {
                return InventoryContext.ToCommandResult(removed);
            }

            var equip = m_Context.Loadout.Equipment.Equip(item, command.Slot, m_Context.Loadout.Backpack);
            if (!equip.Success)
            {
                PutBackOrElsewhere(grid, item, cell, wasRotated);
                return InventoryContext.ToCommandResult(equip);
            }

            m_Context.PublishChanged(
                command.ContainerId, InventoryChangeTypes.Equip, command.PlayerId, command.Sequence);
            return CommandResult.Ok();
        }

        /// <summary>
        /// 把物品放回原格；原格万一被占（理论上不会发生），就退一步找任意空位。
        /// </summary>
        /// <remarks>
        /// 这是最后一道保险。装备失败本身是正常结果，但"装备失败导致物品丢失"不是——
        /// 宁可在别处放下让玩家自己搬回去，也不能让东西消失。
        /// </remarks>
        private static void PutBackOrElsewhere(InventoryGrid grid, ItemInstance item, GridPoint cell, bool rotated)
        {
            if (grid.Place(item, cell, rotated).Success)
            {
                return;
            }

            grid.AutoPlace(item);
        }
    }

    /// <summary>
    /// 处理卸下意图：把装备槽里的物品放回主背包。
    /// </summary>
    /// <remarks>
    /// 卸下比装备简单一层：物品只有一个来源（槽位）和一个去处（主背包），
    /// 而放不下的判断由装备栏在真正改动之前完成。
    /// </remarks>
    public sealed class InventoryUnequipCommandHandler : ICommandHandler<InventoryUnequipIntent>
    {
        private readonly InventoryContext m_Context;

        /// <summary>创建处理器。</summary>
        /// <param name="context">背包运行时上下文。</param>
        public InventoryUnequipCommandHandler(InventoryContext context)
        {
            m_Context = context;
        }

        /// <inheritdoc />
        public CommandResult Execute(in InventoryUnequipIntent command)
        {
            var result = m_Context.Loadout.Equipment.Unequip(command.Slot, m_Context.Loadout.Backpack);
            if (!result.Success)
            {
                return InventoryContext.ToCommandResult(result);
            }

            // 卸下的物品进的是主背包，因此变更通知发给主背包对应的容器。
            // 主背包的容器 ID 由启动层注册时确定，这里通过注册表反查。
            PublishBackpackChanged(command.PlayerId, command.Sequence);
            return CommandResult.Ok();
        }

        /// <summary>向主背包容器广播变更。</summary>
        private void PublishBackpackChanged(int playerId, uint sequence)
        {
            var ids = m_Context.Registry.ContainerIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (!m_Context.Registry.TryGetGrid(ids[i], out var grid))
                {
                    continue;
                }

                if (ReferenceEquals(grid, m_Context.Loadout.Backpack))
                {
                    m_Context.PublishChanged(ids[i], InventoryChangeTypes.Unequip, playerId, sequence);
                    return;
                }
            }
        }
    }
}
