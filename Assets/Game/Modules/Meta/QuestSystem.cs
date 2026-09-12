using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 任务系统：固定任务的接取、进度、上交、领奖与解锁。
    /// </summary>
    /// <remarks>
    /// <para><b>它是纯逻辑层。</b>不引用场景、不读输入、不直接画界面；
    /// 所有改变都通过公共方法发生，界面只调用方法并订阅变更事件。
    /// 因此任务状态机可以在 EditMode 测试里完整跑一遍。</para>
    ///
    /// <para>任务进度与仓库放在同一个跨场景对象上（<see cref="MetaProgress"/>），
    /// 因为二者是同一份存档的两部分：任务要检查仓库里的上交物品，
    /// 奖励也要放进同一个仓库。</para>
    /// </remarks>
    public sealed partial class QuestSystem
    {
        /// <summary>同时可进行的任务上限。</summary>
        public const int MaxActiveQuests = 3;

        private readonly List<QuestProgress> m_Quests;
        private readonly Dictionary<string, QuestProgress> m_Lookup;
        private readonly InventoryGrid m_Stash;
        private readonly ItemFactory m_Factory;
        private IItemDefinitionLookup m_Catalog;
        private readonly Action<int> m_AddMoney;
        private readonly Action<string> m_OnChanged;

        /// <summary>创建任务系统并生成默认任务状态。</summary>
        /// <param name="stash">仓库网格，用于检查上交物品与发放物品奖励。</param>
        /// <param name="factory">物品工厂。</param>
        /// <param name="catalog">物品目录，用于把稳定 ID 还原成定义。</param>
        /// <param name="addMoney">发放金币奖励的回调。</param>
        /// <param name="onChanged">任务发生变化时的回调，参数为任务 ID，批量变化时为 null。</param>
        public QuestSystem(
            InventoryGrid stash,
            ItemFactory factory,
            IItemDefinitionLookup catalog,
            Action<int> addMoney,
            Action<string> onChanged)
        {
            m_Stash = stash;
            m_Factory = factory ?? new ItemFactory();
            m_Catalog = catalog;
            m_AddMoney = addMoney;
            m_OnChanged = onChanged;

            var definitions = QuestCatalog.CreateDefault();
            m_Quests = new List<QuestProgress>(definitions.Count);
            m_Lookup = new Dictionary<string, QuestProgress>(definitions.Count, StringComparer.Ordinal);
            for (var i = 0; i < definitions.Count; i++)
            {
                var progress = new QuestProgress(definitions[i]);
                m_Quests.Add(progress);
                m_Lookup[definitions[i].Id] = progress;
            }

            RefreshAvailability();
        }

        /// <summary>全部任务，顺序与任务表一致。</summary>
        public IReadOnlyList<QuestProgress> Quests
        {
            get { return m_Quests; }
        }

        /// <summary>当前被追踪的任务 ID；没有追踪时为空。</summary>
        public string TrackedQuestId { get; private set; }

        /// <summary>按 ID 取任务进度。找不到返回 null。</summary>
        public QuestProgress Get(string questId)
        {
            if (string.IsNullOrEmpty(questId))
            {
                return null;
            }

            return m_Lookup.TryGetValue(questId, out var progress) ? progress : null;
        }

        /// <summary>接取一个可接取的任务。</summary>
        public bool TryAccept(string questId, out string problem)
        {
            var quest = Get(questId);
            if (quest == null)
            {
                problem = "找不到这个任务。";
                return false;
            }

            if (quest.State != QuestState.Available)
            {
                problem = quest.State == QuestState.Locked ? "前置任务还没有完成。" : "这个任务已经接过了。";
                return false;
            }

            if (CountActive() >= MaxActiveQuests)
            {
                problem = $"同时最多进行 {MaxActiveQuests} 个任务，先完成一个再来接取。";
                return false;
            }

            quest.State = QuestState.Active;
            if (string.IsNullOrEmpty(TrackedQuestId))
            {
                TrackedQuestId = quest.Definition.Id;
            }

            problem = null;
            NotifyChanged(quest.Definition.Id);
            return true;
        }

        /// <summary>把某个进行中的任务设为追踪目标。</summary>
        public bool TryTrack(string questId, out string problem)
        {
            var quest = Get(questId);
            if (quest == null)
            {
                problem = "找不到这个任务。";
                return false;
            }

            if (quest.State != QuestState.Active && quest.State != QuestState.Completed)
            {
                problem = "只有进行中的任务可以被追踪。";
                return false;
            }

            TrackedQuestId = quest.Definition.Id;
            problem = null;
            NotifyChanged(quest.Definition.Id);
            return true;
        }

        /// <summary>仓库里指定物品的总数量。上交类任务界面用它显示进度。</summary>
        public int CountInStash(string itemId)
        {
            if (m_Stash == null || string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            var total = 0;
            var items = m_Stash.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].Definition.Id, itemId, StringComparison.Ordinal))
                {
                    total += items[i].StackCount;
                }
            }

            return total;
        }

        /// <summary>生成目标说明，例如「击杀敌人 1/3」。</summary>
        public string BuildObjectiveText(QuestProgress quest)
        {
            if (quest == null)
            {
                return string.Empty;
            }

            var def = quest.Definition;
            switch (def.ObjectiveType)
            {
                case QuestObjectiveType.ExtractCount:
                    return $"成功撤离 {quest.Current}/{def.TargetCount} 次";
                case QuestObjectiveType.KillCount:
                    return $"击杀敌人 {quest.Current}/{def.TargetCount}";
                case QuestObjectiveType.SingleRaidLootValue:
                    return $"单局带出价值 ≥ {def.TargetCount:N0}（当前 {quest.Current:N0}）";
                case QuestObjectiveType.NoDamageExtract:
                    return $"未受伤撤离 {quest.Current}/{def.TargetCount}";
                case QuestObjectiveType.TurnInItem:
                    return $"上交 {ResolveItemName(def.TargetItemId)} {quest.Current}/{def.TargetCount}";
                default:
                    return string.Empty;
            }
        }

        /// <summary>生成奖励说明。</summary>
        public string BuildRewardText(QuestProgress quest)
        {
            if (quest == null)
            {
                return string.Empty;
            }

            var def = quest.Definition;
            var reward = def.RewardMoney > 0 ? $"金币 {def.RewardMoney:N0}" : string.Empty;
            if (!string.IsNullOrEmpty(def.RewardItemId) && def.RewardItemCount > 0)
            {
                var itemText = $"{ResolveItemName(def.RewardItemId)} x{def.RewardItemCount}";
                reward = string.IsNullOrEmpty(reward) ? itemText : $"{reward} + {itemText}";
            }

            return string.IsNullOrEmpty(reward) ? "无奖励" : reward;
        }

        /// <summary>任务状态的中文名。</summary>
        public static string GetStateText(QuestState state)
        {
            switch (state)
            {
                case QuestState.Locked:
                    return "未解锁";
                case QuestState.Available:
                    return "可接取";
                case QuestState.Active:
                    return "进行中";
                case QuestState.Completed:
                    return "可领取";
                case QuestState.Claimed:
                    return "已完成";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 刷新任务解锁状态。
        /// </summary>
        /// <remarks>
        /// 只做"前置已完成 → 解锁"这一个方向的转换，不会反向把已接取的任务锁回去。
        /// 单向转换保证任务状态在任何操作顺序下都稳定。
        /// </remarks>
        private void RefreshAvailability()
        {
            for (var i = 0; i < m_Quests.Count; i++)
            {
                var quest = m_Quests[i];
                if (quest.State != QuestState.Locked)
                {
                    continue;
                }

                var prerequisite = quest.Definition.PrerequisiteQuestId;
                if (string.IsNullOrEmpty(prerequisite))
                {
                    quest.State = QuestState.Available;
                    continue;
                }

                var prerequisiteQuest = Get(prerequisite);
                if (prerequisiteQuest != null && prerequisiteQuest.State == QuestState.Claimed)
                {
                    quest.State = QuestState.Available;
                }
            }
        }

        private void NotifyChanged(string questId)
        {
            m_OnChanged?.Invoke(questId);
        }

        /// <summary>统计进行中的任务数量。</summary>
        private int CountActive()
        {
            var count = 0;
            for (var i = 0; i < m_Quests.Count; i++)
            {
                if (m_Quests[i].State == QuestState.Active)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 注入物品目录。
        /// </summary>
        /// <remarks>
        /// 局外进度在场景加载前就会创建，那时还拿不到场景里的目录资产，
        /// 因此目录必须在装配阶段补上。任务本身的接取与进度不需要目录，
        /// 只有上交与发奖需要，所以延迟注入不会影响状态机启动。
        /// </remarks>
        internal void AttachCatalog(IItemDefinitionLookup catalog)
        {
            m_Catalog = catalog;
        }

        private string ResolveItemName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || m_Catalog == null)
            {
                return string.IsNullOrEmpty(itemId) ? "物品" : itemId;
            }

            return m_Catalog.TryGet(itemId, out var definition)
                ? definition.DisplayName
                : itemId;
        }
    }
}
