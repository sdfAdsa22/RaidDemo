using System;
using System.Collections.Generic;
using RaidDemo.Raid;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 战局结算界面：回答「这一局我贪对了还是贪错了」。
    /// </summary>
    /// <remarks>
    /// <para><b>结算的重点是收支对比，而不是物品清单。</b>清单只回答「我拿了什么」，
    /// 而玩家真正要做的判断是「为了这些东西冒的险值不值」。
    /// 因此界面上「带入」与「带出 / 损失」两个数字并列显示，且用颜色表达盈亏。</para>
    ///
    /// <para>阵亡与超时都会如实列出损失价值。M5 阶段这些损失并不会真正扣除物品
    /// （仓库要等 M6），界面里会写明这一点，避免把「暂时不做」伪装成「已经做了」。</para>
    ///
    /// <para><b>M7 批次 4 换皮：</b>从 <c>RaidScreenFactory</c>（纯色方板 + 旧版 Text）
    /// 迁到 <c>UiFactory</c>。排版集中在 <c>RaidResultScreen.Layout.cs</c>，
    /// 本文件只保留"数据怎么变成界面上的字与颜色"这一半，两部分各自都读得完。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class RaidResultScreen : MonoBehaviour
    {
        /// <summary>清单最多显示的行数。</summary>
        private const int MaxItemRows = 9;

        private readonly List<ResultRow> m_ItemRows = new List<ResultRow>(MaxItemRows);

        private RectTransform m_Root;
        private TextMeshProUGUI m_TitleLabel;
        private TextMeshProUGUI m_SummaryLabel;
        private TextMeshProUGUI m_BroughtLabel;
        private TextMeshProUGUI m_ExtractedLabel;
        private TextMeshProUGUI m_ListHeaderLabel;
        private TextMeshProUGUI m_ListFooterLabel;
        private TextMeshProUGUI m_QuestLabel;
        private TextMeshProUGUI m_NoteLabel;
        private UiButton m_MenuButton;
        private Action m_OnReturnToMenu;
        private bool m_IsVisible;

        /// <summary>构建界面。</summary>
        /// <param name="onReturnToMenu">点击「返回安全屋」时执行的回调。</param>
        public void Initialize(Action onReturnToMenu)
        {
            m_OnReturnToMenu = onReturnToMenu;
            BuildLayout();
            SetVisible(false);
        }

        /// <summary>显示或隐藏结算界面。</summary>
        public void SetVisible(bool visible)
        {
            m_IsVisible = visible;
            if (m_Root != null)
            {
                m_Root.gameObject.SetActive(visible);
            }
        }

        /// <summary>用一份战局结算数据刷新界面。</summary>
        /// <param name="result">结算数据。</param>
        /// <param name="questSummary">任务进度摘要；为空时隐藏该行。</param>
        public void Show(RaidResult result, string questSummary = null)
        {
            if (result == null)
            {
                return;
            }

            m_TitleLabel.text = ResolveOutcomeTitle(result.Outcome);
            m_TitleLabel.color = ResolveOutcomeColor(result.Outcome);
            m_SummaryLabel.text = $"存活 {FormatDuration(result.ElapsedSeconds)}   ｜   击杀 {result.Kills}";

            m_BroughtLabel.text = $"带入 {result.BroughtInValue:N0}";

            // 阵亡与超时看的是「损失」，撤离看的是「带出 + 盈亏」：
            // 同一个位置回答两个问题，比再加一行数字更清楚。
            var profit = result.ExtractedValue - result.BroughtInValue;
            var failed = result.Outcome != RaidOutcome.Extracted;
            m_ExtractedLabel.text = failed
                ? $"损失 {result.LostValue:N0}"
                : $"带出 {result.ExtractedValue:N0}（{(profit >= 0 ? "+" : string.Empty)}{profit:N0}）";
            m_ExtractedLabel.color = failed
                ? UiPalette.Bad
                : profit >= 0
                    ? UiPalette.Ok
                    : UiPalette.Warn;

            var items = failed ? result.LostItems : result.ExtractedItems;
            m_ListHeaderLabel.text = failed ? "损失清单" : "带出的物品";
            FillItemRows(items);

            m_NoteLabel.text = failed
                ? "随身携带的装备与物资已全部丢失；仓库里的物品不受影响。"
                : "带出的物品已存入仓库，可在出击准备界面查看。";

            var hasQuest = !string.IsNullOrEmpty(questSummary);
            m_QuestLabel.gameObject.SetActive(hasQuest);
            m_QuestLabel.text = hasQuest ? questSummary : string.Empty;

            SetVisible(true);
        }

        /// <summary>把物品清单写进固定的行里。</summary>
        /// <remarks>
        /// 行对象在构建时一次性建好（行数固定为 <see cref="MaxItemRows"/>），每次结算只改文字与图标。
        /// 每局的清单长度不同，但重建行会让结算界面在打开的瞬间抖一下——那一帧正好是玩家最注意画面的时候。
        /// </remarks>
        private void FillItemRows(IReadOnlyList<RaidResultEntry> items)
        {
            var count = items != null ? items.Count : 0;
            for (var i = 0; i < m_ItemRows.Count; i++)
            {
                var row = m_ItemRows[i];
                if (items == null || i >= count)
                {
                    row.Root.SetActive(false);
                    continue;
                }

                var entry = items[i];
                row.Root.SetActive(true);
                row.Name.text = entry.DisplayName;
                row.Count.text = entry.Count > 1 ? $"x{entry.Count}" : string.Empty;
                row.Unit.text = $"单价 {entry.UnitValue:N0}";
                row.Total.text = $"合计 {entry.TotalValue:N0}";

                // 物品行只用稀有度色小方块，不再画分类图标：
                // 分类图标在背包/商人/结算里都容易与物品名争夺视线，
                // 而稀有度本身就是结算清单里更需要一眼看出的信息。
                row.Chip.color = UiPalette.ItemOutline(entry.Rarity);
            }

            m_ListFooterLabel.text = count > MaxItemRows
                ? $"…还有 {count - MaxItemRows} 件未显示"
                : count == 0
                    ? "（没有带走任何东西）"
                    : string.Empty;
        }

        /// <summary>把秒数格式化成 分:秒。</summary>
        private static string FormatDuration(float seconds)
        {
            var total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        /// <summary>结果标题。</summary>
        private static string ResolveOutcomeTitle(RaidOutcome outcome)
        {
            switch (outcome)
            {
                case RaidOutcome.Extracted:
                    return "撤离成功";
                case RaidOutcome.Killed:
                    return "阵亡";
                case RaidOutcome.TimeExpired:
                    return "时间耗尽";
                default:
                    return "战局结束";
            }
        }

        /// <summary>
        /// 结果标题对应的颜色。
        /// </summary>
        /// <remarks>用主题层的三个状态色，而不是本文件自带的颜色常量：
        /// 撤离成功必须是"全游戏通用的那个绿"，阵亡也必须是"全游戏通用的那个红"，
        /// 否则结算界面的绿和血条旁的绿会是两个不同的绿。</remarks>
        private static Color ResolveOutcomeColor(RaidOutcome outcome)
        {
            switch (outcome)
            {
                case RaidOutcome.Extracted:
                    return UiPalette.Ok;
                case RaidOutcome.Killed:
                    return UiPalette.Bad;
                case RaidOutcome.TimeExpired:
                    return UiPalette.Warn;
                default:
                    return UiPalette.Ink;
            }
        }

        /// <summary>轮询唯一按钮与 Esc。</summary>
        /// <remarks>
        /// 结算之后只有一条去处（回安全屋），因此这里没有"最后点过哪个按钮"的状态：
        /// Esc 与点击走同一个回调，玩家按哪个键都只会发生一件事。
        /// </remarks>
        private void Update()
        {
            if (!m_IsVisible)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                m_OnReturnToMenu?.Invoke();
                return;
            }

            var mouse = Mouse.current;
            var pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            var overMenu = mouse != null && m_MenuButton.Contains(pointer);
            m_MenuButton.SetHovered(overMenu);
            m_MenuButton.ApplyVisual(overMenu && mouse != null && mouse.leftButton.isPressed);

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && overMenu)
            {
                m_OnReturnToMenu?.Invoke();
            }
        }
    }
}
