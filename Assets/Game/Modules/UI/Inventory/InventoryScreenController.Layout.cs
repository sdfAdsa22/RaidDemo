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
        private const float PanelWidth = 1560f;

        private const float PanelHeight = 690f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 64f;

        /// <summary>左栏（装备）的横坐标。</summary>
        private const float LeftColumnX = 28f;

        /// <summary>中栏（随身物品）的横坐标。</summary>
        private const float MiddleColumnX = 368f;

        /// <summary>内容区起始高度（标题条之下）。</summary>
        private const float ContentTop = 84f;

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

            BuildTitleBar(root);
            BuildEquipmentColumn(root);
            BuildWeightBar(root);

            var backpack = m_Loadout.Backpack;
            m_BackpackView = CreateGridView(root, backpack, m_BackpackContainerId, "主背包", new Vector2(MiddleColumnX, ContentTop));

            // 弹药挂紧贴在背包下方：它是随身物品的一部分，放在一起符合"背包里的东西"这个分组。
            var backpackHeight = (backpack.Height * InventoryGridView.CellSize) + 50f;
            var pouchTopLeft = new Vector2(MiddleColumnX, ContentTop + backpackHeight + 16f);
            if (m_Registry.TryGetGrid(m_AmmoPouchContainerId, out var pouch))
            {
                m_AmmoPouchView = CreateGridView(root, pouch, m_AmmoPouchContainerId, "弹药挂", pouchTopLeft);
            }

            // 右栏：战利品与仓库。横坐标必须**按背包实际宽度推算**，不能写死：
            // 背包由装备决定（5x5 / 6x6 / 7x7），写死会让 7x7 的包与右栏压在一起。
            var backpackWidth = (backpack.Width * InventoryGridView.CellSize) + 16f;
            m_LootAnchorTopLeft = new Vector2(MiddleColumnX + backpackWidth + 24f, ContentTop);
        }

        /// <summary>标题条：界面名 + 操作提示。</summary>
        private static void BuildTitleBar(RectTransform parent)
        {
            UiFactory.CreatePanel(
                parent,
                "TitleBar",
                new Vector2(PanelWidth, TitleBarHeight),
                UiSprites.CardDim,
                Vector2.zero);

            UiFactory.CreateLabel(
                parent,
                "背包",
                new Vector2(LeftColumnX, 14f),
                new Vector2(200f, 38f),
                26f,
                TextAlignmentOptions.Left,
                UiPalette.Ink);

            UiFactory.CreateLabel(
                parent,
                "Tab 关闭　F 整理　双击快速搬运　拖拽中按 R 旋转　右键菜单 / Shift+右键 拆分",
                new Vector2(LeftColumnX + 120f, 22f),
                new Vector2(PanelWidth - LeftColumnX - 160f, 26f),
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
