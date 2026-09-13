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

        /// <summary>右栏最多按几列预留（仓库是 10 列，是最宽的一种容器）。</summary>
        private const float MaxContainerColumns = 10f;

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

        /// <summary>
        /// 换背包后重建整套界面布局。
        /// </summary>
        /// <param name="backpack">新的背包网格。</param>
        /// <param name="containerId">背包的容器 ID。</param>
        /// <remarks>
        /// <para>换包会改变网格尺寸，而弹药挂与右栏的位置是按背包尺寸推算出来的，
        /// 因此不能只替换一个视图——必须整块重建，否则面板会互相压住。</para>
        /// <para>重建只销毁自己创建的画布，玩家数据（网格与装备）完全不动。</para>
        /// </remarks>
        public void RebuildLayout(InventoryGrid backpack, int containerId)
        {
            // 先记住当前打开的战利品容器，并把状态清成"没有打开"。
            // 重建会销毁它的视图；若状态还留着这个 ID，OpenLootContainer 会以为
            // "这个容器已经展示过了"而跳过重建——表现就是面板消失、重搜同一个箱子也不回来。
            var openLootId = m_LootContainerId;
            m_LootContainerId = 0;

            if (m_CanvasHost != null)
            {
                Destroy(m_CanvasHost);
            }

            m_CanvasHost = null;
            m_Root = null;
            m_MenuRoot = null;
            m_BackpackView = null;
            m_AmmoPouchView = null;
            m_LootView = null;
            m_Slots.Clear();
            m_MenuRows.Clear();

            m_BackpackContainerId = containerId;
            m_Loadout.ReplaceBackpack(backpack);

            var wasOpen = m_IsOpen;
            BuildLayout();
            RefreshAll();
            SetVisible(wasOpen);

            // 把打开着的战利品面板按原样恢复。标题用网格自己的标签，
            // 它就是创建容器时写进网格的显示名，不必再去问容器定义。
            if (wasOpen && openLootId > 0 && m_Registry.TryGetGrid(openLootId, out var loot))
            {
                OpenLootContainer(openLootId, loot.Label);
            }
        }

        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            // 必须高于准星画布的 100：两者相同时，后创建的画布会盖在上面，
            // 而准星是启动过程中后建的，于是准星会压在背包面板上。
            var canvas = UiFactory.CreateCanvas(transform, "InventoryCanvas", 200);
            m_CanvasHost = canvas.gameObject;

            // 面板打开时压暗背后的世界：奶油面板本身够亮，遮罩只压一半，
            // 让玩家知道自己还站在地图里。
            UiFactory.CreateVeil(canvas, "Veil");

            var root = UiFactory.CreateCenteredPanel(canvas, "Root", new Vector2(PanelWidth, PanelHeight), UiSprites.Card);
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
        /// </remarks>
        private void ComputeContentOrigin(InventoryGrid backpack)
        {
            var backpackWidth = (backpack.Width * InventoryGridView.CellSize) + GridFrame;
            var backpackHeight = (backpack.Height * InventoryGridView.CellSize) + GridTitle;
            var maxContainerWidth = (MaxContainerColumns * InventoryGridView.CellSize) + GridFrame;

            // ---- 水平：三栏 + 两个间距 ----
            var contentWidth = LeftColumnWidth + ColumnGap + backpackWidth + 24f + maxContainerWidth;
            m_ContentLeftX = Mathf.Max(OuterMargin, (PanelWidth - contentWidth) * 0.5f);

            // ---- 纵向：取三栏里最高的那一栏 ----
            var pouchHeight = InvokePouchHeight();
            var middleHeight = backpackHeight + 16f + pouchHeight;
            // 左栏高度：5 个装备槽 + 槽间距 + 负重标签与条 + 两行价值文字（见 Equipment 部分的排布）。
            var leftHeight = (5f * (58f + 14f)) + 22f + 106f;
            var rightHeight = (MaxContainerRows * InventoryGridView.CellSize) + GridTitle;
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
        }

        /// <summary>显示或隐藏整个界面。</summary>
        private void SetVisible(bool visible)
        {
            m_IsOpen = visible;
            if (m_Root != null)
            {
                m_Root.SetActive(visible);
            }

            // 打开界面时**如果什么容器都没指定**，就默认显示仓库（出击准备）。
            //
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
