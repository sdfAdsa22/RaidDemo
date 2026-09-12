namespace RaidDemo.Meta
{
    /// <summary>
    /// 一个任务的运行时进度。
    /// </summary>
    /// <remarks>
    /// 定义与进度分开：定义是只读的共享数据，进度是每个存档独立的一份。
    /// 这样以后增加"重复任务"时，同一份定义可以挂多份进度，而不必复制定义。
    /// </remarks>
    public sealed class QuestProgress
    {
        /// <summary>创建一个默认未解锁的任务进度。</summary>
        public QuestProgress(QuestDefinition definition)
        {
            Definition = definition;
            State = QuestState.Locked;
        }

        /// <summary>静态定义。</summary>
        public QuestDefinition Definition { get; }

        /// <summary>当前状态。</summary>
        public QuestState State { get; internal set; }

        /// <summary>当前进度。不同目标类型的单位不同（次、人、金币）。</summary>
        public int Current { get; internal set; }

        /// <summary>是否已经达到目标数量。上交类任务在物品被上交时直接置为完成。</summary>
        public bool IsGoalReached
        {
            get { return Current >= Definition.TargetCount; }
        }
    }
}
