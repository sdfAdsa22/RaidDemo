using System.Collections.Generic;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Shared;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的商人链路（P5.5）：交易 / 任务意图上行，结果与任务快照下行。
    /// </summary>
    /// <remarks>
    /// <para><b>它替代了什么：</b>单机时购买 / 出售 / 接任务都是本地命令处理器直接改
    /// 本地 <c>MetaProgress</c>；联机时那两样东西（金币、共享仓库）在服务器上，
    /// 本地只是镜像。本类把商店与任务的命令处理器整套换成"只上行"的版本——
    /// 与 <see cref="MultiplayerContainerLink"/> 对背包命令做的事完全一样。</para>
    ///
    /// <para><b>任务快照为什么也要下行：</b>任务的规则（进度怎么涨、奖励多少）两端共用同一份
    /// 静态目录，但状态（接没接、进度多少）是账号级的、存在服务器上。因此服务器把状态
    /// 整批发下来，客户端用 <c>QuestSystem.RestoreState</c> 覆盖本地镜像——界面照旧只读本地副本。</para>
    ///
    /// <para><b>通道归属是静态的：</b>与移动 / 容器链路同一条规矩——处理器名只有一个，
    /// 场景切换的顺序是"新场景注册、旧场景退订"，旧场景若无条件退订就会把新场景刚注册的
    /// 处理器删掉（P-49.4 的教训）。</para>
    /// </remarks>
    internal sealed partial class MultiplayerMerchantLink
    {
        /// <summary>当前真正持有商人通道的链路（跨场景注册的归属标记）。</summary>
        private static MultiplayerMerchantLink s_ChannelOwner;

        private readonly SessionScope m_Session;
        private readonly MetaProgress m_Progress;
        private readonly EventBus m_Events;

        /// <summary>任务快照应用时的复用缓冲。</summary>
        private readonly List<QuestSaveRecord> m_QuestRecords =
            new List<QuestSaveRecord>(MerchantQuestStateMessage.SlotCount);

        /// <summary>应用任务快照时收集的问题（只用于日志）。</summary>
        private readonly List<string> m_QuestProblems = new List<string>();

        private NetworkManager m_Network;
        private bool m_HandlerRegistered;
        private uint m_Sequence;

        /// <summary>创建商人链路。</summary>
        /// <param name="session">会话（日志）。</param>
        /// <param name="progress">本地镜像（任务快照的写入目标；金币由 ProfileState 通道写）。</param>
        /// <param name="events">本地事件总线（交易结果要通知界面）。</param>
        public MultiplayerMerchantLink(SessionScope session, MetaProgress progress, EventBus events)
        {
            m_Session = session;
            m_Progress = progress;
            m_Events = events;
        }

        /// <summary>通道是否已经挂上。</summary>
        public bool IsAttached
        {
            get { return m_Network != null; }
        }

        /// <summary>本实例是不是商人通道的当前持有者（跨场景退订保护）。</summary>
        public bool IsChannelOwner
        {
            get { return ReferenceEquals(s_ChannelOwner, this); }
        }

        /// <summary>
        /// 接管连接：订阅结果与任务快照两条下行通道。
        /// </summary>
        /// <param name="network">大厅会话持有的网络管理器。</param>
        public void Attach(NetworkManager network)
        {
            if (network == null)
            {
                return;
            }

            m_Network = network;
            s_ChannelOwner = this;

            if (!m_HandlerRegistered && m_Network.CustomMessagingManager != null)
            {
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                    MerchantNetworkChannel.ResultMessageName,
                    OnMerchantResultReceived);
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                    MerchantNetworkChannel.QuestStateMessageName,
                    OnQuestStateReceived);
                m_HandlerRegistered = true;
            }
        }

        /// <summary>退订通道。</summary>
        /// <remarks>不是当前持有者时只清自己的状态：那个处理器可能是新场景刚注册的那一个。</remarks>
        public void Detach()
        {
            if (m_Network != null && m_HandlerRegistered && IsChannelOwner)
            {
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    MerchantNetworkChannel.ResultMessageName);
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    MerchantNetworkChannel.QuestStateMessageName);
                s_ChannelOwner = null;
            }

            m_HandlerRegistered = false;
            m_Network = null;
        }

        /// <summary>
        /// 把商店与任务的本地命令处理器换成"只上行"的版本。
        /// </summary>
        /// <param name="router">本场景的命令路由。</param>
        /// <remarks>
        /// 在场景注册完本地处理器之后调用（覆盖注册）：与背包命令同一条规则——
        /// 界面照旧派发同一种意图，只是意图的去处从"本地执行"变成"服务器执行"。
        /// </remarks>
        public void InstallCommandHandlers(CommandRouter router)
        {
            if (router == null)
            {
                return;
            }

            router.Register<BuyItemIntent>(new MultiplayerBuyCommandHandler(this), overwrite: true);
            router.Register<SellItemIntent>(new MultiplayerSellCommandHandler(this), overwrite: true);
            router.Register<SellItemsIntent>(new MultiplayerSellBatchCommandHandler(this), overwrite: true);

            // 四个任务操作共用一个处理器实例：它们实现的是四份不同的接口（参数类型不同），
            // 每份接口的 Execute 自带自己的种类，不需要外部再传一遍。
            var questHandler = new MultiplayerQuestCommandHandler(this);
            router.Register<QuestAcceptIntent>(questHandler, overwrite: true);
            router.Register<QuestTrackIntent>(questHandler, overwrite: true);
            router.Register<QuestTurnInIntent>(questHandler, overwrite: true);
            router.Register<QuestClaimIntent>(questHandler, overwrite: true);

            m_Session?.Log.Info("[联机] 购买 / 出售 / 任务改为上行，由服务器执行（P5.5）。");
        }

        /// <summary>发送一条购买意图。</summary>
        /// <param name="itemId">物品 ID。</param>
        /// <param name="count">数量。</param>
        /// <returns>发送成功返回 Ok；未连接时返回失败。</returns>
        internal CommandResult SendBuy(string itemId, int count)
        {
            var message = new MerchantTradeMessage
            {
                Kind = MerchantTradeKinds.Buy,
                ItemId = itemId ?? string.Empty,
                Count = count,
                Sequence = ++m_Sequence,
            };
            return Send(in message);
        }

        /// <summary>发送一条出售意图（单件或批量）。</summary>
        /// <param name="refs">待售格子。</param>
        /// <param name="batch">是否批量（<see cref="MerchantTradeKinds.SellBatch"/>）。</param>
        /// <returns>发送成功返回 Ok；未连接或清单非法时返回失败。</returns>
        internal CommandResult SendSell(IReadOnlyList<SellItemRef> refs, bool batch)
        {
            if (refs == null || refs.Count == 0)
            {
                return CommandResult.Fail(CommandCodes.MetaInvalidQuantity, "没有选择要出售的物品。");
            }

            if (refs.Count > MerchantTradeMessage.MaxSellCells)
            {
                return CommandResult.Fail(
                    CommandCodes.MetaInvalidQuantity,
                    $"一次最多出售 {MerchantTradeMessage.MaxSellCells} 件物品。");
            }

            var message = new MerchantTradeMessage
            {
                Kind = batch ? MerchantTradeKinds.SellBatch : MerchantTradeKinds.Sell,
                Sequence = ++m_Sequence,
            };

            for (var i = 0; i < refs.Count; i++)
            {
                message.AddCell(refs[i].CellX, refs[i].CellY);
            }

            return Send(in message);
        }

        /// <summary>发送一条任务意图。</summary>
        /// <param name="kind">任务操作种类（<see cref="MerchantTradeKinds"/>）。</param>
        /// <param name="questId">任务 ID。</param>
        /// <returns>发送成功返回 Ok；未连接时返回失败。</returns>
        internal CommandResult SendQuest(byte kind, string questId)
        {
            var message = new MerchantTradeMessage
            {
                Kind = kind,
                ItemId = questId ?? string.Empty,
                Sequence = ++m_Sequence,
            };
            return Send(in message);
        }

        /// <summary>把一条交易消息发往服务器。</summary>
        private CommandResult Send(in MerchantTradeMessage message)
        {
            if (m_Network == null || !m_Network.IsConnectedClient || m_Network.CustomMessagingManager == null)
            {
                return CommandResult.Fail(CommandCodes.Rejected, "尚未连接到服务器。");
            }

            using (var writer = new FastBufferWriter(640, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                m_Network.CustomMessagingManager.SendNamedMessage(
                    MerchantNetworkChannel.TradeMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            // 返回成功而不是"已发送"：界面会先显示乐观文案，真正的结果由服务器的
            // ResultMessage 覆盖（成功换成权威文案、失败换成原因）。
            return CommandResult.Ok();
        }

        /// <summary>收到一条交易结果：交给事件总线，界面据此更新提示条。</summary>
        private void OnMerchantResultReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(MerchantResultMessage);
            reader.ReadValueSafe(out message);

            m_Events?.Publish(new MerchantTradeResultEvent(
                message.Kind,
                message.Success,
                message.Detail.ToString()));
        }

        /// <summary>
        /// 收到任务状态快照：覆盖本地镜像。
        /// </summary>
        /// <remarks>
        /// 用 <c>RestoreState</c> 而不是逐条打补丁：它与读档走的是同一条路径
        /// （连"前置任务解锁"的重算都一样），因此不可能出现"联机下的任务状态
        /// 与单机读档后的状态规则不一致"。
        /// </remarks>
        private void OnQuestStateReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(MerchantQuestStateMessage);
            reader.ReadValueSafe(out message);

            if (m_Progress == null)
            {
                return;
            }

            var count = message.QuestCount;
            if (count < 0)
            {
                count = 0;
            }
            else if (count > MerchantQuestStateMessage.SlotCount)
            {
                count = MerchantQuestStateMessage.SlotCount;
            }

            m_QuestRecords.Clear();
            for (var i = 0; i < count; i++)
            {
                var entry = message.GetQuest(i);
                m_QuestRecords.Add(new QuestSaveRecord
                {
                    questId = entry.QuestId.ToString(),
                    state = entry.State,
                    progress = entry.Progress,
                });
            }

            m_QuestProblems.Clear();
            m_Progress.Quests.RestoreState(
                m_QuestRecords,
                message.TrackedQuestId.ToString(),
                m_QuestProblems);

            for (var i = 0; i < m_QuestProblems.Count; i++)
            {
                Debug.LogWarning("[联机] 任务快照：" + m_QuestProblems[i]);
            }
        }
    }
}
