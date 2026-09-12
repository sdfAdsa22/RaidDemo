using RaidDemo.Meta;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局里的任务进度接线：击杀、受伤与 HUD 追踪。
    /// </summary>
    /// <remarks>
    /// 任务规则在 <see cref="QuestSystem"/> 里，本文件只负责把战局事件喂给它，
    /// 并把最新追踪文本写进 HUD。拆出来的原因是主战局文件已经接近行数上限，
    /// 而任务接线与掉落、撤离逻辑本来就是两件事。
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>本局玩家是否受过任何伤害。任务「毫发无伤」用它判定。</summary>
        private bool m_PlayerTookDamageThisRaid;

        private MetaProgress m_MetaProgress;

        /// <summary>创建战局界面。</summary>
        private void BuildRaidHud()
        {
            var host = new GameObject("RaidHud");
            host.transform.SetParent(transform, worldPositionStays: false);
            m_RaidHud = host.AddComponent<RaidHudView>();
            m_RaidHud.Initialize();
            m_RaidHud.SetKills(0);
        }

        /// <summary>战局开始时挂上任务追踪并写入一次当前状态。</summary>
        private void InitializeQuestTracking()
        {
            m_PlayerTookDamageThisRaid = false;
            if (m_MetaProgress == null)
            {
                return;
            }

            m_MetaProgress.Changed += RefreshQuestTracker;
            RefreshQuestTracker();
        }

        /// <summary>把当前追踪任务写进战局 HUD。</summary>
        private void RefreshQuestTracker()
        {
            if (m_RaidHud == null || m_MetaProgress == null)
            {
                return;
            }

            m_RaidHud.SetQuestTracker(m_MetaProgress.Quests.BuildTrackerText());
        }

        /// <summary>登记一次玩家击杀。由伤害事件调用。</summary>
        private void NotifyQuestKill()
        {
            m_MetaProgress?.Quests.NotifyKill();
        }

        /// <summary>标记玩家本局受过伤。</summary>
        private void MarkPlayerDamagedForQuests()
        {
            m_PlayerTookDamageThisRaid = true;
        }
    }
}
