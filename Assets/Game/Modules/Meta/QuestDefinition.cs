namespace RaidDemo.Meta
{
    /// <summary>
    /// 任务目标类型。
    /// </summary>
    /// <remarks>
    /// 只保留五种能用现有战局数据判定的目标。新增类型时必须同时提供
    /// "进度从哪里来"与"如何存档"两个答案，否则只会留下永远无法完成的任务。
    /// </remarks>
    public enum QuestObjectiveType
    {
        /// <summary>累计成功撤离次数。</summary>
        ExtractCount = 0,

        /// <summary>累计击杀敌人数量。</summary>
        KillCount = 1,

        /// <summary>单局带出价值达到阈值。</summary>
        SingleRaidLootValue = 2,

        /// <summary>在一局未受伤的情况下成功撤离。</summary>
        NoDamageExtract = 3,

        /// <summary>向任务面板上交指定物品。</summary>
        TurnInItem = 4,
    }

    /// <summary>
    /// 任务状态。
    /// </summary>
    public enum QuestState
    {
        /// <summary>前置任务尚未完成，不可接取。</summary>
        Locked = 0,

        /// <summary>可以接取。</summary>
        Available = 1,

        /// <summary>进行中。</summary>
        Active = 2,

        /// <summary>目标已达成，等待领取奖励。</summary>
        Completed = 3,

        /// <summary>奖励已领取，任务结束。</summary>
        Claimed = 4,
    }

    /// <summary>
    /// 一个固定任务的静态定义。
    /// </summary>
    /// <remarks>
    /// 批次 4 明确不做随机任务：固定任务足够展示任务系统的状态机、
    /// 进度来源、奖励结算与存档，而随机任务会把验证重点从系统转移到内容量。
    /// </remarks>
    public sealed class QuestDefinition
    {
        /// <summary>创建一个任务定义。</summary>
        public QuestDefinition(
            string id,
            string title,
            string description,
            QuestObjectiveType objectiveType,
            int targetCount,
            string targetItemId = null,
            int rewardMoney = 0,
            string rewardItemId = null,
            int rewardItemCount = 0,
            string prerequisiteQuestId = null)
        {
            Id = id;
            Title = title;
            Description = description;
            ObjectiveType = objectiveType;
            TargetCount = targetCount < 1 ? 1 : targetCount;
            TargetItemId = targetItemId;
            RewardMoney = rewardMoney < 0 ? 0 : rewardMoney;
            RewardItemId = rewardItemId;
            RewardItemCount = rewardItemCount < 0 ? 0 : rewardItemCount;
            PrerequisiteQuestId = prerequisiteQuestId;
        }

        /// <summary>稳定 ID，存档只记录它。</summary>
        public string Id { get; }

        /// <summary>任务标题。</summary>
        public string Title { get; }

        /// <summary>一句话说明。</summary>
        public string Description { get; }

        /// <summary>目标类型。</summary>
        public QuestObjectiveType ObjectiveType { get; }

        /// <summary>需要达到的数量。</summary>
        public int TargetCount { get; }

        /// <summary>上交类任务需要的物品 ID。</summary>
        public string TargetItemId { get; }

        /// <summary>金币奖励。</summary>
        public int RewardMoney { get; }

        /// <summary>物品奖励 ID，可为空。</summary>
        public string RewardItemId { get; }

        /// <summary>物品奖励数量。</summary>
        public int RewardItemCount { get; }

        /// <summary>前置任务 ID，可为空。</summary>
        public string PrerequisiteQuestId { get; }
    }
}
