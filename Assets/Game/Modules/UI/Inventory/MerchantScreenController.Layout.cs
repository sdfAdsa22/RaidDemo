using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面的布局与控件创建（M7 批次 4 换皮）。
    /// </summary>
    /// <remarks>
    /// <para><b>版式：左操作、右家底。</b>左栏是页签与页签内容（购买 / 任务），
    /// 右侧始终显示仓库网格——玩家在买与卖之间切换时看到的都是同一份家底，
    /// 不必来回开关两个界面核对仓位。出售入口就挂在仓库正下方。</para>
    ///
    /// <para>与背包界面共用同一套主题：奶油纸面、九宫格面板、厚片按钮、TMP 中文。
    /// 两个界面的面板尺寸与留白规则刻意保持一致（28 边距、64/72 高的标题条），
    /// 这样从背包切到商人时不会有"换了一套 UI"的割裂感。</para>
    /// </remarks>
    public sealed partial class MerchantScreenController
    {
        /// <summary>界面的页签。</summary>
        private enum MerchantTab
        {
            Buy = 0,
            Quest = 1,
        }

        /// <summary>页签按钮。</summary>
        private sealed class TabWidget
        {
            public MerchantTab Tab;
            public UiButton Button;
        }

        /// <summary>货架的一行。</summary>
        private sealed class ShopRowWidget
        {
            public TraderStockEntry Entry;
            public ItemDefinition Definition;
            public GameObject Root;
            public TextMeshProUGUI Label;
            public UiButton Buy;
        }

        /// <summary>任务面板的一行。</summary>
        private sealed class QuestRowWidget
        {
            public QuestProgress Quest;
            public GameObject Root;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI Objective;
            public TextMeshProUGUI Reward;
            public TextMeshProUGUI State;
            public UiButton Action;
        }

        private readonly List<TabWidget> m_TabWidgets = new List<TabWidget>(3);
        private readonly List<ShopRowWidget> m_ShopRows = new List<ShopRowWidget>(12);
        private readonly List<QuestRowWidget> m_QuestRows = new List<QuestRowWidget>(8);

        private MerchantTab m_ActiveTab = MerchantTab.Buy;
        private RectTransform m_BuyTabRoot;
        private RectTransform m_QuestTabRoot;
        private TextMeshProUGUI m_SellInfoLabel;
        private UiButton m_SellToggleButton;
        private UiButton m_SellCancelButton;
        private GameObject m_SellMenuRoot;
        private UiButton m_SellMenuButton;
        private UiButton m_SellMenuCancelButton;
        private GameObject m_ConfirmRoot;
        private TextMeshProUGUI m_ConfirmLabel;
        private UiButton m_ConfirmButton;
        private UiButton m_CancelButton;

        /// <summary>内容区左边距。</summary>
        private const float Margin = 28f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 72f;

        /// <summary>左栏宽度（页签与列表）。</summary>
        private const float LeftWidth = 820f;

        /// <summary>右栏（仓库网格）的横坐标。</summary>
        private const float RightX = 880f;

        /// <summary>创建页签内容根节点时用的尺寸（够放下最长的列表）。</summary>
        private static readonly Vector2 TabRootSize = new Vector2(LeftWidth, 640f);

        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            var canvas = UiFactory.CreateCanvas(transform, "MerchantCanvas", 210);
            UiFactory.CreateVeil(canvas, "Veil");

            var panel = UiFactory.CreateCenteredPanel(
                canvas, "Panel", new Vector2(PanelWidth, PanelHeight), UiSprites.Card);
            m_Root = panel.gameObject;

            BuildTitleBar(panel);
            BuildTabs(panel);
            BuildBuyTab(panel);
            BuildQuestTab(panel);
            BuildStashView(panel);
            BuildSellControls(panel);
            BuildSellContextMenu(panel);
            BuildConfirmPanel(panel);

            m_StatusLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Margin, PanelHeight - 46f),
                new Vector2(PanelWidth - (Margin * 2f), 26f),
                UiPalette.BodySize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            ApplyTabVisibility();
        }

        /// <summary>标题条：界面名 + 金币 + 仓库总价值。</summary>
        private void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(
                panel, "TitleBar",
                new Vector2(PanelWidth, TitleBarHeight),
                UiSprites.CardDim,
                Vector2.zero);

            UiFactory.CreateLabel(
                panel,
                "商人 · 交易与任务",
                new Vector2(Margin, 20f),
                new Vector2(600f, 36f),
                26f,
                TextAlignmentOptions.Left,
                UiPalette.Ink);

            m_MoneyLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(PanelWidth - 340f, 16f),
                new Vector2(312f, 34f),
                24f,
                TextAlignmentOptions.Right,
                UiPalette.Money);

            m_StashValueLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(PanelWidth - 340f, 46f),
                new Vector2(312f, 22f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Right,
                UiPalette.InkSoft);
        }

        /// <summary>顶部两个页签。</summary>
        private void BuildTabs(RectTransform panel)
        {
            var captions = new[] { "购买", "任务" };
            for (var i = 0; i < captions.Length; i++)
            {
                var button = UiFactory.CreateButton(
                    panel,
                    captions[i],
                    new Vector2(Margin + (i * 132f), TitleBarHeight + 16f),
                    new Vector2(120f, 44f));
                m_TabWidgets.Add(new TabWidget
                {
                    Tab = (MerchantTab)i,
                    Button = button,
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
            m_StashView.Build(
                panel, stash, m_StashContainerId, "仓库（交易来源）",
                new Vector2(RightX, TitleBarHeight + 16f));
            m_StashView.Refresh();
        }

        /// <summary>仓库下方的常驻出售入口：右键快捷出售 + 批量出售模式。</summary>
        private void BuildSellControls(RectTransform panel)
        {
            var top = TitleBarHeight + 16f + 498f + 16f;

            m_SellInfoLabel = UiFactory.CreateLabel(
                panel,
                "右键仓库物品可直接出售；点击「出售」可多选批量出售。",
                new Vector2(RightX, top),
                new Vector2(592f, 24f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            m_SellToggleButton = UiFactory.CreateButton(
                panel, "出售",
                new Vector2(RightX, top + 32f),
                new Vector2(180f, 46f));

            m_SellCancelButton = UiFactory.CreateButton(
                panel, "取消",
                new Vector2(RightX + 196f, top + 32f),
                new Vector2(130f, 46f));
            m_SellCancelButton.Rect.gameObject.SetActive(false);
        }

        /// <summary>右键物品后弹出的出售菜单。</summary>
        private void BuildSellContextMenu(RectTransform panel)
        {
            var host = new GameObject("SellMenu", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(panel, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(196f, 104f);

            var back = host.GetComponent<Image>();
            back.sprite = UiSprites.CardDim;
            back.type = Image.Type.Sliced;
            back.pixelsPerUnitMultiplier = 1f;
            back.color = Color.white;
            back.raycastTarget = false;

            m_SellMenuButton = UiFactory.CreateButton(
                rect, "出售",
                new Vector2(10f, 10f), new Vector2(176f, 40f));
            m_SellMenuCancelButton = UiFactory.CreateButton(
                rect, "取消",
                new Vector2(10f, 54f), new Vector2(176f, 40f));

            m_SellMenuRoot = host;
            host.SetActive(false);
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
            rect.sizeDelta = new Vector2(560f, 240f);

            var back = host.GetComponent<Image>();
            back.sprite = UiSprites.Card;
            back.type = Image.Type.Sliced;
            back.pixelsPerUnitMultiplier = 1f;
            back.color = Color.white;
            back.raycastTarget = false;

            m_ConfirmLabel = UiFactory.CreateLabel(
                rect, string.Empty,
                new Vector2(32f, 36f), new Vector2(496f, 90f),
                20f, TextAlignmentOptions.TopLeft, UiPalette.Ink,
                wrap: true);

            m_ConfirmButton = UiFactory.CreateButton(
                rect, "确认出售",
                new Vector2(32f, 156f), new Vector2(230f, 52f),
                UiButtonKind.Primary);
            m_CancelButton = UiFactory.CreateButton(
                rect, "取消",
                new Vector2(286f, 156f), new Vector2(230f, 52f));

            m_ConfirmRoot = host;
            host.SetActive(false);
        }

        /// <summary>创建一个页签内容的根节点。</summary>
        private static RectTransform CreateTabRoot(RectTransform parent, string name)
        {
            var rect = UiFactory.CreateRect(parent, name);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Margin, -(TitleBarHeight + 72f));
            rect.sizeDelta = TabRootSize;
            return rect;
        }

        /// <summary>创建一行纸面底色（列表行与任务卡共用）。</summary>
        private static RectTransform CreateRowBackground(
            RectTransform parent, string name, float top, float height)
        {
            var rect = UiFactory.CreatePanel(
                parent,
                name,
                new Vector2(LeftWidth, height),
                UiSprites.CardDim,
                new Vector2(0f, top));
            return rect;
        }
    }
}
