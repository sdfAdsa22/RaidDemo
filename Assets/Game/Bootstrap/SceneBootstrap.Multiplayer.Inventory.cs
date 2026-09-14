using RaidDemo.Shared;
using RaidDemo.Inventory;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的背包操作部分：把背包意图上行给服务器，本地只等结果。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么本地不执行：</b>容器的内容是服务器抽的（P3-1），背包能不能放下、
    /// 谁先拿到，都必须由服务器裁定。客户端若先本地改一遍，就会出现"我这边显示拿到了、
    /// 服务器说没有"这种必须回滚的界面状态，而背包的回滚比移动位置的回滚难写得多
    /// （物品实例、堆叠、格子占用全都要还原）。</para>
    ///
    /// <para><b>做法：换掉一个处理器，而不是重写命令层。</b>
    /// 命令路由本来就支持覆盖注册（<c>Register(handler, overwrite: true)</c>），
    /// 因此联机时把 <see cref="InventoryMoveIntent"/> 的本地处理器换成"发上行"这一个动作即可，
    /// 其余命令（换弹、装备、整理）暂时保持本地——它们只影响自己的装备，
    /// 等 P3 的后续批次再一起搬。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        private uint m_InventoryCommandSequence;

        /// <summary>
        /// 联机模式下接管背包移动命令：发上行，不本地执行。
        /// </summary>
        /// <remarks>在 <c>InitializeInventory</c> 注册完本地处理器之后调用。</remarks>
        private void OverrideInventoryCommandsForMultiplayer()
        {
            if (!ClientMode.IsActive || m_CommandRouter == null)
            {
                return;
            }

            m_CommandRouter.Register<InventoryMoveIntent>(
                new MultiplayerMoveCommandHandler(this),
                overwrite: true);

            // 装备：本地先执行（界面要即时反馈），同时把同一条意图上行，
            // 让服务器的手持武器与客户端一致——否则会出现"我拿着手枪、服务器按步枪结算"。
            m_CommandRouter.Register<InventoryEquipIntent>(
                new MultiplayerEquipCommandHandler(this),
                overwrite: true);
            m_CommandRouter.Register<InventoryUnequipIntent>(
                new MultiplayerUnequipCommandHandler(this),
                overwrite: true);

            Debug.Log("[联机] 背包移动改为上行；装备本地执行并同步给服务器。");
        }

        /// <summary>
        /// 把一条装备 / 卸下意图发给服务器。
        /// </summary>
        /// <param name="containerId">来源容器编号（卸下时为 0）。</param>
        /// <param name="cellX">来源格子 X。</param>
        /// <param name="cellY">来源格子 Y。</param>
        /// <param name="slot">装备槽。</param>
        /// <param name="unequip">是卸下还是装备。</param>
        private void SendEquipToServer(int containerId, int cellX, int cellY, EquipmentSlot slot, bool unequip)
        {
            if (m_NetworkClient == null || !m_NetworkClient.IsConnectedClient
                || m_NetworkClient.CustomMessagingManager == null)
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
                Sequence = ++m_InventoryCommandSequence,
            };

            using (var writer = new FastBufferWriter(32, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                m_NetworkClient.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.EquipCommandMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>装备：先本地执行（界面即时反馈），再通知服务器。</summary>
        internal CommandResult HandleEquipLocallyAndNotify(in InventoryEquipIntent command)
        {
            var handler = new InventoryEquipCommandHandler(
                new InventoryContext(m_ContainerRegistry, m_Loadout, m_EventBus));
            var result = handler.Execute(command);

            if (result.Success)
            {
                SendEquipToServer(
                    command.ContainerId, command.CellX, command.CellY, command.Slot, unequip: false);
            }

            return result;
        }

        /// <summary>卸下：先本地执行，再通知服务器。</summary>
        internal CommandResult HandleUnequipLocallyAndNotify(in InventoryUnequipIntent command)
        {
            var handler = new InventoryUnequipCommandHandler(
                new InventoryContext(m_ContainerRegistry, m_Loadout, m_EventBus));
            var result = handler.Execute(command);

            if (result.Success)
            {
                SendEquipToServer(0, 0, 0, command.Slot, unequip: true);
            }

            return result;
        }

        /// <summary>装备：本地执行 + 上行。</summary>
        private sealed class MultiplayerEquipCommandHandler : ICommandHandler<InventoryEquipIntent>
        {
            private readonly SceneBootstrap m_Owner;

            /// <summary>创建处理器。</summary>
            /// <param name="owner">所属装配根。</param>
            public MultiplayerEquipCommandHandler(SceneBootstrap owner)
            {
                m_Owner = owner;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryEquipIntent command)
            {
                return m_Owner.HandleEquipLocallyAndNotify(command);
            }
        }

        /// <summary>卸下：本地执行 + 上行。</summary>
        private sealed class MultiplayerUnequipCommandHandler : ICommandHandler<InventoryUnequipIntent>
        {
            private readonly SceneBootstrap m_Owner;

            /// <summary>创建处理器。</summary>
            /// <param name="owner">所属装配根。</param>
            public MultiplayerUnequipCommandHandler(SceneBootstrap owner)
            {
                m_Owner = owner;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryUnequipIntent command)
            {
                return m_Owner.HandleUnequipLocallyAndNotify(command);
            }
        }

        /// <summary>
        /// 把一条背包移动意图发到服务器。
        /// </summary>
        /// <param name="command">命令。</param>
        /// <returns>发送成功返回成功结果；未连接时返回失败（界面据此提示）。</returns>
        private CommandResult SendInventoryMoveToServer(in InventoryMoveIntent command)
        {
            if (m_NetworkClient == null || !m_NetworkClient.IsConnectedClient
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return CommandResult.Fail(CommandCodes.InventoryNotFound, "尚未连接到服务器。");
            }

            var message = new InventoryMoveCommandMessage
            {
                SourceContainerId = command.SourceContainerId,
                TargetContainerId = command.TargetContainerId,
                SourceCellX = command.SourceCellX,
                SourceCellY = command.SourceCellY,
                TargetCellX = command.TargetCellX,
                TargetCellY = command.TargetCellY,
                Rotated = command.Rotated,
                Sequence = ++m_InventoryCommandSequence,
            };

            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                m_NetworkClient.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.CommandMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            // 返回成功而不是"已发送"：界面已经把物品画到新位置了，
            // 真正的结果随后由服务器回发的容器内容纠正（见 SceneBootstrap.Multiplayer.Containers）。
            return CommandResult.Ok();
        }

        /// <summary>把一条移动意图转成上行消息的处理器。</summary>
        private sealed class MultiplayerMoveCommandHandler : ICommandHandler<InventoryMoveIntent>
        {
            private readonly SceneBootstrap m_Owner;

            /// <summary>创建处理器。</summary>
            /// <param name="owner">所属的装配根。</param>
            public MultiplayerMoveCommandHandler(SceneBootstrap owner)
            {
                m_Owner = owner;
            }

            /// <inheritdoc />
            public CommandResult Execute(in InventoryMoveIntent command)
            {
                return m_Owner.SendInventoryMoveToServer(command);
            }
        }
    }
}
