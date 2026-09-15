using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Meta;
using RaidDemo.Shared;
using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的商人经济部分（P5.5）：购买 / 出售 / 任务的服务端权威执行。
    /// </summary>
    /// <remarks>
    /// <para><b>它复用谁：</b>交易与任务的**规则**早就在 <c>TraderCommandHandlers</c> 与
    /// <c>QuestCommandHandlers</c> 里写好了（M6 定稿），而且这些 handler 只依赖
    /// "一份 MetaProgress + 容器注册表 + 静态目录"。服务器把同一批 handler
    /// 用**同一个实现**再执行一遍，规则就不可能出现"单机允许、联机拒绝"的两套行为——
    /// 这正是 P3-2 对背包命令做过的事，本批次把商店与任务接进同一条路线。</para>
    ///
    /// <para><b>每名玩家一套 handler：</b>金币与任务是**账号级**的（各算各的），
    /// 而仓库是房间共享的（大家卖的是同一份库存）。<c>MetaProgress</c> 在服务端的构造
    /// （<c>ServerProfileStore</c>）里已经把自己的 Stash 指向共享仓库，
    /// 因此"购买放入 Stash"与"出售从 Stash 扣除"自然落到同一个网格上，
    /// 两个玩家同时交易时由服务器主线程串行执行。</para>
    ///
    /// <para><b>结果怎么回去：</b>结果消息只带成败与文案；金币走 P5 的
    /// <c>ProfileStateMessage</c>，物品走容器内容批次，任务走本批次新增的任务快照。
    /// 一条数据只有一个权威来源。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>商人货架（静态目录，服务器与客户端各持一份同源副本）。</summary>
        private TraderCatalog m_TraderCatalog;

        /// <summary>建立商人经济：静态货架 + 上行通道。</summary>
        /// <remarks>在 <c>Initialize</c> 的装配末尾调用（此时网络已监听、会话已就绪）。</remarks>
        private void InitializeMerchant()
        {
            m_TraderCatalog = TraderCatalog.CreateDefault();
            RegisterMerchantHandlers();

            m_Session?.Log.Info(
                "[服务器] 商人经济已就绪：购买 / 出售 / 任务全部由服务端执行（P5.5）。");
        }

        /// <summary>
        /// 注册商人交易通道。
        /// </summary>
        /// <remarks>首次启动与传输层重建（P-51）后都要调用：NGO 每次启动会话都会新建消息管理器。</remarks>
        private void RegisterMerchantHandlers()
        {
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                MerchantNetworkChannel.TradeMessageName,
                OnMerchantTradeReceived);
        }

        /// <summary>收到一条上行的交易 / 任务意图。</summary>
        /// <param name="senderId">发起请求的客户端。</param>
        /// <param name="reader">消息体。</param>
        private void OnMerchantTradeReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(MerchantTradeMessage);
            reader.ReadValueSafe(out message);

            var playerId = (int)senderId;
            var kind = message.Kind;
            var success = ExecuteMerchantTrade(playerId, in message, out var detail);

            SendMerchantResult(playerId, kind, success, detail);

            if (!success)
            {
                m_Session?.Log.Info(
                    $"[服务器] 玩家 {playerId} 的{MerchantTradeKinds.Describe(kind)}被拒绝：{detail}");
                return;
            }

            // 成功的副作用：金币（可能变了）、任务（可能变了）、仓库（可能变了）各走各的权威通道。
            // 三条都发是刻意的：交易与任务操作都可能牵动其中任意几项，
            // "按种类猜哪条要发"迟早会漏。
            SendProfileStateTo(playerId, ProfileStateReasons.Traded);
            SendQuestStateTo(playerId);
            BroadcastAllContainerContents();
            SendContainerContentsTo(playerId);
            MarkProfilesDirty();

            m_Session?.Log.Info($"[服务器] 玩家 {playerId} 的{MerchantTradeKinds.Describe(kind)}完成：{detail}");
        }

        /// <summary>
        /// 执行一条交易 / 任务意图。
        /// </summary>
        /// <param name="playerId">请求者编号。</param>
        /// <param name="message">意图内容。</param>
        /// <param name="detail">输出：成功摘要或失败原因（直接展示给玩家）。</param>
        /// <returns>执行成功返回 true。</returns>
        private bool ExecuteMerchantTrade(
            int playerId,
            in MerchantTradeMessage message,
            out string detail)
        {
            detail = null;

            var profile = ResolveProfileForPlayer(playerId);
            if (profile == null)
            {
                detail = "账号进度尚未就绪，请稍后再试。";
                return false;
            }

            var catalog = ServerMode.SceneItemCatalog;
            if (catalog == null)
            {
                detail = "服务器物品目录尚未就绪，请稍后再试。";
                return false;
            }

            switch (message.Kind)
            {
                case MerchantTradeKinds.Buy:
                    return ExecuteBuy(playerId, profile, catalog, in message, out detail);

                case MerchantTradeKinds.Sell:
                case MerchantTradeKinds.SellBatch:
                    return ExecuteSell(playerId, profile, in message, out detail);

                case MerchantTradeKinds.QuestAccept:
                case MerchantTradeKinds.QuestTrack:
                case MerchantTradeKinds.QuestTurnIn:
                case MerchantTradeKinds.QuestClaim:
                    return ExecuteQuest(playerId, profile, in message, out detail);

                default:
                    detail = $"未知的交易类型（{message.Kind}）。";
                    return false;
            }
        }

        /// <summary>执行购买：规则与单机完全同一份 handler。</summary>
        private bool ExecuteBuy(
            int playerId,
            MetaProgress profile,
            IItemDefinitionLookup catalog,
            in MerchantTradeMessage message,
            out string detail)
        {
            var itemId = message.ItemId.ToString();
            var result = new BuyItemCommandHandler(profile, catalog, m_TraderCatalog, m_Session.Events)
                .Execute(new BuyItemIntent(playerId, itemId, message.Count, message.Sequence));

            if (!result.Success)
            {
                detail = result.Message;
                return false;
            }

            var name = catalog.TryGet(itemId, out var definition) ? definition.DisplayName : itemId;
            detail = $"已购买 {name} x{message.Count}。";
            return true;
        }

        /// <summary>执行出售（单件与批量的规则完全一致）。</summary>
        /// <remarks>
        /// 客户端只允许出售仓库（界面层已限制），服务端把它翻译成共享仓库编号——
        /// 即使客户端伪造了别的容器编号，规则层也只接受 Stash 类容器。
        /// </remarks>
        private bool ExecuteSell(
            int playerId,
            MetaProgress profile,
            in MerchantTradeMessage message,
            out string detail)
        {
            var count = message.CellCount;
            if (count <= 0 || count > MerchantTradeMessage.MaxSellCells)
            {
                detail = count <= 0 ? "没有选择要出售的物品。" : $"一次最多出售 {MerchantTradeMessage.MaxSellCells} 件物品。";
                return false;
            }

            var refs = new SellItemRef[count];
            for (var i = 0; i < count; i++)
            {
                var cell = message.Cells[i];
                refs[i] = new SellItemRef(cell.X, cell.Y);
            }

            var result = new SellItemsCommandHandler(profile, m_Containers, m_Session.Events)
                .Execute(new SellItemsIntent(
                    playerId, ContainerIds.ServerSharedStash, refs, message.Sequence));

            if (!result.Success)
            {
                detail = result.Message;
                return false;
            }

            detail = $"已出售 {count} 件物品。";
            return true;
        }

        /// <summary>执行任务操作（接取 / 追踪 / 上交 / 领奖）。</summary>
        private bool ExecuteQuest(
            int playerId,
            MetaProgress profile,
            in MerchantTradeMessage message,
            out string detail)
        {
            var questId = message.ItemId.ToString();
            CommandResult result;
            string successText;

            switch (message.Kind)
            {
                case MerchantTradeKinds.QuestAccept:
                    result = new QuestAcceptCommandHandler(profile.Quests)
                        .Execute(new QuestAcceptIntent(playerId, questId, message.Sequence));
                    successText = "任务已接取。";
                    break;

                case MerchantTradeKinds.QuestTrack:
                    result = new QuestTrackCommandHandler(profile.Quests)
                        .Execute(new QuestTrackIntent(playerId, questId, message.Sequence));
                    successText = "已开始追踪该任务。";
                    break;

                case MerchantTradeKinds.QuestTurnIn:
                    result = new QuestTurnInCommandHandler(profile.Quests)
                        .Execute(new QuestTurnInIntent(playerId, questId, message.Sequence));
                    successText = "任务物品已上交。";
                    break;

                default:
                    result = new QuestClaimCommandHandler(profile.Quests)
                        .Execute(new QuestClaimIntent(playerId, questId, message.Sequence));
                    successText = "奖励已领取。";
                    break;
            }

            if (!result.Success)
            {
                detail = result.Message;
                return false;
            }

            detail = successText;
            return true;
        }

        /// <summary>把交易结果点对点发给请求者。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="kind">请求种类。</param>
        /// <param name="success">是否成功。</param>
        /// <param name="detail">展示文案。</param>
        private void SendMerchantResult(int playerId, byte kind, bool success, string detail)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null || !IsClientConnected((ulong)playerId))
            {
                return;
            }

            var message = new MerchantResultMessage
            {
                Kind = kind,
                Success = success,
                Detail = detail ?? string.Empty,
            };

            using (var writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(
                    MerchantNetworkChannel.ResultMessageName,
                    (ulong)playerId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>
        /// 把任务状态快照点对点发给这名玩家。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>
        /// 三个时机都会调用：登录 / 重连（让客户端的镜像与服务器对齐）、
        /// 一次任务操作之后、撤离结算与击杀推进之后（战局内 HUD 也要跟着动）。
        /// </remarks>
        internal void SendQuestStateTo(int playerId)
        {
            var manager = m_Network;
            var profile = ResolveProfileForPlayer(playerId);
            if (manager == null || manager.CustomMessagingManager == null || profile == null
                || !IsClientConnected((ulong)playerId))
            {
                return;
            }

            var records = profile.Quests.CaptureState();
            var message = new MerchantQuestStateMessage
            {
                QuestCount = 0,
                TrackedQuestId = profile.Quests.TrackedQuestId ?? string.Empty,
            };

            for (var i = 0; i < records.Length && i < MerchantQuestStateMessage.SlotCount; i++)
            {
                message.SetQuest(i, new MerchantQuestEntry
                {
                    QuestId = records[i].questId,
                    State = (byte)records[i].state,
                    Progress = records[i].progress,
                });
                message.QuestCount = i + 1;
            }

            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(
                    MerchantNetworkChannel.QuestStateMessageName,
                    (ulong)playerId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose(
                    $"[服务器] 已下发玩家 {playerId} 的任务快照（{message.QuestCount} 条，"
                    + $"追踪 {message.TrackedQuestId}）。");
            }
        }
    }
}
