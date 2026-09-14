using RaidDemo.Shared;
using RaidDemo.Inventory;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端上行背包命令的处理器集合（快速转移 / 旋转 / 整理 / 拆分）。
    /// </summary>
    /// <remarks>
    /// <para>与"发送与注册"分开放：这一半只回答"某条意图该带哪些字段上行"，
    /// 那一半回答"消息怎么发、哪些意图被接管"。两份东西改动原因不同，合成一个文件会互相淹没。</para>
    /// <para>它们的共同点：**都不在本地执行**——容器的权威在服务器，本地改了也会被下发的权威内容覆盖（U-75）。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>双击快速转移：上行给服务器执行。</summary>
        private sealed class MultiplayerQuickTransferCommandHandler : ICommandHandler<InventoryQuickTransferIntent>
        {
            private readonly SceneBootstrap m_Owner;

            /// <summary>创建处理器。</summary>
            /// <param name="owner">所属装配根。</param>
            public MultiplayerQuickTransferCommandHandler(SceneBootstrap owner)
            {
                m_Owner = owner;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryQuickTransferIntent command)
            {
                return m_Owner.SendInventoryKindToServer(
                    InventoryCommandKinds.QuickTransfer,
                    command.SourceContainerId,
                    command.TargetContainerId,
                    command.SourceCellX,
                    command.SourceCellY,
                    0,
                    0,
                    rotated: false,
                    count: 0);
            }
        }

        /// <summary>旋转：上行给服务器执行。</summary>
        private sealed class MultiplayerRotateCommandHandler : ICommandHandler<InventoryRotateIntent>
        {
            private readonly SceneBootstrap m_Owner;

            /// <summary>创建处理器。</summary>
            /// <param name="owner">所属装配根。</param>
            public MultiplayerRotateCommandHandler(SceneBootstrap owner)
            {
                m_Owner = owner;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryRotateIntent command)
            {
                return m_Owner.SendInventoryKindToServer(
                    InventoryCommandKinds.Rotate,
                    command.ContainerId,
                    command.ContainerId,
                    command.CellX,
                    command.CellY,
                    0,
                    0,
                    rotated: false,
                    count: 0);
            }
        }

        /// <summary>整理：上行给服务器执行。</summary>
        private sealed class MultiplayerSortCommandHandler : ICommandHandler<InventorySortIntent>
        {
            private readonly SceneBootstrap m_Owner;

            /// <summary>创建处理器。</summary>
            /// <param name="owner">所属装配根。</param>
            public MultiplayerSortCommandHandler(SceneBootstrap owner)
            {
                m_Owner = owner;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventorySortIntent command)
            {
                return m_Owner.SendInventoryKindToServer(
                    InventoryCommandKinds.Sort,
                    command.ContainerId,
                    command.ContainerId,
                    0,
                    0,
                    0,
                    0,
                    rotated: false,
                    count: 0);
            }
        }

        /// <summary>拆分：上行给服务器执行。</summary>
        private sealed class MultiplayerSplitCommandHandler : ICommandHandler<InventorySplitIntent>
        {
            private readonly SceneBootstrap m_Owner;

            /// <summary>创建处理器。</summary>
            /// <param name="owner">所属装配根。</param>
            public MultiplayerSplitCommandHandler(SceneBootstrap owner)
            {
                m_Owner = owner;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventorySplitIntent command)
            {
                return m_Owner.SendInventoryKindToServer(
                    InventoryCommandKinds.Split,
                    command.ContainerId,
                    command.ContainerId,
                    command.CellX,
                    command.CellY,
                    0,
                    0,
                    rotated: false,
                    count: command.Count);
            }
        }
    }
}
