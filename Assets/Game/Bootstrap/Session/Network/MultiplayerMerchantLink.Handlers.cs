using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 商人链路的上行部分：把商店与任务的本地意图翻成服务器消息。
    /// </summary>
    /// <remarks>
    /// <para>每个处理器都只做三件事：读意图里的字段、编码成 <c>MerchantTradeMessage</c>、
    /// 交给链路发送。不在这里做任何规则判断——价格、库存、任务状态全都由服务器算，
    /// 客户端判断得再对也不算数，判断错了反而会误导玩家。</para>
    ///
    /// <para>与单机的同名处理器（<c>TraderCommandHandlers</c> / <c>QuestCommandHandlers</c>）是
    /// 替换关系：注册顺序上后注册的覆盖先注册的，因此联机安全屋拿到的是这一套。</para>
    /// </remarks>
    internal sealed partial class MultiplayerMerchantLink
    {
        /// <summary>购买：上行物品 ID 与数量。</summary>
        private sealed class MultiplayerBuyCommandHandler : ICommandHandler<BuyItemIntent>
        {
            private readonly MultiplayerMerchantLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属商人链路。</param>
            public MultiplayerBuyCommandHandler(MultiplayerMerchantLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in BuyItemIntent command)
            {
                return m_Link.SendBuy(command.ItemId, command.Count);
            }
        }

        /// <summary>单件出售：上行一个格子坐标。</summary>
        private sealed class MultiplayerSellCommandHandler : ICommandHandler<SellItemIntent>
        {
            private readonly MultiplayerMerchantLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属商人链路。</param>
            public MultiplayerSellCommandHandler(MultiplayerMerchantLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in SellItemIntent command)
            {
                var refs = new[]
                {
                    new SellItemRef(command.CellX, command.CellY),
                };
                return m_Link.SendSell(refs, batch: false);
            }
        }

        /// <summary>批量出售：上行全部格子坐标（服务器原子结算）。</summary>
        private sealed class MultiplayerSellBatchCommandHandler : ICommandHandler<SellItemsIntent>
        {
            private readonly MultiplayerMerchantLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属商人链路。</param>
            public MultiplayerSellBatchCommandHandler(MultiplayerMerchantLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in SellItemsIntent command)
            {
                return m_Link.SendSell(command.Items, batch: true);
            }
        }

        /// <summary>
        /// 四个任务操作共用的处理器。
        /// </summary>
        /// <remarks>
        /// 四个意图的结构相同（任务 ID + 序号），差别只在语义；
        /// 用一份实现承接四份接口，避免四份几乎一样的转发代码各写一遍。
        /// </remarks>
        private sealed class MultiplayerQuestCommandHandler :
            ICommandHandler<QuestAcceptIntent>,
            ICommandHandler<QuestTrackIntent>,
            ICommandHandler<QuestTurnInIntent>,
            ICommandHandler<QuestClaimIntent>
        {
            private readonly MultiplayerMerchantLink m_Link;

            /// <summary>创建处理器。</summary>
            /// <param name="link">所属商人链路。</param>
            public MultiplayerQuestCommandHandler(MultiplayerMerchantLink link)
            {
                m_Link = link;
            }

            /// <inheritdoc />
            public CommandResult Execute(in QuestAcceptIntent command)
            {
                return m_Link.SendQuest(MerchantTradeKinds.QuestAccept, command.QuestId);
            }

            /// <inheritdoc />
            public CommandResult Execute(in QuestTrackIntent command)
            {
                return m_Link.SendQuest(MerchantTradeKinds.QuestTrack, command.QuestId);
            }

            /// <inheritdoc />
            public CommandResult Execute(in QuestTurnInIntent command)
            {
                return m_Link.SendQuest(MerchantTradeKinds.QuestTurnIn, command.QuestId);
            }

            /// <inheritdoc />
            public CommandResult Execute(in QuestClaimIntent command)
            {
                return m_Link.SendQuest(MerchantTradeKinds.QuestClaim, command.QuestId);
            }
        }
    }
}
