namespace RaidDemo.Shared
{
    /// <summary>把物品从一处移动到另一处，可携带旋转意图。</summary>
    /// <remarks>
    /// <para>所有这些背包命令的共同前提：它们是玩家意图，必须经由 CommandRouter 执行，
    /// 界面不得直接修改容器。这是本项目最重要的架构约束在背包上的落点。</para>
    ///
    /// <para><b>为什么命令携带的是容器 ID 加格子坐标，而不是物品数据本身：</b>
    /// 坐标只是位置描述，服务端据此在权威数据里查找物品并做规则校验，
    /// 因此客户端无法凭空构造一件物品递给服务端。若命令里带上完整的物品数据，
    /// 改过客户端的玩家就能凭空造出任意装备。</para>
    ///
    /// <para>命令同样不携带结果，只携带意图。能否放置由规则层判定，
    /// 客户端预判只是为了让拖拽有即时反馈，真正的裁决永远在权威侧。</para>
    /// </remarks>
    public readonly struct InventoryMoveIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "inventory.move";

        /// <summary>创建移动意图。</summary>
        public InventoryMoveIntent(
            int playerId,
            int sourceContainerId,
            int targetContainerId,
            int sourceCellX,
            int sourceCellY,
            int targetCellX,
            int targetCellY,
            bool rotated = false,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            SourceContainerId = sourceContainerId;
            TargetContainerId = targetContainerId;
            SourceCellX = sourceCellX;
            SourceCellY = sourceCellY;
            TargetCellX = targetCellX;
            TargetCellY = targetCellY;
            Rotated = rotated;
            Sequence = sequence;
            Timestamp = timestamp;
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
        public double Timestamp { get; }

        /// <summary>源容器 ID。</summary>
        public int SourceContainerId { get; }

        /// <summary>目标容器 ID。</summary>
        public int TargetContainerId { get; }

        /// <summary>源物品所在格的横坐标。</summary>
        public int SourceCellX { get; }

        /// <summary>源物品所在格的纵坐标。</summary>
        public int SourceCellY { get; }

        /// <summary>目标左上角的横坐标。</summary>
        public int TargetCellX { get; }

        /// <summary>目标左上角的纵坐标。</summary>
        public int TargetCellY { get; }

        /// <summary>是否旋转 90 度放置。</summary>
        public bool Rotated { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"InventoryMoveIntent(P{PlayerId}, c{SourceContainerId}({SourceCellX},{SourceCellY}) -> " +
                   $"c{TargetContainerId}({TargetCellX},{TargetCellY}), rotated={Rotated})";
        }
    }

    /// <summary>一键转移：不指定落点，由规则寻找位置。</summary>
    public readonly struct InventoryQuickTransferIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "inventory.quick_transfer";

        /// <summary>创建快速转移意图。</summary>
        public InventoryQuickTransferIntent(
            int playerId,
            int sourceContainerId,
            int targetContainerId,
            int sourceCellX,
            int sourceCellY,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            SourceContainerId = sourceContainerId;
            TargetContainerId = targetContainerId;
            SourceCellX = sourceCellX;
            SourceCellY = sourceCellY;
            Sequence = sequence;
            Timestamp = timestamp;
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
        public double Timestamp { get; }

        /// <summary>源容器 ID。</summary>
        public int SourceContainerId { get; }

        /// <summary>目标容器 ID。</summary>
        public int TargetContainerId { get; }

        /// <summary>源物品所在格的横坐标。</summary>
        public int SourceCellX { get; }

        /// <summary>源物品所在格的纵坐标。</summary>
        public int SourceCellY { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"InventoryQuickTransferIntent(P{PlayerId}, c{SourceContainerId}({SourceCellX},{SourceCellY}) " +
                   $"-> c{TargetContainerId})";
        }
    }

    /// <summary>就地把物品旋转 90 度。</summary>
    public readonly struct InventoryRotateIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "inventory.rotate";

        /// <summary>创建旋转意图。</summary>
        public InventoryRotateIntent(
            int playerId,
            int containerId,
            int cellX,
            int cellY,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            ContainerId = containerId;
            CellX = cellX;
            CellY = cellY;
            Sequence = sequence;
            Timestamp = timestamp;
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
        public double Timestamp { get; }

        /// <summary>物品所在容器 ID。</summary>
        public int ContainerId { get; }

        /// <summary>物品所在格的横坐标。</summary>
        public int CellX { get; }

        /// <summary>物品所在格的纵坐标。</summary>
        public int CellY { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"InventoryRotateIntent(P{PlayerId}, c{ContainerId}({CellX},{CellY}))";
        }
    }

    /// <summary>从一堆中拆出指定数量。</summary>
    public readonly struct InventorySplitIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "inventory.split";

        /// <summary>创建拆分意图。</summary>
        public InventorySplitIntent(
            int playerId,
            int containerId,
            int cellX,
            int cellY,
            int count,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            ContainerId = containerId;
            CellX = cellX;
            CellY = cellY;
            Count = count;
            Sequence = sequence;
            Timestamp = timestamp;
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
        public double Timestamp { get; }

        /// <summary>源堆所在容器 ID。</summary>
        public int ContainerId { get; }

        /// <summary>源堆所在格的横坐标。</summary>
        public int CellX { get; }

        /// <summary>源堆所在格的纵坐标。</summary>
        public int CellY { get; }

        /// <summary>要拆出的数量。</summary>
        public int Count { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"InventorySplitIntent(P{PlayerId}, c{ContainerId}({CellX},{CellY}), count={Count})";
        }
    }
}
