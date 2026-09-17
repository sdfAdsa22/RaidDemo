using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using Unity.Netcode;
using UnityEngine;

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
            // 不再要求 m_Combat 存在：安全屋世界不创建战斗权威，装备直接来自账号档案
            // （见 ResolveCommandLoadout）。以前这里的 m_Combat 守卫正是"安全屋命令通道建不起来"的一环。
            if (m_InventoryRouters.ContainsKey(playerId))
            {
                return;
            }

            var loadout = ResolveCommandLoadout(playerId);
            if (loadout == null)
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
            // 与客户端注册的是同一批 handler：快速转移 / 旋转 / 整理 / 拆分也走服务器执行，
            // 否则这些操作会在客户端本地发生、再被权威内容覆盖回去（U-75）。
            router.Register<InventoryQuickTransferIntent>(new InventoryQuickTransferCommandHandler(context));
            router.Register<InventoryRotateIntent>(new InventoryRotateCommandHandler(context));
            router.Register<InventorySortIntent>(new InventorySortCommandHandler(context));
            router.Register<InventorySplitIntent>(new InventorySplitCommandHandler(context));
            // 装备与卸下用同一套 handler：服务器与客户端执行的规则完全一致。
            router.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(context));
            router.Register<InventoryUnequipIntent>(new InventoryUnequipCommandHandler(context));
            m_InventoryRouters[playerId] = router;

            m_Session?.Log.Info($"[服务器] 玩家 {playerId} 的背包命令通道已建立。");
        }

        /// <summary>
        /// 取玩家在背包命令通道里使用的随身装备。
        /// </summary>
        /// <remarks>
        /// <para><b>战局里</b>用参战时交给 CombatWorld 的那一份；<b>安全屋里</b>玩家没有参战
        /// （<c>AddPlayerToCombat</c> 在安全屋世界传 null），直接回退到账号档案里的那一份——
        /// 两者本来就是同一个对象（战局参战传入的就是 <c>profile.Loadout</c>）。</para>
        ///
        /// <para><b>为什么必须回退（负责人反馈的"第二把空手"）：</b>命令通道原先只在战局里建立，
        /// 安全屋里的装备 / 整理命令被服务器丢弃——客户端"本地先执行"让界面看起来换好了，
        /// 但服务器档案里还是"上一把撤离后清空"的装备，于是下一局进图空手。</para>
        /// </remarks>
        private RaidDemo.Inventory.PlayerLoadout ResolveCommandLoadout(int playerId)
        {
            if (m_Combat != null
                && m_Combat.TryGetLoadout(playerId, out var combatLoadout)
                && combatLoadout != null)
            {
                return combatLoadout;
            }

            return ResolveProfileForPlayer(playerId)?.Loadout;
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

            // 按"命令种类"分派到对应的意图：所有种类共用一条上行通道（见 InventoryCommandKinds）。
            CommandResult result;
            switch (message.Kind)
            {
                case InventoryCommandKinds.QuickTransfer:
                    result = router.Dispatch(new InventoryQuickTransferIntent(
                        playerId, source, target, message.SourceCellX, message.SourceCellY, message.Sequence));
                    break;

                case InventoryCommandKinds.Rotate:
                    result = router.Dispatch(new InventoryRotateIntent(
                        playerId, source, message.SourceCellX, message.SourceCellY, message.Sequence));
                    break;

                case InventoryCommandKinds.Sort:
                    result = router.Dispatch(new InventorySortIntent(playerId, source, message.Sequence));
                    break;

                case InventoryCommandKinds.Split:
                    result = router.Dispatch(new InventorySplitIntent(
                        playerId, source, message.SourceCellX, message.SourceCellY, message.Count, message.Sequence));
                    break;

                default:
                    result = router.Dispatch(new InventoryMoveIntent(
                        playerId,
                        source,
                        target,
                        message.SourceCellX,
                        message.SourceCellY,
                        message.TargetCellX,
                        message.TargetCellY,
                        message.Rotated,
                        message.Sequence));
                    break;
            }

            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose(
                    $"[服务器] 玩家 {playerId} 背包命令（种类 {message.Kind}）："
                    + $"{message.SourceContainerId}->{message.TargetContainerId}"
                    + $" 结果={result.Success}（{result.Code}）");
            }

            if (result.Success)
            {
                // 背包 / 装备是账号档案的一部分：成功后标脏，按节流写盘——
                // 漏掉这一步时"整理完就关服"会把改动丢掉（下一次登录装备就回去了）。
                MarkProfilesDirty();
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
                    // P5：仓库是**房间共享**的，不再按玩家分。所有玩家的 3 号容器
                    // 都落到同一个共享仓库网格上，两个人同时搬东西时由服务器串行执行。
                    return ContainerIds.ServerSharedStash;
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

            // 自检痕迹：把"服务器侧这名玩家的随身容器里到底有什么"写进日志。
            // 没有它时，客户端"背包还是空的"这一现象无法区分三种原因——
            // 服务器没搬成功、服务器搬了但没下发、下发的内容被客户端丢掉了（P4 验收实机排查）。
            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose(
                    $"[服务器] 玩家 {playerId} 随身容器自检：背包 {DescribeGrid(playerId, ContainerIds.PlayerSlot.Backpack)}"
                    + $"；弹药挂 {DescribeGrid(playerId, ContainerIds.PlayerSlot.AmmoPouch)}");
            }
        }

        /// <summary>把某个随身容器的内容压成一行（自检日志用）。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="slot">随身容器槽位。</param>
        private string DescribeGrid(int playerId, ContainerIds.PlayerSlot slot)
        {
            var id = ContainerIds.ServerPlayerContainer(playerId, slot);
            if (!m_Containers.TryGetGrid(id, out var grid))
            {
                return $"容器 {id} 未注册";
            }

            var builder = new System.Text.StringBuilder();
            builder.Append(grid.Items.Count).Append(" 件[");
            for (var i = 0; i < grid.Items.Count; i++)
            {
                var item = grid.Items[i];
                grid.TryGetOrigin(item, out var origin);
                builder.Append(item.Definition != null ? item.Definition.Id : "?")
                    .Append('@').Append(origin.X).Append(',').Append(origin.Y).Append(' ');
            }

            builder.Append(']');
            return builder.ToString();
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

            // 入口校验（RD-AUD-046）：Slot 与 Kind 都是上行字节，直接强转成枚举再拿去索引
            // 长度 5 的槽位数组，会走到越界路径（异常会被命令路由兜住，但那是"事后补救"）。
            // 非法输入一律丢弃，并把权威状态推回客户端——它在本地是"先动过界面"的。
            if (message.Slot >= EquipmentLoadout.SlotCount
                || (message.Kind != InventoryEquipCommandMessage.KindEquip
                    && message.Kind != InventoryEquipCommandMessage.KindUnequip
                    && message.Kind != InventoryEquipCommandMessage.KindUse))
            {
                m_Session?.Log.Warning(
                    $"[服务器] 玩家 {playerId} 的装备命令不合法（Kind {message.Kind}，槽位 {message.Slot}），已丢弃。");
                RefreshContainersAfterCommand(playerId);
                return;
            }

            if (message.Kind == InventoryEquipCommandMessage.KindUse)
            {
                HandleItemUseCommand(playerId, in message);
                return;
            }

            CommandResult result;
            if (message.Kind == InventoryEquipCommandMessage.KindEquip)
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

            // 装备变化要落档：节流写盘（否则"换好装备就关服"会让变更丢失）。
            MarkProfilesDirty();

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
