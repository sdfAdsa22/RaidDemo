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

        /// <summary>
        /// 货架列表的滚动视口与内容。
        /// </summary>
        /// <remarks>M8 批次 2 把货架扩到 15 项之后，列表高度超过了面板；
        /// 视口负责裁剪（RectMask2D），内容负责整体上移——滚轮改的就是内容的 Y 偏移。</remarks>
        private RectTransform m_ShopViewport;
        private RectTransform m_ShopContent;

        /// <summary>货架当前滚动偏移（像素，向下为正）。</summary>
        private float m_ShopScroll;

        /// <summary>货架内容高度，用于计算最大滚动量。</summary>
        private float m_ShopContentHeight;

        /// <summary>滚动提示：仅在内容超出视口时显示。</summary>
        private TextMeshProUGUI m_ShopScrollHint;

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

        /// <summary>
        /// 屏幕根：遮罩 + 面板的统一显隐开关。
        /// </summary>
        /// <remarks>与 <c>m_Root</c>（面板）分开：面板同时还是出售菜单定位的参照物，
        /// 而遮罩必须与面板一起显隐，两者不能是同一个节点。</remarks>
        private GameObject m_ScreenRoot;

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

        /// <summary>
        /// 货架视口高度（像素）。
        /// </summary>
        /// <remarks>从"列表起点 144"到"底部提示条 734"之间取 580，留出提示区的空隙；
        /// 一屏约能看到 12 行，其余用滚轮查看。</remarks>
        private const float ShopViewportHeight = 580f;

        /// <summary>滚轮一格对应的滚动像素：一格一行（行距 48）。</summary>
        private const float ShopScrollPixelsPerNotch = 48f;

        /// <summary>
        /// 判定"原始滚轮值是不是 120 制"的阈值。
        /// </summary>
        /// <remarks>
        /// <para>滚轮值的量纲取决于平台后端：老式平台给 WHEEL_DELTA 的 <b>120/格</b>，
        /// 而新版 Input System 在 Windows 上会按 <c>scrollWheelDeltaPerTick</c> 归一成 <b>1/格</b>。</para>
        /// <para>一格永远不可能超过 10：凡是绝对值大于 10 的输入都按 120 制换算，其余按"已经是格数"处理。
        /// 这样两种量纲都对，且不依赖任何平台宏。</para>
        /// </remarks>
        private const float ShopScrollRawUnitsPerNotchThreshold = 10f;

        /// <summary>120 制滚轮的一格数值。</summary>
        private const float ShopScrollRawUnitsPerNotch = 120f;

        /// <summary>键盘上下键每帧滚动的格数（约 12 像素/帧，与滚轮手感接近）。</summary>
        private const float ShopArrowNotchesPerFrame = 0.25f;

        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            var canvas = UiFactory.CreateCanvas(transform, "MerchantCanvas", 210);

            // 与背包界面同一条规则：遮罩与面板挂在同一个屏幕根下，一起显隐。
            // 遮罩单独挂在画布上会一直留在屏幕上，把游戏画面永久压暗。
            var screen = UiFactory.CreateRect(canvas, "Screen");
            UiFactory.Stretch(screen);
            m_ScreenRoot = screen.gameObject;
            UiFactory.CreateVeil(screen, "Veil");

            var panel = UiFactory.CreateCenteredPanel(
                screen, "Panel", new Vector2(PanelWidth, PanelHeight), UiSprites.Card);
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
