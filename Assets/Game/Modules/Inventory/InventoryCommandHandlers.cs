using RaidDemo.Data;
using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 处理物品移动意图。
    /// </summary>
    /// <remarks>
    /// 七个背包处理器（移动、快速转移、旋转、拆分、整理、装备、卸下）的写法完全一致：
    /// 按 ID 找到容器，取出格子上的物品，调用规则层，成功时广播事件。
    ///
    /// 规则一律不写在这里。处理器只负责翻译——把命令翻译成规则层调用，
    /// 再把失败原因翻译成结果码。之所以坚持这一点，是因为拖拽与快捷键必须走同一条规则路径。
    /// 一旦规则在处理器里也抄了一份，迟早会出现拖拽被拦、快捷键却塞进去了的不一致。
    /// </remarks>
    public sealed class InventoryMoveCommandHandler : ICommandHandler<InventoryMoveIntent>
    {
        private readonly InventoryContext m_Context;

        /// <summary>创建处理器。</summary>
        /// <param name="context">背包运行时上下文。</param>
        public InventoryMoveCommandHandler(InventoryContext context)
        {
            m_Context = context;
        }

        /// <inheritdoc />
        public CommandResult Execute(in InventoryMoveIntent command)
        {
            if (!m_Context.TryResolve(command.SourceContainerId, out var source))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到源容器 {command.SourceContainerId}。");
            }

            if (!m_Context.TryResolve(command.TargetContainerId, out var target))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到目标容器 {command.TargetContainerId}。");
            }

            var item = source.GetAt(new GridPoint(command.SourceCellX, command.SourceCellY));
            if (item == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "源格子上没有物品。");
            }

            var origin = new GridPoint(command.TargetCellX, command.TargetCellY);
            var result = source.Transfer(item, target, origin, command.Rotated);
            if (!result.Success)
            {
                return InventoryContext.ToCommandResult(result);
            }

            // 合并与跨容器移动都要通知两边刷新：源容器少了一件，目标容器多了一件。
            var changeType = result.MovedCount > 0 ? InventoryChangeTypes.Stack : InventoryChangeTypes.Move;
            m_Context.PublishChanged(command.SourceContainerId, changeType, command.PlayerId, command.Sequence);
            if (command.TargetContainerId != command.SourceContainerId)
            {
                m_Context.PublishChanged(command.TargetContainerId, changeType, command.PlayerId, command.Sequence);
            }

            return CommandResult.Ok();
        }
    }

    /// <summary>
    /// 处理快速转移意图。
    /// </summary>
    /// <remarks>
    /// 与移动命令的唯一区别是目标落点未知，由规则层按先合并、后找空位的顺序决定。
    /// 两条路径最终调用的是同一份放置规则，因此不会出现只有其中一条能绕开限制的情况。
    /// </remarks>
    public sealed class InventoryQuickTransferCommandHandler : ICommandHandler<InventoryQuickTransferIntent>
    {
        private readonly InventoryContext m_Context;

        /// <summary>创建处理器。</summary>
        /// <param name="context">背包运行时上下文。</param>
        public InventoryQuickTransferCommandHandler(InventoryContext context)
        {
            m_Context = context;
        }

        /// <inheritdoc />
        public CommandResult Execute(in InventoryQuickTransferIntent command)
        {
            if (!m_Context.TryResolve(command.SourceContainerId, out var source))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到源容器 {command.SourceContainerId}。");
            }

            if (!m_Context.TryResolve(command.TargetContainerId, out var target))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到目标容器 {command.TargetContainerId}。");
            }

            var item = source.GetAt(new GridPoint(command.SourceCellX, command.SourceCellY));
            if (item == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "源格子上没有物品。");
            }

            var result = source.QuickTransfer(item, target);
            if (!result.Success)
            {
                return InventoryContext.ToCommandResult(result);
            }

            m_Context.PublishChanged(
                command.SourceContainerId, InventoryChangeTypes.Move, command.PlayerId, command.Sequence);
            m_Context.PublishChanged(
                command.TargetContainerId, InventoryChangeTypes.Move, command.PlayerId, command.Sequence);
            return CommandResult.Ok();
        }
    }

    /// <summary>处理就地旋转意图。</summary>
    public sealed class InventoryRotateCommandHandler : ICommandHandler<InventoryRotateIntent>
    {
        private readonly InventoryContext m_Context;

        /// <summary>创建处理器。</summary>
        /// <param name="context">背包运行时上下文。</param>
        public InventoryRotateCommandHandler(InventoryContext context)
        {
            m_Context = context;
        }

        /// <inheritdoc />
        public CommandResult Execute(in InventoryRotateIntent command)
        {
            if (!m_Context.TryResolve(command.ContainerId, out var grid))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到容器 {command.ContainerId}。");
            }

            var item = grid.GetAt(new GridPoint(command.CellX, command.CellY));
            if (item == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "该格子上没有物品。");
            }

            var result = grid.Rotate(item);
            if (!result.Success)
            {
                return InventoryContext.ToCommandResult(result);
            }

            m_Context.PublishChanged(
                command.ContainerId, InventoryChangeTypes.Move, command.PlayerId, command.Sequence);
            return CommandResult.Ok();
        }
    }

    /// <summary>处理拆分意图。</summary>
    public sealed class InventorySplitCommandHandler : ICommandHandler<InventorySplitIntent>
    {
        private readonly InventoryContext m_Context;

        /// <summary>创建处理器。</summary>
        /// <param name="context">背包运行时上下文。</param>
        public InventorySplitCommandHandler(InventoryContext context)
        {
            m_Context = context;
        }

        /// <inheritdoc />
        public CommandResult Execute(in InventorySplitIntent command)
        {
            if (!m_Context.TryResolve(command.ContainerId, out var grid))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到容器 {command.ContainerId}。");
            }

            var item = grid.GetAt(new GridPoint(command.CellX, command.CellY));
            if (item == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "该格子上没有物品。");
            }

            // 不指定落点的拆分：由规则层先找空位、再拆分，避免拆完放不下。
            var result = grid.Split(item, command.Count);
            if (!result.Success)
            {
                return InventoryContext.ToCommandResult(result);
            }

            m_Context.PublishChanged(
                command.ContainerId, InventoryChangeTypes.Split, command.PlayerId, command.Sequence);
            return CommandResult.Ok();
        }
    }

    /// <summary>处理自动整理意图。</summary>
    public sealed class InventorySortCommandHandler : ICommandHandler<InventorySortIntent>
    {
        private readonly InventoryContext m_Context;

        /// <summary>创建处理器。</summary>
        /// <param name="context">背包运行时上下文。</param>
        public InventorySortCommandHandler(InventoryContext context)
        {
            m_Context = context;
        }

        /// <inheritdoc />
        public CommandResult Execute(in InventorySortIntent command)
        {
            if (!m_Context.TryResolve(command.ContainerId, out var grid))
            {
                return CommandResult.Fail(
                    CommandCodes.InventoryNotFound,
                    $"找不到容器 {command.ContainerId}。");
            }

            // 整理永远成功：空间不足时规则层会整体回滚到原布局，物品一件不少。
            grid.Sort();
            m_Context.PublishChanged(
                command.ContainerId, InventoryChangeTypes.Sort, command.PlayerId, command.Sequence);
            return CommandResult.Ok();
        }
    }
}
