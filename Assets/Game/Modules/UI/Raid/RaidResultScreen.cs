using System;
using System.Collections.Generic;
using RaidDemo.Raid;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 战局结算界面：回答「这一局我贪对了还是贪错了」。
    /// </summary>
    /// <remarks>
    /// <para><b>结算的重点是收支对比，而不是物品清单。</b>清单只回答「我拿了什么」，
    /// 而玩家真正要做的判断是「为了这些东西冒的险值不值」。
    /// 因此界面上「带入」与「带出」两个数字并列显示，且用颜色表达盈亏。</para>
    ///
    /// <para>阵亡与超时都会如实列出损失价值。M5 阶段这些损失并不会真正扣除物品
    /// （仓库要等 M6），界面里会写明这一点，避免把「暂时不做」伪装成「已经做了」。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RaidResultScreen : MonoBehaviour
    {
        private const int MaxItemRows = 9;

        private static readonly Color OverlayColor = new Color(0.04f, 0.05f, 0.07f, 0.90f);
        private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.13f, 0.98f);
        private static readonly Color TitleColor = new Color(0.96f, 0.94f, 0.88f);
        private static readonly Color BodyColor = new Color(0.82f, 0.83f, 0.87f);
        private static readonly Color HintColor = new Color(0.60f, 0.61f, 0.66f);
        private static readonly Color ProfitColor = new Color(0.36f, 0.85f, 0.52f);
        private static readonly Color LossColor = new Color(0.95f, 0.42f, 0.36f);
        private static readonly Color SuccessColor = new Color(0.36f, 0.85f, 0.52f);
        private static readonly Color FailureColor = new Color(0.95f, 0.35f, 0.30f);
        private static readonly Color TimeoutColor = new Color(0.95f, 0.72f, 0.30f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.46f, 0.80f);
        private static readonly Color ButtonHoverColor = new Color(0.26f, 0.58f, 0.94f);
        private static readonly Color SecondaryButtonColor = new Color(0.24f, 0.25f, 0.28f);
        private static readonly Color SecondaryHoverColor = new Color(0.32f, 0.33f, 0.37f);

        private readonly List<Text> m_ItemRows = new List<Text>(MaxItemRows);

        private RectTransform m_Root;
        private Text m_TitleLabel;
        private Text m_SummaryLabel;
        private Text m_BroughtLabel;
        private Text m_ExtractedLabel;
        private Text m_ListHeaderLabel;
        private Text m_ListFooterLabel;
        private Text m_NoteLabel;
        private RaidButtonWidget m_RestartButton;
        private RaidButtonWidget m_MenuButton;
        private Action m_OnRestart;
        private Action m_OnReturnToMenu;
        private bool m_IsVisible;

        /// <summary>构建界面。</summary>
        /// <param name="onRestart">点击「再来一局」时执行的回调。</param>
        /// <param name="onReturnToMenu">点击「返回主菜单」时执行的回调。</param>
        public void Initialize(Action onRestart, Action onReturnToMenu)
        {
            m_OnRestart = onRestart;
            m_OnReturnToMenu = onReturnToMenu;

            m_Root = RaidScreenFactory.CreateCanvas(transform, "RaidResultCanvas", 310);
            RaidScreenFactory.CreatePanel(
                m_Root,
                "Overlay",
                new Vector2(RaidScreenFactory.ReferenceWidth, RaidScreenFactory.ReferenceHeight),
                OverlayColor);

            var panel = RaidScreenFactory.CreatePanel(m_Root, "Panel", new Vector2(900f, 680f), PanelColor);

            m_TitleLabel = RaidScreenFactory.CreateLabel(
                panel, "战局结束", new Vector2(48f, 32f), new Vector2(800f, 52f),
                42, TextAnchor.MiddleLeft, TitleColor);

            m_SummaryLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty, new Vector2(48f, 100f), new Vector2(800f, 30f),
                19, TextAnchor.MiddleLeft, BodyColor);

            m_BroughtLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty, new Vector2(48f, 148f), new Vector2(380f, 40f),
                26, TextAnchor.MiddleLeft, HintColor);

            m_ExtractedLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty, new Vector2(440f, 148f), new Vector2(420f, 40f),
                26, TextAnchor.MiddleLeft, ProfitColor);

            m_ListHeaderLabel = RaidScreenFactory.CreateLabel(
                panel, "带出的物品", new Vector2(48f, 216f), new Vector2(800f, 26f),
                17, TextAnchor.MiddleLeft, HintColor);

            for (var i = 0; i < MaxItemRows; i++)
            {
                var row = RaidScreenFactory.CreateLabel(
                    panel, string.Empty, new Vector2(48f, 250f + (i * 26f)), new Vector2(800f, 24f),
                    17, TextAnchor.MiddleLeft, BodyColor);
                m_ItemRows.Add(row);
            }

            m_ListFooterLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty, new Vector2(48f, 250f + (MaxItemRows * 26f)), new Vector2(800f, 24f),
                16, TextAnchor.MiddleLeft, HintColor);

            m_NoteLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty, new Vector2(48f, 540f), new Vector2(800f, 26f),
                15, TextAnchor.MiddleLeft, HintColor);

            m_RestartButton = RaidScreenFactory.CreateButton(
                panel, "再来一局（Enter）", new Vector2(48f, 588f), new Vector2(320f, 58f),
                ButtonColor, ButtonHoverColor);

            m_MenuButton = RaidScreenFactory.CreateButton(
                panel, "返回安全屋（Esc）", new Vector2(392f, 588f), new Vector2(320f, 58f),
                SecondaryButtonColor, SecondaryHoverColor);

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
        public void Show(RaidResult result)
        {
            if (result == null)
            {
                return;
            }

            m_TitleLabel.text = ResolveOutcomeTitle(result.Outcome);
            m_TitleLabel.color = ResolveOutcomeColor(result.Outcome);
            m_SummaryLabel.text =
                $"存活 {FormatDuration(result.ElapsedSeconds)}   ｜   击杀 {result.Kills}";

            m_BroughtLabel.text = $"带入 {result.BroughtInValue:N0}";
            var profit = result.ExtractedValue - result.BroughtInValue;
            var failed = result.Outcome != RaidOutcome.Extracted;
            m_ExtractedLabel.text = failed
                ? $"损失 {result.LostValue:N0}"
                : $"带出 {result.ExtractedValue:N0}（{(profit >= 0 ? "+" : string.Empty)}{profit:N0}）";
            m_ExtractedLabel.color = failed
                ? LossColor
                : profit >= 0 ? ProfitColor : TimeoutColor;

            var items = failed ? result.LostItems : result.ExtractedItems;
            m_ListHeaderLabel.text = failed ? "损失清单" : "带出的物品";
            FillItemRows(items);

            m_NoteLabel.text = failed
                ? "随身携带的装备与物资已全部丢失；仓库里的物品不受影响。"
                : "带出的物品已存入仓库，可在出击准备界面查看。";

            SetVisible(true);
        }

        /// <summary>把物品清单写进固定的行里。</summary>
        private void FillItemRows(IReadOnlyList<RaidResultEntry> items)
        {
            var count = items != null ? items.Count : 0;
            for (var i = 0; i < m_ItemRows.Count; i++)
            {
                var row = m_ItemRows[i];
                if (items == null || i >= count)
                {
                    row.text = string.Empty;
                    continue;
                }

                var entry = items[i];
                var amount = entry.Count > 1 ? $" x{entry.Count}" : string.Empty;
                row.text = $"{entry.DisplayName}{amount}   单价 {entry.UnitValue:N0}   合计 {entry.TotalValue:N0}";
            }

            m_ListFooterLabel.text = count > MaxItemRows
                ? $"…还有 {count - MaxItemRows} 件未显示"
                : count == 0 ? "（没有带走任何东西）" : string.Empty;
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

        /// <summary>结果标题对应的颜色。</summary>
        private static Color ResolveOutcomeColor(RaidOutcome outcome)
        {
            switch (outcome)
            {
                case RaidOutcome.Extracted:
                    return SuccessColor;
                case RaidOutcome.Killed:
                    return FailureColor;
                case RaidOutcome.TimeExpired:
                    return TimeoutColor;
                default:
                    return TitleColor;
            }
        }

        /// <summary>轮询两个按钮与快捷键。</summary>
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

            if (keyboard != null && keyboard.enterKey.wasPressedThisFrame)
            {
                m_OnRestart?.Invoke();
                return;
            }

            if (Mouse.current == null)
            {
                return;
            }

            var pointer = Mouse.current.position.ReadValue();
            var overRestart = m_RestartButton.Contains(pointer);
            var overMenu = m_MenuButton.Contains(pointer);
            m_RestartButton.SetHovered(overRestart);
            m_MenuButton.SetHovered(overMenu);

            if (!Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (overRestart)
            {
                m_OnRestart?.Invoke();
            }
            else if (overMenu)
            {
                m_OnReturnToMenu?.Invoke();
            }
        }
    }
}
