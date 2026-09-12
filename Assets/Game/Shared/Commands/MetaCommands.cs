namespace RaidDemo.Shared
{
    /// <summary>向商人购买一件商品（可包含一组数量）。</summary>
    /// <remarks>
    /// 命令只携带物品 ID 与数量，不携带价格。价格由权威侧的商人规则计算，
    /// 客户端不能通过改价或改余额来凭空取得物品。
    /// </remarks>
    public readonly struct BuyItemIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "meta.buy_item";

        /// <summary>创建购买意图。</summary>
        public BuyItemIntent(int playerId, string itemId, int count = 1, uint sequence = 0u)
        {
            PlayerId = playerId;
            ItemId = itemId;
            Count = count;
            Sequence = sequence;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <summary>要购买的物品 ID。</summary>
        public string ItemId { get; }

        /// <summary>购买数量。</summary>
        public int Count { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"BuyItemIntent(P{PlayerId}, {ItemId} x{Count})";
        }
    }

    /// <summary>把仓库里的一堆物品卖给商人。</summary>
    /// <remarks>
    /// 与背包命令一致，用容器 ID 加格子坐标定位物品；
    /// 界面不能凭空构造一件物品递给商人。
    /// </remarks>
    public readonly struct SellItemIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "meta.sell_item";

        /// <summary>创建出售意图。</summary>
        public SellItemIntent(
            int playerId,
            int containerId,
            int cellX,
            int cellY,
            uint sequence = 0u)
        {
            PlayerId = playerId;
            ContainerId = containerId;
            CellX = cellX;
            CellY = cellY;
            Sequence = sequence;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <summary>物品所在容器 ID。必须允许交易，当前只接受仓库。</summary>
        public int ContainerId { get; }

        /// <summary>物品所在格的横坐标。</summary>
        public int CellX { get; }

        /// <summary>物品所在格的纵坐标。</summary>
        public int CellY { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"SellItemIntent(P{PlayerId}, c{ContainerId}({CellX},{CellY}))";
        }
    }

    /// <summary>接取任务。</summary>
    public readonly struct QuestAcceptIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "meta.quest_accept";

        /// <summary>创建接取意图。</summary>
        public QuestAcceptIntent(int playerId, string questId, uint sequence = 0u)
        {
            PlayerId = playerId;
            QuestId = questId;
            Sequence = sequence;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <summary>任务 ID。</summary>
        public string QuestId { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"QuestAcceptIntent(P{PlayerId}, {QuestId})";
        }
    }

    /// <summary>把任务设为追踪目标。</summary>
    public readonly struct QuestTrackIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "meta.quest_track";

        /// <summary>创建追踪意图。</summary>
        public QuestTrackIntent(int playerId, string questId, uint sequence = 0u)
        {
            PlayerId = playerId;
            QuestId = questId;
            Sequence = sequence;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <summary>任务 ID。</summary>
        public string QuestId { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"QuestTrackIntent(P{PlayerId}, {QuestId})";
        }
    }

    /// <summary>上交任务要求的物品。</summary>
    public readonly struct QuestTurnInIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "meta.quest_turn_in";

        /// <summary>创建上交意图。</summary>
        public QuestTurnInIntent(int playerId, string questId, uint sequence = 0u)
        {
            PlayerId = playerId;
            QuestId = questId;
            Sequence = sequence;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <summary>任务 ID。</summary>
        public string QuestId { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"QuestTurnInIntent(P{PlayerId}, {QuestId})";
        }
    }

    /// <summary>领取任务奖励。</summary>
    public readonly struct QuestClaimIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "meta.quest_claim";

        /// <summary>创建领奖意图。</summary>
        public QuestClaimIntent(int playerId, string questId, uint sequence = 0u)
        {
            PlayerId = playerId;
            QuestId = questId;
            Sequence = sequence;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <summary>任务 ID。</summary>
        public string QuestId { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"QuestClaimIntent(P{PlayerId}, {QuestId})";
        }
    }
}
