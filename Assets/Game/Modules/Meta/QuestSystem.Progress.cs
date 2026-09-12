using System;
using System.Collections.Generic;
using System.Text;
using RaidDemo.Data;
using RaidDemo.Inventory;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 任务系统的进度推进部分：击杀、撤离、上交与领奖。
    /// </summary>
    public sealed partial class QuestSystem
    {
        /// <summary>登记一次玩家击杀。</summary>
        /// <returns>是否有任务进度因此改变。</returns>
        public bool NotifyKill()
        {
            var changed = false;
            for (var i = 0; i < m_Quests.Count; i++)
            {
                var quest = m_Quests[i];
                if (quest.State != QuestState.Active
                    || quest.Definition.ObjectiveType != QuestObjectiveType.KillCount)
                {
                    continue;
                }

                changed |= Advance(quest, quest.Current + 1);
            }

            if (changed)
            {
                NotifyChanged(null);
            }

            return changed;
        }

        /// <summary>一局结束时登记撤离结果。</summary>
        /// <param name="extractedValue">本局带出的战利品总价值。</param>
        /// <param name="tookDamage">本局玩家是否受过任何伤害。</param>
        /// <returns>是否有任务进度因此改变。</returns>
        public bool ReportExtraction(int extractedValue, bool tookDamage)
        {
            var changed = false;
            for (var i = 0; i < m_Quests.Count; i++)
            {
                var quest = m_Quests[i];
                if (quest.State != QuestState.Active)
                {
                    continue;
                }

                switch (quest.Definition.ObjectiveType)
                {
                    case QuestObjectiveType.ExtractCount:
                        changed |= Advance(quest, quest.Current + 1);
                        break;

                    case QuestObjectiveType.SingleRaidLootValue:
                        changed |= Advance(quest, Math.Max(quest.Current, extractedValue));
                        break;

                    case QuestObjectiveType.NoDamageExtract:
                        if (!tookDamage)
                        {
                            changed |= Advance(quest, quest.Current + 1);
                        }

                        break;
                }
            }

            if (changed)
            {
                NotifyChanged(null);
            }

            return changed;
        }

        /// <summary>上交任务要求的物品。</summary>
        public bool TryTurnIn(string questId, out string problem)
        {
            var quest = Get(questId);
            if (quest == null)
            {
                problem = "找不到这个任务。";
                return false;
            }

            if (quest.State != QuestState.Active
                || quest.Definition.ObjectiveType != QuestObjectiveType.TurnInItem)
            {
                problem = "这个任务不需要上交物品。";
                return false;
            }

            var itemId = quest.Definition.TargetItemId;
            var required = quest.Definition.TargetCount;
            if (CountInStash(itemId) < required)
            {
                problem = $"仓库里的{ResolveItemName(itemId)}不够（需要 {required} 个）。";
                return false;
            }

            if (!TryConsumeFromStash(itemId, required))
            {
                problem = "扣除仓库物品失败，请检查仓库空间。";
                return false;
            }

            quest.Current = quest.Definition.TargetCount;
            quest.State = QuestState.Completed;
            problem = null;
            NotifyChanged(quest.Definition.Id);
            return true;
        }

        /// <summary>领取已完成任务的奖励。</summary>
        public bool TryClaim(string questId, out string problem)
        {
            var quest = Get(questId);
            if (quest == null)
            {
                problem = "找不到这个任务。";
                return false;
            }

            if (quest.State != QuestState.Completed)
            {
                problem = "任务还没有完成，或者奖励已经领过了。";
                return false;
            }

            var rewardItem = CreateRewardItem(quest.Definition, out var rewardProblem);
            if (rewardProblem != null)
            {
                problem = rewardProblem;
                return false;
            }

            // 先检查空间再发钱：否则仓库满时会出现「钱到手了、物品没地方放」的半完成状态。
            if (rewardItem != null && (m_Stash == null || !m_Stash.CanAutoPlace(rewardItem)))
            {
                problem = "仓库空间不足，先腾出一格再领取奖励。";
                return false;
            }

            if (quest.Definition.RewardMoney > 0)
            {
                m_AddMoney?.Invoke(quest.Definition.RewardMoney);
            }

            if (rewardItem != null)
            {
                var placed = m_Stash.AutoPlace(rewardItem);
                if (!placed.Success)
                {
                    problem = "奖励发放失败：仓库空间不足。";
                    return false;
                }
            }

            quest.State = QuestState.Claimed;
            RefreshAvailability();
            if (string.Equals(TrackedQuestId, quest.Definition.Id, StringComparison.Ordinal))
            {
                TrackedQuestId = FindNextTrackableId();
            }

            problem = null;
            NotifyChanged(quest.Definition.Id);
            return true;
        }

        /// <summary>生成战局界面显示的任务追踪文本；没有可显示任务时返回 null。</summary>
        public string BuildTrackerText()
        {
            var quest = Get(TrackedQuestId);
            if (quest == null)
            {
                quest = FindFirstActive();
            }

            if (quest == null)
            {
                return null;
            }

            if (quest.State == QuestState.Completed)
            {
                return $"任务：{quest.Definition.Title} · 返回安全屋领取奖励";
            }

            return $"任务：{quest.Definition.Title}　{BuildObjectiveText(quest)}";
        }

        /// <summary>生成结算界面的任务进度摘要；没有可显示内容时返回 null。</summary>
        public string BuildRaidSummary()
        {
            var builder = new StringBuilder();
            var lines = 0;
            for (var i = 0; i < m_Quests.Count && lines < 4; i++)
            {
                var quest = m_Quests[i];
                if (quest.State != QuestState.Active && quest.State != QuestState.Completed)
                {
                    continue;
                }

                if (lines > 0)
                {
                    builder.Append("　｜　");
                }

                builder.Append(quest.Definition.Title);
                builder.Append(' ');
                builder.Append(quest.State == QuestState.Completed
                    ? "可领取"
                    : BuildObjectiveText(quest));
                lines++;
            }

            return lines == 0 ? null : "任务进度：" + builder;
        }

        /// <summary>
        /// 推进一个任务的进度，并在达到目标时转为「可领取」。
        /// </summary>
        private static bool Advance(QuestProgress quest, int value)
        {
            var clamped = Math.Min(Math.Max(0, value), quest.Definition.TargetCount);
            var previousValue = quest.Current;
            var previousState = quest.State;
            quest.Current = clamped;
            if (quest.State == QuestState.Active && quest.IsGoalReached)
            {
                quest.State = QuestState.Completed;
            }

            return quest.Current != previousValue || quest.State != previousState;
        }

        /// <summary>创建任务奖励物品；没有物品奖励时返回 null。</summary>
        private ItemInstance CreateRewardItem(QuestDefinition definition, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(definition.RewardItemId) || definition.RewardItemCount <= 0)
            {
                return null;
            }

            IItemDefinition itemDefinition = null;
            if (m_Catalog == null
                || !m_Catalog.TryGet(definition.RewardItemId, out itemDefinition))
            {
                problem = $"奖励物品 {definition.RewardItemId} 不存在，任务奖励无法发放。";
                return null;
            }

            if (definition.RewardItemCount > itemDefinition.MaxStack)
            {
                problem = $"奖励数量超过该物品的堆叠上限（{itemDefinition.MaxStack}）。";
                return null;
            }

            return m_Factory.Create(itemDefinition, definition.RewardItemCount);
        }

        /// <summary>
        /// 从仓库扣除指定数量的物品。
        /// </summary>
        /// <remarks>
        /// 调用前必须先用 <see cref="CountInStash"/> 确认总量足够。
        /// 需要拆堆时，剩余部分会放回**原来的格子**——该格刚被清空，
        /// 而剩余堆的占地不会比原堆更大，因此放回一定成功。
        /// </remarks>
        private bool TryConsumeFromStash(string itemId, int count)
        {
            if (m_Stash == null || count <= 0)
            {
                return false;
            }

            var remaining = count;
            var snapshot = new List<ItemInstance>(m_Stash.Items);
            for (var i = 0; i < snapshot.Count && remaining > 0; i++)
            {
                var item = snapshot[i];
                if (!string.Equals(item.Definition.Id, itemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (item.StackCount <= remaining)
                {
                    if (m_Stash.Remove(item).Success)
                    {
                        remaining -= item.StackCount;
                    }

                    continue;
                }

                // 需要从这堆里只扣一部分：先记录位置，再把剩余部分放回原格。
                m_Stash.TryGetOrigin(item, out var origin);
                var rotated = item.Rotated;
                if (!m_Stash.Remove(item).Success)
                {
                    return false;
                }

                var rest = m_Factory.Create(item.Definition, item.StackCount - remaining);
                var placed = m_Stash.Place(rest, origin, rotated);
                if (!placed.Success)
                {
                    m_Stash.AutoPlace(rest);
                }

                remaining = 0;
            }

            return remaining == 0;
        }

        private QuestProgress FindFirstActive()
        {
            for (var i = 0; i < m_Quests.Count; i++)
            {
                if (m_Quests[i].State == QuestState.Active)
                {
                    return m_Quests[i];
                }
            }

            return null;
        }

        private string FindNextTrackableId()
        {
            for (var i = 0; i < m_Quests.Count; i++)
            {
                var quest = m_Quests[i];
                if (quest.State == QuestState.Active || quest.State == QuestState.Completed)
                {
                    return quest.Definition.Id;
                }
            }

            return null;
        }
    }
}
