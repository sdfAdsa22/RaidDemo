using System.Collections.Generic;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 固定任务表。
    /// </summary>
    /// <remarks>
    /// <para>任务奖励的定价与商人经济对齐：奖励如果低于把物品直接卖给商人，
    /// 玩家就没有理由上交，任务会立刻失去意义。因此燃料罐任务的奖励
    /// 必须高于一个燃料罐的收购价（8,250）。</para>
    ///
    /// <para>五个任务覆盖三条成长线：撤离（第一次成功）、战斗（击杀）、
    /// 搜刮（指定物品与单局价值）、生存（无伤撤离）。</para>
    /// </remarks>
    public static class QuestCatalog
    {
        /// <summary>第一个任务：所有玩家默认可见的起步目标。</summary>
        public const string FirstExtractId = "quest.first_extract";

        /// <summary>其余任务的共同前置。</summary>
        public const string ScavengerId = "quest.scavenger";

        public const string FuelRecoveryId = "quest.fuel_recovery";

        public const string FullHaulId = "quest.full_haul";

        public const string UntouchedId = "quest.untouched";

        /// <summary>生成默认任务表。</summary>
        public static List<QuestDefinition> CreateDefault()
        {
            return new List<QuestDefinition>(5)
            {
                new QuestDefinition(
                    FirstExtractId,
                    "首次撤离",
                    "带着这一局的收获活着离开。",
                    QuestObjectiveType.ExtractCount,
                    1,
                    rewardMoney: 5000),

                new QuestDefinition(
                    ScavengerId,
                    "清道夫",
                    "清理三名挡路的敌人。",
                    QuestObjectiveType.KillCount,
                    3,
                    rewardMoney: 8000,
                    rewardItemId: "ammo.5.45.standard",
                    rewardItemCount: 30,
                    prerequisiteQuestId: FirstExtractId),

                new QuestDefinition(
                    FuelRecoveryId,
                    "燃料回收",
                    "找到一罐燃料并交回安全屋。",
                    QuestObjectiveType.TurnInItem,
                    1,
                    targetItemId: "loot.canister.fuel",
                    rewardMoney: 15000,
                    rewardItemId: "ammo.5.45.standard",
                    rewardItemCount: 20,
                    prerequisiteQuestId: FirstExtractId),

                new QuestDefinition(
                    FullHaulId,
                    "满载而归",
                    "单局带出的战利品价值达到 15,000。",
                    QuestObjectiveType.SingleRaidLootValue,
                    15000,
                    rewardMoney: 10000,
                    prerequisiteQuestId: FirstExtractId),

                new QuestDefinition(
                    UntouchedId,
                    "毫发无伤",
                    "一枪未挨地成功撤离一次。",
                    QuestObjectiveType.NoDamageExtract,
                    1,
                    rewardMoney: 8000,
                    rewardItemId: "medical.kit.field",
                    rewardItemCount: 1,
                    prerequisiteQuestId: FirstExtractId),
            };
        }
    }
}
