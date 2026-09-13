using System.Collections.Generic;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的布局与刷新部分（M7 批次 4 的版式重排 + 换皮）。
    /// </summary>
    /// <remarks>
    /// <para><b>版式：三栏。</b>左栏是装备（人体概念的竖列）、中栏是随身物品（主背包 + 弹药挂）、
    /// 右栏是"另一处容器"（战利品箱或仓库）。旧版把战利品塞在背包下方，
    /// 仓库是 10 列宽，一张面板根本放不下，而且和随身物品混在一起也说不清哪些是自己的。</para>
    ///
    /// <para>拆成 partial 文件的原因：整个界面类已经超过项目规定的单文件 400 行上限。
    /// 这里按职责切分：布局负责"长什么样"，拖拽部分负责"鼠标做了什么"。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>
        /// 面板尺寸（参考像素）。
        /// </summary>
        /// <remarks>宽度按最宽的右栏（10 列仓库）反推；高度按最高的一列反推——
        /// 中栏在 7×7 背包 + 弹药挂时最高（约 648），因此取 690 留出下边距。
        /// 写死这两个数是因为内容尺寸本身有上界（仓库 10×8、背包最多 7×7），
        /// 而"面板跟着内容长"会让不同背包下面板大小不一样，看起来像界面在抖。</remarks>
        /// <remarks>
        /// <para><b>宽度按最坏情况反推：</b>左右边距 28×2 + 装备栏 300 + 间距 40 +
        /// 7×7 背包（408）+ 间距 24 + 10 列仓库（576）= 1404。</para>
        /// <para><b>内容不足时靠居中补，而不是缩面板。</b>背包没装备时只有 5×5（296 宽），
        /// 三栏整体比面板窄 112 像素；如果让它们左对齐，右下角就会空出一块（负责人 2026-09-13 反馈），
        /// 而缩面板会导致换背包时窗口大小跳变。做法是把三栏当作一组在面板内居中，
        /// 空出来的量均分成左右两侧的留白。</para>
        /// </remarks>
        private const float PanelWidth = 1404f;

        private const float PanelHeight = 690f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 64f;

        /// <summary>左栏（装备）的横坐标。</summary>
        private const float OuterMargin = 28f;

        /// <summary>栏与栏之间的间距。</summary>
        private const float ColumnGap = 40f;

        /// <summary>内容区起始高度（标题条之下）。</summary>
        private const float MinContentTop = 84f;

        /// <summary>右栏最多按几行预留（仓库是 8 行）。</summary>
        private const float MaxContainerRows = 8f;

        /// <summary>格子视图四周的内边距合计（与 InventoryGridView 的 PanelPadding 对应）。</summary>
        private const float GridFrame = 16f;

        /// <summary>格子视图标题区高度（与 InventoryGridView 的 TitleHeight 对应）。</summary>
        private const float GridTitle = 50f;

        /// <summary>本帧算出的左栏横坐标。三栏居中后它不是常量，由 BuildLayout 写入。</summary>
        private float m_ContentLeftX = OuterMargin;

        /// <summary>本帧算出的内容区顶部。纵向居中后它不是常量，由 BuildLayout 写入。</summary>
        private float m_ContentTopY = MinContentTop;
        /// <summary>本次布局右栏实际使用的列数；0 表示当前没有右栏。</summary>
        /// <remarks>右栏宽度参与"三栏整体居中"，不能永远按 10 列仓库预留；武器架只有 4 列，
        /// 按 10 列留位置会让右侧空出一大块。实际列数由构建布局前的当前容器决定。</remarks>
        private int m_RightContainerColumns;

        /// <summary>
        /// 屏幕根：遮罩 + 面板的统一显隐开关。
        /// </summary>
        /// <remarks>它必须与 <c>m_Root</c>（面板）分开：面板同时还是各网格视图的父节点，
        /// 那些视图的坐标是相对面板算的，不能改挂到整屏节点下。</remarks>
        private GameObject m_ScreenRoot;

        /// <summary>
        /// 换背包后重建整套界面布局。
        /// </summary>
        /// <param name="backpack">新的背包网格。</param>
        /// <param name="containerId">背包的容器 ID。</param>
        /// <remarks>
        /// <para>换包会改变网格尺寸，而弹药挂与右栏的位置是按背包尺寸推算出来的，
        /// 因此不能只替换一个视图——必须整块重建，否则面板会互相压住。重建只销毁自己创建的画布，玩家数据完全不动。</para>
        /// </remarks>
        public void RebuildLayout(InventoryGrid backpack, int containerId)
        {
            // 当前打开的右栏容器（仓库或战利品）及其标题要一起恢复：
            // 右栏宽度决定三栏怎么居中，而"战利品："与"仓库"的标题语义只有打开它的调用点知道，
            // 不能靠网格标签重新猜一次。
            var rightContainerId = m_LootContainerId;
            var rightTitle = m_RightContainerTitle;
            if (rightContainerId == 0 && m_IsOpen && m_PrepStashContainerId > 0)
            {
                // 准备界面在打开且没有指定容器时默认显示仓库；直接在这里写入目标，
                // 避免 SetVisible(true) 先按"没有右栏"建一次、发现仓库后又重建第二次。
                rightContainerId = m_PrepStashContainerId;
                rightTitle = "仓库";
            }

            RebuildLayout(backpack, containerId, rightContainerId, rightTitle, m_IsOpen);
        }

        /// <summary>
        /// 重建整套界面，并指定右栏要显示哪个容器。
        /// </summary>
        /// <param name="backpack">新的背包网格。</param>
        /// <param name="containerId">背包的容器 ID。</param>
        /// <param name="rightContainerId">右栏容器 ID；0 表示没有右栏。</param>
        /// <param name="rightTitle">右栏标题；没有右栏时可以为 null。</param>
        /// <param name="show">重建后是否显示界面。</param>
        /// <remarks>右栏宽度影响三栏整体居中，因此"换容器且宽度不同"与"换背包"都必须走整块重建；
        /// 只替换网格视图会让内容按旧宽度预留，出现右侧一大块空白。</remarks>
        private void RebuildLayout(
            InventoryGrid backpack,
            int containerId,
            int rightContainerId,
            string rightTitle,
            bool show)
        {
            if (m_CanvasHost != null)
            {
                Destroy(m_CanvasHost);
            }

            m_CanvasHost = null;
            m_ScreenRoot = null;
            m_Root = null;
            m_MenuRoot = null;
            m_BackpackView = null;
            m_AmmoPouchView = null;
            m_LootView = null;
            m_Slots.Clear();
            m_MenuRows.Clear();

            m_BackpackContainerId = containerId;
            m_Loadout.ReplaceBackpack(backpack);

            // 先确定右栏宽度，再构建布局：ComputeContentOrigin 要靠它决定三栏整体是否居中。
            InventoryGrid rightGrid = null;
            var hasRightGrid = rightContainerId > 0 && m_Registry.TryGetGrid(rightContainerId, out rightGrid);
            m_LootContainerId = rightContainerId;
            m_RightContainerTitle = rightTitle;
            m_RightContainerColumns = hasRightGrid ? rightGrid.Width : 0;

            BuildLayout();
            RefreshAll();
            // 右栏视图必须在 BuildLayout 算出 m_LootAnchorTopLeft 之后创建。
            // 此时 m_LootContainerId 已经指向目标容器，SetVisible 不会再自动改开仓库。
            if (show && hasRightGrid)
            {
                m_LootView = CreateGridView(
                    (RectTransform)m_Root.transform,
                    rightGrid,
                    rightContainerId,
                    string.IsNullOrEmpty(rightTitle) ? "战利品" : rightTitle,
                    m_LootAnchorTopLeft);
            }

            RefreshAll();
            SetVisible(show);
        }

        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            // 必须高于准星画布的 100：两者相同时，后创建的画布会盖在上面，
            // 而准星是启动过程中后建的，于是准星会压在背包面板上。
            var canvas = UiFactory.CreateCanvas(transform, "InventoryCanvas", 200);
            m_CanvasHost = canvas.gameObject;

            // 遮罩与面板必须挂在同一个"屏幕根"下，由它统一显隐。
            // 曾经把遮罩直接挂在画布上，而显隐开关只作用于面板——结果关掉背包之后
            // 遮罩还留在屏幕上，整个游戏画面被永久压暗（负责人 2026-09-13 反馈）。
            var screen = UiFactory.CreateRect(canvas, "Screen");
            UiFactory.Stretch(screen);
            m_ScreenRoot = screen.gameObject;

            // 面板打开时压暗背后的世界：奶油面板本身够亮，遮罩只压一半，
            // 让玩家知道自己还站在地图里。
            UiFactory.CreateVeil(screen, "Veil");

            var root = UiFactory.CreateCenteredPanel(screen, "Root", new Vector2(PanelWidth, PanelHeight), UiSprites.Card);
            m_Root = root.gameObject;

            var backpack = m_Loadout.Backpack;
            ComputeContentOrigin(backpack);

            BuildTitleBar(root);
            BuildEquipmentColumn(root);
            BuildWeightBar(root);

            var middleX = m_ContentLeftX + LeftColumnWidth + ColumnGap;
            m_BackpackView = CreateGridView(root, backpack, m_BackpackContainerId, "主背包", new Vector2(middleX, m_ContentTopY));

            // 弹药挂紧贴在背包下方：它是随身物品的一部分，放在一起符合"背包里的东西"这个分组。
            var backpackHeight = (backpack.Height * InventoryGridView.CellSize) + 50f;
            var pouchTopLeft = new Vector2(middleX, m_ContentTopY + backpackHeight + 16f);
            if (m_Registry.TryGetGrid(m_AmmoPouchContainerId, out var pouch))
            {
                m_AmmoPouchView = CreateGridView(root, pouch, m_AmmoPouchContainerId, "弹药挂", pouchTopLeft);
            }

            // 右栏：战利品与仓库。横坐标必须**按背包实际宽度推算**，不能写死：
            // 背包由装备决定（5x5 / 6x6 / 7x7），写死会让 7x7 的包与右栏压在一起。
            var backpackWidth = (backpack.Width * InventoryGridView.CellSize) + GridFrame;
            m_LootAnchorTopLeft = new Vector2(middleX + backpackWidth + 24f, m_ContentTopY);
        }

        /// <summary>
        /// 算出内容区的左上角：三栏作为一组，在面板内水平居中；纵向也居中于标题条之下。
        /// </summary>
        /// <param name="backpack">当前背包网格，决定中栏宽度。</param>
        /// <remarks>
        /// <para><b>为什么居中而不是缩面板：</b>面板尺寸如果跟着背包变，换包时窗口会变宽变窄，
        /// 看起来像界面在抖；而如果内容左对齐，背包越小时右下角空得越多——
        /// 这两者都是"内容与面板尺寸不匹配"的不同表现。居中是唯一让两者都不发生的做法。</para>
        /// <para>纵向同理：右栏按 8 行仓库预留，背包装得少时下方会空。
        /// 把内容在标题条以下居中，空出来的量上下均分。</para>
        /// <para><b>右栏宽度按当前容器实际列数计算</b>：仓库是 10 列，武器架可能只有 4 列。
        /// 如果永远按 10 列预留，搜刮武器架时内容会被挤到左边，右侧空出一大块
        /// （负责人 2026-09-13 反馈的"搜索物品时背包界面没有居中"）。</para>
        /// </remarks>
        private void ComputeContentOrigin(InventoryGrid backpack)
        {
            var backpackWidth = (backpack.Width * InventoryGridView.CellSize) + GridFrame;
            var backpackHeight = (backpack.Height * InventoryGridView.CellSize) + GridTitle;
            var rightWidth = m_RightContainerColumns > 0
                ? (m_RightContainerColumns * InventoryGridView.CellSize) + GridFrame
                : 0f;
            var rightGap = rightWidth > 0f ? 24f : 0f;

            // ---- 水平：三栏 + 两个间距 ----
            var contentWidth = LeftColumnWidth + ColumnGap + backpackWidth + rightGap + rightWidth;
            m_ContentLeftX = Mathf.Max(OuterMargin, (PanelWidth - contentWidth) * 0.5f);

            // ---- 纵向：取三栏里最高的那一栏 ----
            var pouchHeight = InvokePouchHeight();
            var middleHeight = backpackHeight + 16f + pouchHeight;
            // 左栏高度：5 个装备槽 + 槽间距 + 负重标签与条 + 两行价值文字（见 Equipment 部分的排布）。
            var leftHeight = (5f * (58f + 14f)) + 22f + 106f;
            var rightHeight = rightWidth > 0f
                ? (MaxContainerRows * InventoryGridView.CellSize) + GridTitle
                : 0f;
            var contentHeight = Mathf.Max(middleHeight, Mathf.Max(leftHeight, rightHeight));

            var available = PanelHeight - TitleBarHeight;
            m_ContentTopY = TitleBarHeight + Mathf.Max(MinContentTop - TitleBarHeight, (available - contentHeight) * 0.5f);
        }

        /// <summary>弹药挂的高度（像素）。没有弹药挂时返回 0。</summary>
        private float InvokePouchHeight()
        {
            return m_Registry.TryGetGrid(m_AmmoPouchContainerId, out var pouch)
                ? (pouch.Height * InventoryGridView.CellSize) + GridTitle
                : 0f;
        }

        /// <summary>标题条：界面名 + 操作提示。</summary>
        private void BuildTitleBar(RectTransform parent)
        {
            UiFactory.CreatePanel(
                parent,
                "TitleBar",
                new Vector2(PanelWidth, TitleBarHeight),
                UiSprites.CardDim,
                Vector2.zero);

            // 标题与提示按内容组的左右边缘对齐：居中之后如果还按面板边缘对齐，
            // 标题会明显偏左，看起来像两套排版。
            UiFactory.CreateLabel(
                parent,
                "背包",
                new Vector2(m_ContentLeftX, 14f),
                new Vector2(200f, 38f),
                26f,
                TextAlignmentOptions.Left,
                UiPalette.Ink);

            UiFactory.CreateLabel(
                parent,
                "Tab 关闭　F 整理　双击快速搬运　拖拽中按 R 旋转　右键菜单 / Shift+右键 拆分",
                new Vector2(m_ContentLeftX + 120f, 22f),
                new Vector2(PanelWidth - (m_ContentLeftX * 2f) - 128f, 26f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Right,
                UiPalette.InkSoft);
        }

        /// <summary>创建一个网格视图。</summary>
        private InventoryGridView CreateGridView(
            RectTransform parent,
            InventoryGrid grid,
            int containerId,
            string title,
            Vector2 topLeft)
        {
            var host = new GameObject($"Grid_{containerId}");
            host.transform.SetParent(parent, worldPositionStays: false);
            var view = host.AddComponent<InventoryGridView>();
            view.Build(parent, grid, containerId, title, topLeft);
            view.Refresh();
            return view;
        }

        /// <summary>重画全部内容。</summary>
        private void RefreshAll()
        {
            m_BackpackView?.Refresh();
            m_AmmoPouchView?.Refresh();
            m_LootView?.Refresh();
            RefreshEquipmentSlots();
            RefreshWeightBar();
            RefreshValueLabel();
            RefreshAmmoPouchTitle();
        }

        /// <summary>显示或隐藏整个界面。</summary>
        private void SetVisible(bool visible)
        {
            var changed = m_IsOpen != visible;
            m_IsOpen = visible;
            var target = m_ScreenRoot != null ? m_ScreenRoot : m_Root;
            if (target != null)
            {
                target.SetActive(visible);
            }
            if (changed)
            {
                UiAudio.Play(visible ? UiCue.PanelOpen : UiCue.PanelClose);
            }
            // 打开界面时**如果什么容器都没指定**，就默认显示仓库（出击准备）。
            // 条件必须是"当前没有打开任何容器"而不是"当前不是仓库"：
            // 后者会把刚被显式打开的容器顶掉——在安全屋里按 E 开测试箱，
            // 面板会立刻被替换成仓库，看起来就像"按 E 只会开仓库"。
            if (visible
                && m_PrepStashContainerId > 0
                && m_LootContainerId == 0)
            {
                OpenStash(m_PrepStashContainerId);
            }
            m_SetCursorLock?.Invoke(!visible);

            if (!visible)
            {
                CancelDrag();
                CloseContextMenu();
                // 关闭界面即结束这次搜刮：战利品面板随之销毁，
                // 想再搬东西就得重新走到箱子前读条。这样"开箱成本"对每次搜刮都成立，
                // 而不是开一次之后就能无限次免费取用。
                CloseLootContainer();
            }
        }

        /// <summary>装备槽的中文名。</summary>
        private static string SlotDisplayName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.PrimaryWeapon:
                    return "主武器";
                case EquipmentSlot.SecondaryWeapon:
                    return "副武器";
                case EquipmentSlot.Head:
                    return "头盔";
                case EquipmentSlot.Body:
                    return "护甲";
                default:
                    return "背包";
            }
        }

        /// <summary>
        /// 创建一个左上对齐的文本标签。
        /// </summary>
        /// <remarks>保留这个包装而不是让调用方直接用 UiFactory：右键菜单与刷新逻辑都按
        /// "左上偏移 + 宽高 + 字号"这组参数调用它，换皮时不必改动那些调用点。</remarks>
        private static TextMeshProUGUI CreateLabel(
            RectTransform parent,
            string content,
            Vector2 topLeft,
            float width,
            float height,
            int fontSize)
        {
            return UiFactory.CreateLabel(
                parent,
                content,
                topLeft,
                new Vector2(width, height),
                fontSize,
                TextAlignmentOptions.Left,
                UiPalette.Ink);
        }
    }
}
