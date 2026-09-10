namespace RaidDemo.Shared
{
    /// <summary>自动整理指定容器。</summary>
    public readonly struct InventorySortIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "inventory.sort";

        /// <summary>创建整理意图。</summary>
        public InventorySortIntent(
            int playerId,
            int containerId,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            ContainerId = containerId;
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

        /// <summary>要整理的容器 ID。</summary>
        public int ContainerId { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"InventorySortIntent(P{PlayerId}, c{ContainerId})";
        }
    }

    /// <summary>把容器里的物品装备到槽位。</summary>
    public readonly struct InventoryEquipIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "inventory.equip";

        /// <summary>创建装备意图。</summary>
        public InventoryEquipIntent(
            int playerId,
            int containerId,
            int cellX,
            int cellY,
            EquipmentSlot slot,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            ContainerId = containerId;
            CellX = cellX;
            CellY = cellY;
            Slot = slot;
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

        /// <summary>目标槽位。</summary>
        public EquipmentSlot Slot { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"InventoryEquipIntent(P{PlayerId}, c{ContainerId}({CellX},{CellY}) -> {Slot})";
        }
    }

    /// <summary>把装备槽里的物品卸回背包。</summary>
    public readonly struct InventoryUnequipIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "inventory.unequip";

        /// <summary>创建卸下意图。</summary>
        public InventoryUnequipIntent(
            int playerId,
            EquipmentSlot slot,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            Slot = slot;
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

        /// <summary>要卸下的槽位。</summary>
        public EquipmentSlot Slot { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"InventoryUnequipIntent(P{PlayerId}, {Slot})";
        }
    }
}
