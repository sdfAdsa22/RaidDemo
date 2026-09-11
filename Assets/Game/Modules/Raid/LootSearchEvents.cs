using RaidDemo.Kernel;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 搜刮读条进度变化事件。
    /// </summary>
    /// <remarks>
    /// 界面用它画读条。进度变化很频繁（每帧都有变化），
    /// 但读条本来就是每帧都要重绘的东西，多一条事件不会带来额外负担。
    /// </remarks>
    public readonly struct LootSearchProgressEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        /// <param name="containerId">被搜刮的容器 ID。</param>
        /// <param name="progress01">进度（0~1）。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public LootSearchProgressEvent(
            int containerId,
            float progress01,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            ContainerId = containerId;
            Progress01 = progress01;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>容器 ID。</summary>
        public int ContainerId { get; }

        /// <summary>进度（0~1）。</summary>
        public float Progress01 { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.loot"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"LootSearchProgressEvent(容器={ContainerId}, 进度={Progress01:P0})";
        }
    }

    /// <summary>
    /// 搜刮完成事件：这个容器现在可以打开了。
    /// </summary>
    /// <remarks>
    /// 装配层订阅它并打开对应的容器面板。把「读条完成」与「打开界面」分开，
    /// 逻辑层就不需要知道界面存在。
    /// </remarks>
    public readonly struct LootSearchCompletedEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        /// <param name="containerId">被搜刮的容器 ID。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public LootSearchCompletedEvent(
            int containerId,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            ContainerId = containerId;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>容器 ID。</summary>
        public int ContainerId { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.loot"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"LootSearchCompletedEvent(容器={ContainerId})";
        }
    }

    /// <summary>
    /// 搜刮被打断事件。
    /// </summary>
    /// <remarks>
    /// <para>带上中断原因（移动 / 阵亡）而不是只报「取消了」：
    /// 排查玩家反馈时，「为什么我的读条总是断」这个问题的答案就是原因字段本身。</para>
    /// </remarks>
    public readonly struct LootSearchCanceledEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        /// <param name="containerId">被打断的容器 ID。</param>
        /// <param name="reason">中断原因（中文，直接可展示）。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public LootSearchCanceledEvent(
            int containerId,
            string reason,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            ContainerId = containerId;
            Reason = reason;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>容器 ID。</summary>
        public int ContainerId { get; }

        /// <summary>中断原因。</summary>
        public string Reason { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.loot"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"LootSearchCanceledEvent(容器={ContainerId}, 原因={Reason})";
        }
    }
}
