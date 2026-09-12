using System;
using System.Collections.Generic;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 任务系统的存档部分：只保存状态与进度，不保存任务定义。
    /// </summary>
    public sealed partial class QuestSystem
    {
        /// <summary>把全部任务状态整理成可序列化的记录。</summary>
        public QuestSaveRecord[] CaptureState()
        {
            var records = new QuestSaveRecord[m_Quests.Count];
            for (var i = 0; i < m_Quests.Count; i++)
            {
                var quest = m_Quests[i];
                records[i] = new QuestSaveRecord
                {
                    questId = quest.Definition.Id,
                    state = (int)quest.State,
                    progress = quest.Current,
                };
            }

            return records;
        }

        /// <summary>
        /// 从存档记录还原任务状态。
        /// </summary>
        /// <param name="records">任务记录，可为 null。</param>
        /// <param name="trackedQuestId">追踪的任务 ID。</param>
        /// <param name="problems">记录无法还原的条目。未知 ID 会被跳过而不是让读档失败。</param>
        public void RestoreState(
            IReadOnlyList<QuestSaveRecord> records,
            string trackedQuestId,
            List<string> problems)
        {
            for (var i = 0; i < m_Quests.Count; i++)
            {
                m_Quests[i].State = QuestState.Locked;
                m_Quests[i].Current = 0;
            }

            if (records != null)
            {
                for (var i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    if (record == null)
                    {
                        continue;
                    }

                    var quest = Get(record.questId);
                    if (quest == null)
                    {
                        problems?.Add($"存档里的任务 {record.questId} 在当前版本中不存在，已跳过。");
                        continue;
                    }

                    if (record.state < (int)QuestState.Locked
                        || record.state > (int)QuestState.Claimed)
                    {
                        problems?.Add($"任务 {record.questId} 的状态值非法（{record.state}），已跳过。");
                        continue;
                    }

                    quest.State = (QuestState)record.state;
                    quest.Current = Math.Min(
                        Math.Max(0, record.progress),
                        quest.Definition.TargetCount);
                }
            }

            // 前置任务已完成但依赖任务仍是锁定时，补一次解锁。
            RefreshAvailability();

            var tracked = Get(trackedQuestId);
            TrackedQuestId = tracked != null
                && (tracked.State == QuestState.Active || tracked.State == QuestState.Completed)
                    ? tracked.Definition.Id
                    : null;

            if (string.IsNullOrEmpty(TrackedQuestId))
            {
                var fallback = FindFirstActive();
                TrackedQuestId = fallback?.Definition.Id;
            }

            NotifyChanged(null);
        }
    }
}
