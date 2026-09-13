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
            RefreshSellControls();
        }

        /// <summary>按当前页签显示对应内容。</summary>
        private void ApplyTabVisibility()
        {
            if (m_BuyTabRoot != null)
            {
                m_BuyTabRoot.gameObject.SetActive(m_ActiveTab == MerchantTab.Buy);
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
                // 当前页签用主按钮的青绿底，其它页签用白底：
                // "选中"与"悬停"因此是两种不同的信号，不会互相冒充。
                tab.Button.SetVariant(tab.Tab == m_ActiveTab ? UiButtonKind.Primary : UiButtonKind.Normal);
                tab.Button.SetHovered(hovered);

                if (!hovered || !Mouse.current.leftButton.wasPressedThisFrame)
                {
                    continue;
                }

                if (m_ActiveTab == tab.Tab)
                {
                    return;
                }

                m_ActiveTab = tab.Tab;
                ApplyTabVisibility();
                RefreshAll();
                UiAudio.Play(UiCue.TabSwitch);
                return;
            }
        }

        /// <summary>购买按钮与快捷键。</summary>
        private void UpdateBuyInput(Vector2 pointer)
        {
            // 货架先吃滚轮：鼠标停在哪一行都能滚，不需要把指针放到滚动条上。
            UpdateShopScroll(Mouse.current.scroll.ReadValue().y);

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
                UiAudio.Play(result.Success ? UiCue.Buy : UiCue.Locked);
                return;
            }
        }

        /// <summary>
        /// 按滚轮输入滚动货架。也支持键盘上下键（滚轮坏掉或笔记本触控板不好用时仍可操作）。
        /// </summary>
        /// <param name="wheelDelta">本帧滚轮输入（Windows 一格约 ±120）。</param>
        /// <remarks>
        /// <para>偏移量的方向：内容原点在左上、Y 轴向上为正，因此"向下滚动"是让内容的
        /// <c>anchoredPosition.y</c> 增大。</para>
        /// <para>边界必须夹住：不夹的话可以一路把内容推出视口，界面会变成一片空白，
        /// 而且没有任何东西提示"滚过头了"。</para>
        /// </remarks>
        private void UpdateShopScroll(float wheelDelta)
        {
            if (m_ShopContent == null)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // 方向键按帧推进：±30 单位 ≈ 每帧 12 像素（60 帧下约 720 像素/秒），
                // 与滚轮一格 48 像素的手感接近；给 240 会一帧滚过一整屏。
                if (keyboard.downArrowKey.isPressed)
                {
                    wheelDelta -= 30f;
                }
                else if (keyboard.upArrowKey.isPressed)
                {
                    wheelDelta += 30f;
                }
            }

            if (Mathf.Approximately(wheelDelta, 0f))
            {
                return;
            }

            var maxScroll = Mathf.Max(0f, m_ShopContentHeight - ShopViewportHeight + 8f);
            var target = Mathf.Clamp(
                m_ShopScroll - (wheelDelta * ShopScrollPixelsPerUnit), 0f, maxScroll);
            if (Mathf.Approximately(target, m_ShopScroll))
            {
                return;
            }

            m_ShopScroll = target;
            m_ShopContent.anchoredPosition = new Vector2(0f, m_ShopScroll);
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
