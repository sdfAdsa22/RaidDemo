using RaidDemo.Data;
using RaidDemo.Kernel;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 开始使用物品事件。
    /// </summary>
    /// <remarks>界面用它显示「正在使用 XXX」的读条。</remarks>
    public readonly struct ItemUseStartedEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        public ItemUseStartedEvent(ItemInstance item, string displayName, double timestamp = 0d, uint sequence = 0u)
        {
            Item = item;
            DisplayName = displayName;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>被使用的物品实例。</summary>
        public ItemInstance Item { get; }

        /// <summary>显示名。</summary>
        public string DisplayName { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.itemuse"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }
    }

    /// <summary>
    /// 物品使用进度事件。
    /// </summary>
    public readonly struct ItemUseProgressEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        public ItemUseProgressEvent(float progress01, double timestamp = 0d, uint sequence = 0u)
        {
            Progress01 = progress01;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>进度（0~1）。</summary>
        public float Progress01 { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.itemuse"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }
    }

    /// <summary>
    /// 物品使用完成事件。
    /// </summary>
    /// <remarks>
    /// <para>载荷直接带物品实例，而不是容器 ID 加格子坐标：读条期间玩家可能把背包整理一遍，
    /// 格子坐标会失效，而「到底用了哪一件」必须唯一确定——否则会出现
    /// 「用了绷带、扣掉的却是另一堆」这种极难复现的错误。</para>
    /// </remarks>
    public readonly struct ItemUseCompletedEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        public ItemUseCompletedEvent(ItemInstance item, double timestamp = 0d, uint sequence = 0u)
        {
            Item = item;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>被使用的物品实例。</summary>
        public ItemInstance Item { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.itemuse"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }
    }

    /// <summary>
    /// 物品使用被打断事件。
    /// </summary>
    /// <remarks>
    /// 打断**不消耗物品**：扣了东西又没回血是最让人恼火的一种失败，
    /// 而「受伤打断」本身已经是足够的代价。
    /// </remarks>
    public readonly struct ItemUseCanceledEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        public ItemUseCanceledEvent(ItemInstance item, string reason, double timestamp = 0d, uint sequence = 0u)
        {
            Item = item;
            Reason = reason;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>被打断的物品实例。</summary>
        public ItemInstance Item { get; }

        /// <summary>打断原因（中文，可直接展示）。</summary>
        public string Reason { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.itemuse"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }
    }
}
