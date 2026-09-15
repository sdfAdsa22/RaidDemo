using RaidDemo.Inventory;
using RaidDemo.Shared;
using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 容器链路的上行部分：把本地的背包 / 装备意图变成服务器消息。
    /// </summary>
    /// <remarks>
    /// <para>与"内容下行"分开放：这一半只回答"某条意图该带哪些字段上行、怎么发"，
    /// 那一半回答"服务器发来的内容怎么铺到本地网格"。两份东西改动原因不同。</para>
    ///
    /// <para>所有上行都用**可靠有序**投递：背包命令有先后语义（先拿走的那个才算拿到），
    /// 丢一条或乱序都会让"谁拿到了"变得不可解释。</para>
    /// </remarks>
    internal sealed partial class MultiplayerContainerLink
    {
        /// <summary>
        /// 把一条"只改容器内容"的意图发到服务器（移动 / 快速转移 / 旋转 / 整理 / 拆分）。
        /// </summary>
        private CommandResult SendInventoryKindToServer(
            byte kind,
            int sourceContainerId,
            int targetContainerId,
            int sourceCellX,
            int sourceCellY,
            int targetCellX,
            int targetCellY,
            bool rotated,
            int count)
        {
            if (m_Network == null || !m_Network.IsConnectedClient || m_Network.CustomMessagingManager == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "尚未连接到服务器。");
            }

            var message = new InventoryMoveCommandMessage
            {
                Kind = kind,
                SourceContainerId = sourceContainerId,
                TargetContainerId = targetContainerId,
                SourceCellX = sourceCellX,
                SourceCellY = sourceCellY,
                TargetCellX = targetCellX,
                TargetCellY = targetCellY,
                Rotated = rotated,
                Count = count,
                Sequence = ++m_Sequence,
            };

            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                m_Network.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.CommandMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            // 返回成功而不是"已发送"：界面已乐观更新，真正的结果由服务器回发的容器内容纠正。
            return CommandResult.Ok();
        }

        /// <summary>
        /// 把一条"使用这一格里的物品"的意图发给服务器（联机医疗品）。
        /// </summary>
        /// <param name="containerId">客户端侧的容器编号（<c>ContainerIds</c>）。</param>
        /// <param name="cellX">格子 X。</param>
        /// <param name="cellY">格子 Y。</param>
        /// <remarks>
        /// <para>与装备命令共用一条通道与一个结构体：它需要的"来源容器 + 格子"这两项信息那儿已经有了，
        /// 而回血与扣物品都必须由服务器执行（`RD-AUD-044`），因此客户端只发意图、不做乐观修改。</para>
        /// </remarks>
        public void RequestItemUse(int containerId, int cellX, int cellY)
        {
            if (m_Network == null || !m_Network.IsConnectedClient || m_Network.CustomMessagingManager == null)
            {
                return;
            }

            var message = new InventoryEquipCommandMessage
            {
                Kind = InventoryEquipCommandMessage.KindUse,
                ContainerId = containerId,
                CellX = cellX,
                CellY = cellY,
                Slot = 0,
                Sequence = ++m_Sequence,
            };

            using (var writer = new FastBufferWriter(32, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                m_Network.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.EquipCommandMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>把一条装备 / 卸下意图发给服务器。</summary>
        private void SendEquipToServer(int containerId, int cellX, int cellY, EquipmentSlot slot, bool unequip)
        {
            if (m_Network == null || !m_Network.IsConnectedClient || m_Network.CustomMessagingManager == null)
            {
                return;
            }

            var message = new InventoryEquipCommandMessage
            {
                Kind = unequip ? (byte)1 : (byte)0,
                ContainerId = containerId,
                CellX = cellX,
                CellY = cellY,
                Slot = (byte)slot,
                Sequence = ++m_Sequence,
            };

            using (var writer = new FastBufferWriter(32, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                m_Network.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.EquipCommandMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>装备：先本地执行（界面即时反馈），再通知服务器。</summary>
        private CommandResult HandleEquipLocallyAndNotify(in InventoryEquipIntent command)
        {
            var handler = new InventoryEquipCommandHandler(new InventoryContext(m_Registry, m_Loadout, m_Events));
            var result = handler.Execute(command);

            if (result.Success)
            {
                SendEquipToServer(command.ContainerId, command.CellX, command.CellY, command.Slot, unequip: false);
            }

            return result;
        }

        /// <summary>卸下：先本地执行，再通知服务器。</summary>
        private CommandResult HandleUnequipLocallyAndNotify(in InventoryUnequipIntent command)
        {
            var handler = new InventoryUnequipCommandHandler(new InventoryContext(m_Registry, m_Loadout, m_Events));
            var result = handler.Execute(command);

            if (result.Success)
            {
                SendEquipToServer(0, 0, 0, command.Slot, unequip: true);
            }

            return result;
        }

        /// <summary>把一条移动意图转成上行消息的处理器。</summary>
        private sealed class MultiplayerMoveCommandHandler : ICommandHandler<InventoryMoveIntent>
        {
            private readonly MultiplayerContainerLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属容器链路。</param>
            public MultiplayerMoveCommandHandler(MultiplayerContainerLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryMoveIntent command)
            {
                return m_Link.SendInventoryKindToServer(
                    InventoryCommandKinds.Move,
                    command.SourceContainerId,
                    command.TargetContainerId,
                    command.SourceCellX,
                    command.SourceCellY,
                    command.TargetCellX,
                    command.TargetCellY,
                    command.Rotated,
                    count: 0);
            }
        }

        /// <summary>双击快速转移：上行给服务器执行。</summary>
        private sealed class MultiplayerQuickTransferCommandHandler : ICommandHandler<InventoryQuickTransferIntent>
        {
            private readonly MultiplayerContainerLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属容器链路。</param>
            public MultiplayerQuickTransferCommandHandler(MultiplayerContainerLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryQuickTransferIntent command)
            {
                return m_Link.SendInventoryKindToServer(
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
            private readonly MultiplayerContainerLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属容器链路。</param>
            public MultiplayerRotateCommandHandler(MultiplayerContainerLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryRotateIntent command)
            {
                return m_Link.SendInventoryKindToServer(
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
            private readonly MultiplayerContainerLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属容器链路。</param>
            public MultiplayerSortCommandHandler(MultiplayerContainerLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventorySortIntent command)
            {
                return m_Link.SendInventoryKindToServer(
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
            private readonly MultiplayerContainerLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属容器链路。</param>
            public MultiplayerSplitCommandHandler(MultiplayerContainerLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventorySplitIntent command)
            {
                return m_Link.SendInventoryKindToServer(
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

        /// <summary>装备：本地执行 + 上行。</summary>
        private sealed class MultiplayerEquipCommandHandler : ICommandHandler<InventoryEquipIntent>
        {
            private readonly MultiplayerContainerLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属容器链路。</param>
            public MultiplayerEquipCommandHandler(MultiplayerContainerLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryEquipIntent command)
            {
                return m_Link.HandleEquipLocallyAndNotify(command);
            }
        }

        /// <summary>卸下：本地执行 + 上行。</summary>
        private sealed class MultiplayerUnequipCommandHandler : ICommandHandler<InventoryUnequipIntent>
        {
            private readonly MultiplayerContainerLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属容器链路。</param>
            public MultiplayerUnequipCommandHandler(MultiplayerContainerLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryUnequipIntent command)
            {
                return m_Link.HandleUnequipLocallyAndNotify(command);
            }
        }
    }
}
