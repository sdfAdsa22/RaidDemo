using RaidDemo.Data;
using RaidDemo.Meta;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面的动作部分：把鼠标操作翻译成命令，并刷新显示。
    /// </summary>
    public sealed partial class MerchantScreenController
    {
        /// <summary>重画全部内容。</summary>
        private void RefreshAll()
        {
            if (m_Progress != null)
            {
                if (m_MoneyLabel != null)
                {
                    m_MoneyLabel.text = $"金币 {m_Progress.Money:N0}";
                }

                if (m_StashValueLabel != null)
                {
                    m_StashValueLabel.text = $"仓库总价值 {m_Progress.TotalStashValue:N0}";
                }
            }

            m_StashView?.Refresh();
            RefreshShopRows();
            RefreshQuestRows();
            RefreshSellPanel();
        }

        /// <summary>按当前页签显示对应内容。</summary>
        private void ApplyTabVisibility()
        {
            if (m_BuyTabRoot != null)
            {
                m_BuyTabRoot.gameObject.SetActive(m_ActiveTab == MerchantTab.Buy);
            }

            if (m_SellTabRoot != null)
            {
                m_SellTabRoot.gameObject.SetActive(m_ActiveTab == MerchantTab.Sell);
            }

            if (m_QuestTabRoot != null)
            {
                m_QuestTabRoot.gameObject.SetActive(m_ActiveTab == MerchantTab.Quest);
            }
        }

        /// <summary>刷新货架文字：名称、单价与一组总价。</summary>
        private void RefreshShopRows()
        {
            for (var i = 0; i < m_ShopRows.Count; i++)
            {
                var row = m_ShopRows[i];
                var bundle = Mathf.Max(1, row.Entry.BundleCount);
                var unit = TraderPricing.GetBuyPrice(row.Definition);
                var total = TraderPricing.GetBuyPrice(row.Definition, bundle);
                row.Label.text = bundle > 1
                    ? $"{row.Definition.DisplayName}　单价 {unit:N0}　一组 {bundle} 个 = {total:N0}"
                    : $"{row.Definition.DisplayName}　单价 {total:N0}";
            }
        }

        /// <summary>刷新任务行：进度、奖励与按钮文字。</summary>
        private void RefreshQuestRows()
        {
            var quests = m_Progress != null ? m_Progress.Quests : null;
            if (quests == null)
            {
                return;
            }

            for (var i = 0; i < m_QuestRows.Count; i++)
            {
                var row = m_QuestRows[i];
                var quest = row.Quest;
                row.Title.text = quest.Definition.Title;
                row.Objective.text = BuildObjectiveText(quests, quest);
                row.Reward.text = "奖励：" + quests.BuildRewardText(quest);
                row.State.text = QuestSystem.GetStateText(quest.State);
                row.Action.Label.text = ResolveQuestActionCaption(quest);
                row.Action.Rect.gameObject.SetActive(
                    quest.State == QuestState.Available
                    || quest.State == QuestState.Active
                    || quest.State == QuestState.Completed);
            }
        }

        /// <summary>
        /// 任务目标文字。
        /// </summary>
        /// <remarks>
        /// 上交类任务额外显示仓库现有数量：玩家不需要切回背包界面
        /// 就能知道"现在能不能交"。
        /// </remarks>
        private static string BuildObjectiveText(QuestSystem quests, QuestProgress quest)
        {
            var text = quests.BuildObjectiveText(quest);
            if (quest.Definition.ObjectiveType == QuestObjectiveType.TurnInItem
                && quest.State == QuestState.Active)
            {
                var owned = quests.CountInStash(quest.Definition.TargetItemId);
                text += $"（仓库现有 {owned}）";
            }

            return text;
        }

        private static string ResolveQuestActionCaption(QuestProgress quest)
        {
            switch (quest.State)
            {
                case QuestState.Available:
                    return "接取";
                case QuestState.Active:
                    return quest.Definition.ObjectiveType == QuestObjectiveType.TurnInItem
                        ? "上交"
                        : "追踪";
                case QuestState.Completed:
                    return "领取";
                case QuestState.Claimed:
                    return "已完成";
                default:
                    return "未解锁";
            }
        }

        /// <summary>页签悬停与点击。</summary>
        private void UpdateTabInput(Vector2 pointer)
        {
            for (var i = 0; i < m_TabWidgets.Count; i++)
            {
                var tab = m_TabWidgets[i];
                var hovered = tab.Button.Contains(pointer);
                tab.Button.Background.color = hovered
                    ? TabHoverColor
                    : tab.Tab == m_ActiveTab ? TabActiveColor : TabColor;

                if (!hovered || !Mouse.current.leftButton.wasPressedThisFrame)
                {
                    continue;
                }

                m_ActiveTab = tab.Tab;
                ApplyTabVisibility();
                RefreshAll();
                return;
            }
        }

        /// <summary>购买按钮与快捷键。</summary>
        private void UpdateBuyInput(Vector2 pointer)
        {
            for (var i = 0; i < m_ShopRows.Count; i++)
            {
                var row = m_ShopRows[i];
                var hovered = row.Buy.Contains(pointer);
                row.Buy.SetHovered(hovered);
                if (!hovered || !Mouse.current.leftButton.wasPressedThisFrame)
                {
                    continue;
                }

                var result = m_Router.Dispatch(new BuyItemIntent(
                    0, row.Entry.ItemId, Mathf.Max(1, row.Entry.BundleCount)));
                ShowStatus(
                    result.Success
                        ? $"已购买 {row.Definition.DisplayName} x{Mathf.Max(1, row.Entry.BundleCount)}。"
                        : result.Message,
                    result.Success);
                return;
            }
        }

        /// <summary>任务按钮。</summary>
        private void UpdateQuestInput(Vector2 pointer)
        {
            for (var i = 0; i < m_QuestRows.Count; i++)
            {
                var row = m_QuestRows[i];
                if (!row.Action.Rect.gameObject.activeSelf)
                {
                    continue;
                }

                var hovered = row.Action.Contains(pointer);
                row.Action.SetHovered(hovered);
                if (!hovered || !Mouse.current.leftButton.wasPressedThisFrame)
                {
                    continue;
                }

                ExecuteQuestAction(row.Quest);
                return;
            }
        }

        /// <summary>按任务状态派发对应命令。</summary>
        private void ExecuteQuestAction(QuestProgress quest)
        {
            if (m_Router == null || quest == null)
            {
                return;
            }

            CommandResult result;
            switch (quest.State)
            {
                case QuestState.Available:
                    result = m_Router.Dispatch(new QuestAcceptIntent(0, quest.Definition.Id));
                    break;

                case QuestState.Active when quest.Definition.ObjectiveType == QuestObjectiveType.TurnInItem:
                    result = m_Router.Dispatch(new QuestTurnInIntent(0, quest.Definition.Id));
                    break;

                case QuestState.Active:
                    result = m_Router.Dispatch(new QuestTrackIntent(0, quest.Definition.Id));
                    break;

                case QuestState.Completed:
                    result = m_Router.Dispatch(new QuestClaimIntent(0, quest.Definition.Id));
                    break;

                default:
                    ShowStatus("这个任务当前不可操作。", false);
                    return;
            }

            if (result.Success)
            {
                ShowStatus($"任务「{quest.Definition.Title}」操作成功。", true);
            }
            else
            {
                ShowStatus(result.Message, false);
            }
        }

    }
}
