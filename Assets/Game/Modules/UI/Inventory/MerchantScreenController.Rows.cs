using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Meta;
using TMPro;
using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面的列表部分：货架（购买页）与任务卡（任务页）。
    /// </summary>
    /// <remarks>
    /// <para>从 Layout 部分拆出来的原因与背包界面一致：布局文件超过了项目规定的 400 行上限。
    /// 这一部分只负责"一行长什么样、放在哪"，页签切换与按钮输入仍在 Actions 部分。</para>
    /// <para>行与行之间靠<b>固定行距</b>排布而不是自动布局组件：列表项数量固定（货架最多 12 项、
    /// 任务 5 项），自动布局反而会引入一个需要维护的额外组件。</para>
    /// </remarks>
    public sealed partial class MerchantScreenController
    {
        /// <summary>货架行高与行距。</summary>
        /// <remarks>行距 48 是按最坏情况反推的：货架最多 12 项，12 × 48 = 576，
        /// 加上起始高度 144 之后是 720，仍给底部提示条（754 起）留出空隙。</remarks>
        private const float ShopRowHeight = 42f;

        private const float ShopRowPitch = 48f;

        /// <summary>任务卡高与行距。五个任务 × 118 = 590，同样不会压到提示条。</summary>
        private const float QuestRowHeight = 110f;

        private const float QuestRowPitch = 118f;

        /// <summary>购买页：货架列表。</summary>
        private void BuildBuyTab(RectTransform panel)
        {
            var host = CreateTabRoot(panel, "BuyTab");
            m_BuyTabRoot = host;

            if (m_Trader == null)
            {
                return;
            }

            var entries = m_Trader.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var definition = m_Catalog != null ? m_Catalog.Get(entry.ItemId) : null;
                if (definition == null)
                {
                    continue;
                }

                var row = CreateRowBackground(
                    host,
                    "ShopRow_" + entry.ItemId,
                    m_ShopRows.Count * ShopRowPitch,
                    ShopRowHeight);

                var label = UiFactory.CreateLabel(
                    row,
                    string.Empty,
                    new Vector2(14f, 0f),
                    new Vector2(LeftWidth - 140f, ShopRowHeight),
                    UiPalette.BodySize,
                    TextAlignmentOptions.Left,
                    UiPalette.Ink);

                var buy = UiFactory.CreateButton(
                    row,
                    "购买",
                    new Vector2(LeftWidth - 116f, 4f),
                    new Vector2(102f, ShopRowHeight - 8f));

                m_ShopRows.Add(new ShopRowWidget
                {
                    Entry = entry,
                    Definition = definition,
                    Root = row.gameObject,
                    Label = label,
                    Buy = buy,
                });
            }
        }

        /// <summary>任务页：固定任务卡片。</summary>
        private void BuildQuestTab(RectTransform panel)
        {
            var host = CreateTabRoot(panel, "QuestTab");
            m_QuestTabRoot = host;

            if (m_Progress == null || m_Progress.Quests == null)
            {
                return;
            }

            var quests = m_Progress.Quests.Quests;
            for (var i = 0; i < quests.Count; i++)
            {
                var row = CreateRowBackground(
                    host,
                    "QuestRow_" + quests[i].Definition.Id,
                    i * QuestRowPitch,
                    QuestRowHeight);

                var title = UiFactory.CreateLabel(
                    row, string.Empty, new Vector2(14f, 6f), new Vector2(LeftWidth - 180f, 26f),
                    20f, TextAlignmentOptions.Left, UiPalette.Ink);
                var objective = UiFactory.CreateLabel(
                    row, string.Empty, new Vector2(14f, 34f), new Vector2(LeftWidth - 180f, 22f),
                    UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
                var reward = UiFactory.CreateLabel(
                    row, string.Empty, new Vector2(14f, 58f), new Vector2(LeftWidth - 180f, 22f),
                    UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.Money);
                var state = UiFactory.CreateLabel(
                    row, string.Empty, new Vector2(14f, 82f), new Vector2(360f, 20f),
                    UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkDisabled);
                var action = UiFactory.CreateButton(
                    row, "接取",
                    new Vector2(LeftWidth - 132f, 32f),
                    new Vector2(118f, 46f));

                m_QuestRows.Add(new QuestRowWidget
                {
                    Quest = quests[i],
                    Root = row.gameObject,
                    Title = title,
                    Objective = objective,
                    Reward = reward,
                    State = state,
                    Action = action,
                });
            }
        }

    }
}
