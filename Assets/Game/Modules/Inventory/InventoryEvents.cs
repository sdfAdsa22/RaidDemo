using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 背包变更类型常量。
    /// </summary>
    /// <remarks>
    /// 用字符串常量而不是枚举，理由与 CommandCodes 一致：
    /// 后续里程碑（战局、商人、任务）可能新增变更类型，
    /// 字符串允许它们在自己的程序集里定义新常量，而不必回头修改背包层的枚举。
    /// </remarks>
    public static class InventoryChangeTypes
    {
        /// <summary>物品被放入容器。</summary>
        public const string Place = "place";

        /// <summary>物品被移出容器。</summary>
        public const string Remove = "remove";

        /// <summary>物品在容器之间或容器内移动。</summary>
        public const string Move = "move";

        /// <summary>物品并入已有堆。</summary>
        public const string Stack = "stack";

        /// <summary>堆被拆分。</summary>
        public const string Split = "split";

        /// <summary>容器被自动整理。</summary>
        public const string Sort = "sort";

        /// <summary>物品被装备。</summary>
        public const string Equip = "equip";

        /// <summary>装备被卸下。</summary>
        public const string Unequip = "unequip";
    }

    /// <summary>
    /// 容器内容发生变化时广播的事件。
    /// </summary>
    /// <remarks>
    /// <para>UI 只订阅它，不轮询容器。这样做的收益是双向的：
    /// 背包逻辑不需要知道有没有界面（无头服务端同样能跑），
    /// 界面也不需要每帧遍历所有格子。</para>
    ///
    /// <para>载荷刻意保持精简，只带容器 ID 与变更类型，界面收到后自行重新读取容器。
    /// 若把变更了哪几个格子也塞进来，就需要在所有规则分支上维护一份增量描述，
    /// 而那份描述没有任何玩法价值，却会成为最容易出错的地方。</para>
    /// </remarks>
    public readonly struct InventoryChangedEvent : IEventEnvelope
    {
        /// <summary>创建背包变更事件。</summary>
        /// <param name="containerId">发生变更的容器运行时 ID。</param>
        /// <param name="changeType">变更类型，取值见 <see cref="InventoryChangeTypes"/>。</param>
        /// <param name="playerId">发起变更的玩家，单机恒为 0。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号，本地事件为 0。</param>
        public InventoryChangedEvent(
            int containerId,
            string changeType,
            int playerId = 0,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            ContainerId = containerId;
            ChangeType = changeType;
            PlayerId = playerId;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>发生变更的容器运行时 ID。</summary>
        public int ContainerId { get; }

        /// <summary>变更类型，取值见 <see cref="InventoryChangeTypes"/>。</summary>
        public string ChangeType { get; }

        /// <summary>发起变更的玩家标识。</summary>
        public int PlayerId { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "inventory"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        public override string ToString()
        {
            return $"InventoryChangedEvent(container={ContainerId}, {ChangeType})";
        }
    }

    /// <summary>
    /// 负重状态发生变化时广播的事件。
    /// </summary>
    /// <remarks>
    /// 两个订阅方已经确定：HUD 用它显示负重条与状态提示；
    /// M4 的 AI 听觉用它判断玩家是否因为背得太满而动静变大。
    /// 事件只在状态真正改变时发布，而不是每帧发布。负重是个慢变量，
    /// 每帧广播只会让订阅方做无意义的重复工作。
    /// </remarks>
    public readonly struct EncumbranceChangedEvent : IEventEnvelope
    {
        /// <summary>创建负重变更事件。</summary>
        /// <param name="state">新的负重状态。</param>
        /// <param name="weightKg">当前总重量（千克）。</param>
        /// <param name="capacityKg">承载上限（千克）。</param>
        /// <param name="speedMultiplier">当前生效的速度倍率。</param>
        /// <param name="canSprint">当前是否允许奔跑。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号，本地事件为 0。</param>
        public EncumbranceChangedEvent(
            EncumbranceState state,
            float weightKg,
            float capacityKg,
            float speedMultiplier,
            bool canSprint,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            State = state;
            WeightKg = weightKg;
            CapacityKg = capacityKg;
            SpeedMultiplier = speedMultiplier;
            CanSprint = canSprint;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>新的负重状态。</summary>
        public EncumbranceState State { get; }

        /// <summary>当前总重量（千克）。</summary>
        public float WeightKg { get; }

        /// <summary>承载上限（千克）。</summary>
        public float CapacityKg { get; }

        /// <summary>当前生效的速度倍率。</summary>
        public float SpeedMultiplier { get; }

        /// <summary>当前是否允许奔跑。</summary>
        public bool CanSprint { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "inventory.encumbrance"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        public override string ToString()
        {
            return $"EncumbranceChangedEvent({State}, {WeightKg:F1}/{CapacityKg:F1}kg, speed={SpeedMultiplier:F2})";
        }
    }
}
