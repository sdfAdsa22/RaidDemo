using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面的布局与控件创建。
    /// </summary>
    /// <remarks>
    /// 与背包界面一致，全部用代码构建。灰盒阶段这样做最省事；
    /// 等 M7 换成预制体时，本文件整体替换，动作与规则部分不需要改动。
    /// </remarks>
    public sealed partial class MerchantScreenController
    {
        /// <summary>界面的三个页签。</summary>
        private enum MerchantTab
        {
            Buy = 0,
            Sell = 1,
            Quest = 2,
        }

        /// <summary>页签按钮。</summary>
        private sealed class TabWidget
        {
            public MerchantTab Tab;
            public RaidButtonWidget Button;
        }

        /// <summary>货架的一行。</summary>
        private sealed class ShopRowWidget
        {
            public TraderStockEntry Entry;
            public ItemDefinition Definition;
            public GameObject Root;
            public Text Label;
            public RaidButtonWidget Buy;
        }

        /// <summary>任务面板的一行。</summary>
        private sealed class QuestRowWidget
        {
            public QuestProgress Quest;
            public GameObject Root;
            public Text Title;
            public Text Objective;
            public Text Reward;
            public Text State;
            public RaidButtonWidget Action;
        }

        private readonly List<TabWidget> m_TabWidgets = new List<TabWidget>(3);
        private readonly List<ShopRowWidget> m_ShopRows = new List<ShopRowWidget>(12);
        private readonly List<QuestRowWidget> m_QuestRows = new List<QuestRowWidget>(8);

        private MerchantTab m_ActiveTab = MerchantTab.Buy;
        private RectTransform m_BuyTabRoot;
        private RectTransform m_SellTabRoot;
        private RectTransform m_QuestTabRoot;
        private Text m_SellSelectionLabel;
        private Text m_SellDetailLabel;
        private RaidButtonWidget m_SellButton;
        private GameObject m_ConfirmRoot;
        private Text m_ConfirmLabel;
        private RaidButtonWidget m_ConfirmButton;
        private RaidButtonWidget m_CancelButton;

        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            var canvas = RaidScreenFactory.CreateCanvas(transform, "MerchantCanvas", 210);
            var panel = RaidScreenFactory.CreatePanel(
                canvas, "Panel", new Vector2(PanelWidth, PanelHeight), PanelColor);
            m_Root = panel.gameObject;

            RaidScreenFactory.CreateLabel(
                panel, "商人 · 交易与任务",
                new Vector2(24f, 16f), new Vector2(600f, 30f),
                24, TextAnchor.MiddleLeft, TextColor);

            m_MoneyLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty,
                new Vector2(1000f, 16f), new Vector2(470f, 30f),
                24, TextAnchor.MiddleRight, MoneyColor);

            m_StashValueLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty,
                new Vector2(1000f, 46f), new Vector2(470f, 22f),
                15, TextAnchor.MiddleRight, DimColor);

            BuildTabs(panel);
            BuildBuyTab(panel);
            BuildSellTab(panel);
            BuildQuestTab(panel);
            BuildStashView(panel);
            BuildConfirmPanel(panel);

            m_StatusLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty,
                new Vector2(24f, 772f), new Vector2(1420f, 26f),
                16, TextAnchor.MiddleLeft, DimColor);

            ApplyTabVisibility();
        }

        /// <summary>顶部三个页签按钮。</summary>
        private void BuildTabs(RectTransform panel)
        {
            var captions = new[] { "购买", "出售", "任务" };
            for (var i = 0; i < captions.Length; i++)
            {
                var button = RaidScreenFactory.CreateButton(
                    panel,
                    captions[i],
                    new Vector2(24f + (i * 132f), 56f),
                    new Vector2(120f, 40f),
                    TabColor,
                    TabHoverColor);
                m_TabWidgets.Add(new TabWidget
                {
                    Tab = (MerchantTab)i,
                    Button = button,
                });
            }
        }

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

                var top = 120f + (m_ShopRows.Count * 54f);
                var row = CreateRowBackground(host, "ShopRow_" + entry.ItemId, top, 48f);
                var label = RaidScreenFactory.CreateLabel(
                    row, string.Empty,
                    new Vector2(12f, 0f), new Vector2(700f, 48f),
                    17, TextAnchor.MiddleLeft, TextColor);
                var buy = RaidScreenFactory.CreateButton(
                    row, "购买",
                    new Vector2(750f, 4f), new Vector2(94f, 40f),
                    ButtonColor, ButtonHoverColor);

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

        /// <summary>出售页：选中物品信息与出售按钮。</summary>
        private void BuildSellTab(RectTransform panel)
        {
            var host = CreateTabRoot(panel, "SellTab");
            m_SellTabRoot = host;

            RaidScreenFactory.CreateLabel(
                host, "在右侧仓库中左键选择物品，右键可直接出售。",
                new Vector2(0f, 70f), new Vector2(820f, 26f),
                16, TextAnchor.MiddleLeft, DimColor);

            m_SellSelectionLabel = RaidScreenFactory.CreateLabel(
                host, "未选择物品",
                new Vector2(0f, 110f), new Vector2(820f, 30f),
                20, TextAnchor.MiddleLeft, TextColor);

            m_SellDetailLabel = RaidScreenFactory.CreateLabel(
                host, string.Empty,
                new Vector2(0f, 148f), new Vector2(820f, 26f),
                16, TextAnchor.MiddleLeft, DimColor);

            m_SellButton = RaidScreenFactory.CreateButton(
                host, "出售选中物品",
                new Vector2(0f, 200f), new Vector2(180f, 46f),
                ButtonColor, ButtonHoverColor);
        }

        /// <summary>任务页：五个固定任务。</summary>
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
                var top = 120f + (i * 124f);
                var row = CreateRowBackground(host, "QuestRow_" + quests[i].Definition.Id, top, 114f);

                var title = RaidScreenFactory.CreateLabel(
                    row, string.Empty,
                    new Vector2(12f, 6f), new Vector2(600f, 26f),
                    19, TextAnchor.MiddleLeft, TextColor);
                var objective = RaidScreenFactory.CreateLabel(
                    row, string.Empty,
                    new Vector2(12f, 36f), new Vector2(600f, 24f),
                    15, TextAnchor.MiddleLeft, DimColor);
                var reward = RaidScreenFactory.CreateLabel(
                    row, string.Empty,
                    new Vector2(12f, 62f), new Vector2(600f, 24f),
                    15, TextAnchor.MiddleLeft, MoneyColor);
                var state = RaidScreenFactory.CreateLabel(
                    row, string.Empty,
                    new Vector2(12f, 88f), new Vector2(400f, 22f),
                    14, TextAnchor.MiddleLeft, DimColor);
                var action = RaidScreenFactory.CreateButton(
                    row, "接取",
                    new Vector2(690f, 34f), new Vector2(110f, 44f),
                    ButtonColor, ButtonHoverColor);

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

        /// <summary>右侧仓库网格。</summary>
        private void BuildStashView(RectTransform panel)
        {
            if (m_Registry == null || !m_Registry.TryGetGrid(m_StashContainerId, out var stash))
            {
                return;
            }

            var host = new GameObject("StashView");
            host.transform.SetParent(panel, worldPositionStays: false);
            m_StashView = host.AddComponent<InventoryGridView>();
            // 仓库网格从 84 像素开始：上方 16~76 留给金币与仓库总价值两行文字，
            // 56 像素起会让"仓库总价值"与网格标题叠在一起。
            m_StashView.Build(panel, stash, m_StashContainerId, "仓库（交易来源）", new Vector2(900f, 84f));
            m_StashView.Refresh();
        }

        /// <summary>高价值出售的确认面板。</summary>
        private void BuildConfirmPanel(RectTransform panel)
        {
            var host = new GameObject("ConfirmPanel", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(panel, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(560f, 220f);
            host.GetComponent<Image>().color = ConfirmPanelColor;

            m_ConfirmLabel = RaidScreenFactory.CreateLabel(
                rect, string.Empty,
                new Vector2(32f, 36f), new Vector2(496f, 80f),
                19, TextAnchor.UpperLeft, TextColor);

            m_ConfirmButton = RaidScreenFactory.CreateButton(
                rect, "确认出售",
                new Vector2(32f, 140f), new Vector2(220f, 52f),
                ButtonColor, ButtonHoverColor);
            m_CancelButton = RaidScreenFactory.CreateButton(
                rect, "取消",
                new Vector2(276f, 140f), new Vector2(220f, 52f),
                DisabledColor, TabHoverColor);

            m_ConfirmRoot = host;
            host.SetActive(false);
        }

        /// <summary>创建一个页签内容的根节点。</summary>
        private static RectTransform CreateTabRoot(RectTransform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(820f, 700f);
            return rect;
        }

        /// <summary>创建一行底色。</summary>
        private static RectTransform CreateRowBackground(
            RectTransform parent, string name, float top, float height)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(820f, height);
            host.GetComponent<Image>().color = RowColor;
            return rect;
        }
    }
}
