using RaidDemo.Kernel;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 金币余额发生变化时广播的事件。
    /// </summary>
    /// <remarks>
    /// 界面只订阅它刷新数字，不轮询 <see cref="MetaProgress.Money"/>。
    /// 与背包事件同一条原则：数据层不需要知道有没有界面。
    /// </remarks>
    public readonly struct WalletChangedEvent : IEventEnvelope
    {
        /// <summary>创建金币变更事件。</summary>
        public WalletChangedEvent(int money, int delta, string reason)
        {
            Money = money;
            Delta = delta;
            Reason = reason;
        }

        /// <summary>变更后的余额。</summary>
        public int Money { get; }

        /// <summary>本次变化量，收入为正、支出为负。</summary>
        public int Delta { get; }

        /// <summary>变化原因，用于调试与后续统计。</summary>
        public string Reason { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <inheritdoc />
        public string Source
        {
            get { return "meta.wallet"; }
        }

        /// <inheritdoc />
        public uint Sequence
        {
            get { return 0u; }
        }
    }

    /// <summary>任务状态或进度发生变化时广播的事件。</summary>
    public readonly struct QuestChangedEvent : IEventEnvelope
    {
        /// <summary>创建任务变更事件。</summary>
        /// <param name="questId">任务 ID；整批状态变化时可为空。</param>
        /// <param name="changeType">变化类型，见 <see cref="QuestChangeTypes"/>。</param>
        public QuestChangedEvent(string questId, string changeType)
        {
            QuestId = questId;
            ChangeType = changeType;
        }

        /// <summary>任务 ID。</summary>
        public string QuestId { get; }

        /// <summary>变化类型。</summary>
        public string ChangeType { get; }

        /// <inheritdoc />
        public double Timestamp
        {
            get { return 0d; }
        }

        /// <inheritdoc />
        public string Source
        {
            get { return "meta.quest"; }
        }

        /// <inheritdoc />
        public uint Sequence
        {
            get { return 0u; }
        }
    }

    /// <summary>任务变化类型常量。</summary>
    public static class QuestChangeTypes
    {
        /// <summary>任务被接取。</summary>
        public const string Accept = "accept";

        /// <summary>追踪目标发生变化。</summary>
        public const string Track = "track";

        /// <summary>进度推进。</summary>
        public const string Progress = "progress";

        /// <summary>达到完成条件，等待领奖。</summary>
        public const string Complete = "complete";

        /// <summary>奖励已领取。</summary>
        public const string Claim = "claim";

        /// <summary>上交物品完成。</summary>
        public const string TurnIn = "turn_in";

        /// <summary>整批任务状态被存档还原。</summary>
        public const string Restore = "restore";
    }
}
