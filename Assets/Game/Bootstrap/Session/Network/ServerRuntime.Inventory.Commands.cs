using System.Collections.Generic;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的背包命令部分：执行客户端上行的背包意图（P3-2 起）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么复用客户端的命令层：</b>背包的规则（能不能放、怎么堆叠、占地怎么算）
    /// 已经写成了一组与引擎无关的 handler。服务器把它们**用同一份实现**再注册一遍，
    /// 规则就不可能出现"单机允许、联机拒绝"这种两套行为。</para>
    ///
    /// <para><b>每名玩家一套命令路由：</b>命令里的"1 号容器"是**玩家自己的背包**，
    /// 而服务器上一张注册表放着所有人的东西，因此每名玩家有自己的
    /// <c>InventoryContext</c>（同一个注册表 + 自己那份 <c>PlayerLoadout</c>），
    /// 命令进来先翻译编号，再投给它自己的路由。</para>
    ///
    /// <para><b>结果怎么回去：</b>执行完直接回发容器内容全量（P3-1 已经做好的那条路）。
    /// 客户端拿到新内容重建网格，界面照旧刷新——与战斗里"事件来源换成服务器广播"同一个套路。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>每名玩家的命令路由（编号 = 玩家编号）。</summary>
        private readonly Dictionary<int, CommandRouter> m_InventoryRouters =
            new Dictionary<int, CommandRouter>();

        /// <summary>把一名玩家的随身容器登记进服务器注册表，并给他一套背包命令路由。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>
        /// 编号用 <see cref="ContainerIds.ServerPlayerContainer"/>：客户端说的 1/2/3
        /// 在服务器上分别对应这名玩家自己的背包 / 弹药挂 / 仓库，翻译在这里完成。
        /// </remarks>
        private void RegisterPlayerContainers(int playerId)
        {
            if (m_Combat == null || m_InventoryRouters.ContainsKey(playerId))
            {
                return;
            }

            if (!m_Combat.TryGetLoadout(playerId, out var loadout) || loadout == null)
            {
                m_Session?.Log.Warning($"[服务器] 玩家 {playerId} 还没有随身装备，背包命令通道未建立。");
                return;
            }

            m_Containers.Register(
                loadout.Backpack, ContainerKind.PlayerBackpack,
                ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.Backpack));
            m_Containers.Register(
                loadout.AmmoPouch, ContainerKind.AmmoPouch,
                ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.AmmoPouch));

            var context = new InventoryContext(m_Containers, loadout, m_Session.Events);
            var router = new CommandRouter();
            router.Register<InventoryMoveIntent>(new InventoryMoveCommandHandler(context));
            // 装备与卸下用同一套 handler：服务器与客户端执行的规则完全一致。
            router.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(context));
            router.Register<InventoryUnequipIntent>(new InventoryUnequipCommandHandler(context));
            m_InventoryRouters[playerId] = router;

            m_Session?.Log.Info($"[服务器] 玩家 {playerId} 的背包命令通道已建立。");
        }

        /// <summary>玩家断开时撤掉他的命令路由与随身容器。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void UnregisterPlayerContainers(int playerId)
        {
            if (m_InventoryRouters.Remove(playerId))
            {
                m_Containers.Unregister(
                    ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.Backpack));
                m_Containers.Unregister(
                    ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.AmmoPouch));
            }
        }

        /// <summary>收到一条上行的背包移动命令。</summary>
        /// <param name="senderId">发起命令的客户端。</param>
        /// <param name="reader">消息体。</param>
        private void OnInventoryMoveCommandReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(InventoryMoveCommandMessage);
            reader.ReadValueSafe(out message);

            var playerId = (int)senderId;
            if (!m_InventoryRouters.TryGetValue(playerId, out var router))
            {
                m_Session?.Log.Warning($"[服务器] 玩家 {playerId} 的背包命令通道不存在，命令被丢弃。");
                return;
            }

            var source = TranslateContainerId(playerId, message.SourceContainerId);
            var target = TranslateContainerId(playerId, message.TargetContainerId);

            var intent = new InventoryMoveIntent(
                playerId,
                source,
                target,
                message.SourceCellX,
                message.SourceCellY,
                message.TargetCellX,
                message.TargetCellY,
                message.Rotated,
                message.Sequence);

            var result = router.Dispatch(intent);

            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose(
                    $"[服务器] 玩家 {playerId} 背包命令：{message.SourceContainerId}->{message.TargetContainerId}"
                    + $" 结果={result.Success}（{result.Code}）");
            }

            // 不论成败都回发一次：成功时客户端要看到新内容，
            // 失败时客户端要把自己可能画出的"乐观结果"改回权威状态。
            RefreshContainersAfterCommand(playerId);
        }

        /// <summary>
        /// 把客户端编号翻译成服务器注册表里的编号。
        /// </summary>
        /// <remarks>
        /// 小于 <see cref="ContainerIds.SceneBase"/> 的编号一律理解成"这名玩家自己的容器"：
        /// 客户端说的 1/2/3 就是它的背包/弹药挂/仓库。
        /// </remarks>
        private static int TranslateContainerId(int playerId, int clientContainerId)
        {
            switch (clientContainerId)
            {
                case ContainerIds.PlayerBackpack:
                    return ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.Backpack);
                case ContainerIds.AmmoPouch:
                    return ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.AmmoPouch);
                case ContainerIds.Stash:
                    return ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.Stash);
                default:
                    // 场景容器两端编号一致，原样返回。
                    return clientContainerId;
            }
        }

        /// <summary>
        /// 命令执行后刷新内容：场景容器发给所有人，发起者自己的容器发给他本人。
        /// </summary>
        /// <param name="playerId">发起命令的玩家。</param>
        private void RefreshContainersAfterCommand(int playerId)
        {
            // 场景容器可能被拿走了一件：所有人都要看到。
            BroadcastAllContainerContents();

            // 自己背包/弹药挂的变化只发给他一个人。
            SendContainerContentsTo(playerId);
        }

        /// <summary>收到一条上行的装备 / 卸下命令。</summary>
        /// <param name="senderId">发起命令的客户端。</param>
        /// <param name="reader">消息体。</param>
        private void OnInventoryEquipCommandReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(InventoryEquipCommandMessage);
            reader.ReadValueSafe(out message);

            var playerId = (int)senderId;
            if (!m_InventoryRouters.TryGetValue(playerId, out var router))
            {
                return;
            }

            CommandResult result;
            if (message.Kind == 0)
            {
                result = router.Dispatch(new InventoryEquipIntent(
                    playerId,
                    TranslateContainerId(playerId, message.ContainerId),
                    message.CellX,
                    message.CellY,
                    (EquipmentSlot)message.Slot,
                    message.Sequence));
            }
            else
            {
                result = router.Dispatch(new InventoryUnequipIntent(
                    playerId,
                    (EquipmentSlot)message.Slot,
                    message.Sequence));
            }

            if (!result.Success)
            {
                // 客户端的界面已经"先动过了"：这里只记一笔，纠正通路留给 P5 的装备持久化。
                m_Session?.Log.Warning(
                    $"[服务器] 玩家 {playerId} 的装备命令被拒绝：{result.Code} - {result.Message}");
                return;
            }

            // 手持武器变了就必须重新同步到战斗权威上：
            // 射程、伤害、口径、换弹来源全按"当前武器"算，漏了这一步会出现
            // "客户端拿着手枪、服务器按步枪结算"。
            m_Combat?.SyncWeaponFromLoadout(playerId);

            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose(
                    $"[服务器] 玩家 {playerId} 的装备命令已执行（槽 {message.Slot}，种类 {message.Kind}）。");
            }

            // 背包与装备的变化回发给本人（其他地方不会用到他的装备）。
            SendContainerContentsTo(playerId);
        }
    }
}
