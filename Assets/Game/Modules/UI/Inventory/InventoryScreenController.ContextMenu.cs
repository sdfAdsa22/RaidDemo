using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的右键上下文菜单：把「右键直接执行一个动作」改成「先列选项再决定」。
    /// </summary>
    /// <remarks>
    /// <para>旧行为是右键即拆分、装备槽即卸下——快，但没地方放「使用医疗品」这类新动作。
    /// 菜单的代价是拆分多一次点击，因此保留了 <c>Shift + 右键</c> 直接拆分这条快捷路径。</para>
    ///
    /// <para>菜单只负责「列选项与派发意图」，所有动作仍然走 CommandRouter，
    /// 与拖拽、双击共用同一套规则：界面永远不直接改数据。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>
        /// 菜单宽度（像素）。
        /// </summary>
        /// <remarks>
        /// 取 100：菜单项只有两三个字，宽度收到刚好容纳文字即可。
        /// 之前 200 宽是为了好看，实际观感是「一块横着的板子压在背包上」，
        /// 反而挡住更多格子。
        /// </remarks>
        private const float MenuWidth = 100f;

        /// <summary>单行高度（像素）。</summary>
        private const float MenuRowHeight = 30f;

        /// <summary>菜单行的界面元素。</summary>
        private sealed class MenuRow
        {
            public RectTransform Rect;
            public Image Background;
            public TMPro.TextMeshProUGUI Label;
            public Action Action;
            public bool Enabled;
        }

        /// <summary>菜单行的常态底色：透明，让菜单底板露出来。</summary>
        private static readonly Color MenuRowColor = new Color(1f, 1f, 1f, 0f);

        /// <summary>菜单行悬停底色：主题的青绿浅色。</summary>
        private static readonly Color MenuRowHoverColor = UiPalette.SlotHover;

        private readonly List<MenuRow> m_MenuRows = new List<MenuRow>(4);
        private RectTransform m_MenuRoot;
        private InventoryGridView m_MenuSourceView;
        private ItemInstance m_MenuItem;
        private GridPoint m_MenuOrigin;
        private EquipmentSlot m_MenuSlot;
        private bool m_MenuHasSlot;

        /// <summary>菜单是否打开。打开时它会吃掉左键，避免误触发拖拽。</summary>
        public bool IsContextMenuOpen
        {
            get { return m_MenuRoot != null && m_MenuRoot.gameObject.activeSelf; }
        }

        /// <summary>打开菜单。target 为装备槽时 <paramref name="slot"/> 有效。</summary>
        private void OpenContextMenu(Vector2 pointer)
        {
            var slot = FindSlotAt(pointer);
            if (slot != null && slot.Item != null)
            {
                m_MenuHasSlot = true;
                m_MenuSlot = slot.Slot;
                m_MenuSourceView = null;
                m_MenuItem = slot.Item;
                m_MenuOrigin = default;
            }
            else
            {
                var view = FindGridAt(pointer, out var cell);
                if (view == null)
                {
                    return;
                }

                var item = view.Grid.GetAt(cell);
                if (item == null)
                {
                    return;
                }

                m_MenuHasSlot = false;
                m_MenuSourceView = view;
                m_MenuItem = item;
                view.Grid.TryGetOrigin(item, out m_MenuOrigin);
            }

            EnsureMenuBuilt();
            ConfigureMenuRows();
            PositionMenu(pointer);
            m_MenuRoot.gameObject.SetActive(true);
        }

        /// <summary>关闭菜单。</summary>
        private void CloseContextMenu()
        {
            if (m_MenuRoot != null)
            {
                m_MenuRoot.gameObject.SetActive(false);
            }
        }

        /// <summary>创建菜单骨架（只做一次）。</summary>
        private void EnsureMenuBuilt()
        {
            if (m_MenuRoot != null)
            {
                return;
            }

            var host = new GameObject("ContextMenu", typeof(RectTransform), typeof(Image));
            m_MenuRoot = (RectTransform)host.transform;
            m_MenuRoot.SetParent(m_CanvasHost.transform, worldPositionStays: false);
            m_MenuRoot.anchorMin = new Vector2(0f, 1f);
            m_MenuRoot.anchorMax = new Vector2(0f, 1f);
            m_MenuRoot.pivot = new Vector2(0f, 1f);
            m_MenuRoot.sizeDelta = new Vector2(MenuWidth, (MenuRowHeight * 4f) + 8f);

            // 菜单是一张浮起来的纸：用比主面板略深的贴图 + 深墨描边，
            // 与背包面板形成层次，而不是另配一套颜色。
            var back = host.GetComponent<Image>();
            back.sprite = UiSprites.CardDim;
            back.type = Image.Type.Sliced;
            back.pixelsPerUnitMultiplier = 1f;
            back.color = Color.white;
            back.raycastTarget = false;

            for (var i = 0; i < 4; i++)
            {
                var rowHost = new GameObject($"Row_{i}", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)rowHost.transform;
                rect.SetParent(m_MenuRoot, worldPositionStays: false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(4f, -4f - (i * MenuRowHeight));
                rect.sizeDelta = new Vector2(MenuWidth - 8f, MenuRowHeight);

                var background = rowHost.GetComponent<Image>();
                background.sprite = UiSprites.Block;
                background.type = Image.Type.Sliced;
                background.pixelsPerUnitMultiplier = 1f;
                background.color = MenuRowColor;
                background.raycastTarget = false;

                var label = CreateLabel(rect, string.Empty, new Vector2(12f, 0f),
                    MenuWidth - 24f, MenuRowHeight, 15);

                m_MenuRows.Add(new MenuRow
                {
                    Rect = rect,
                    Background = background,
                    Label = label,
                });
            }

            m_MenuRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 按当前目标配置四种动作与可用状态。
        /// </summary>
        /// <remarks>
        /// 「使用」的两个硬条件来自负责人的确认：必须是自己身上的容器（战利品箱不能直接用），
        /// 且必须是医疗类物品。不满足时该行灰显而不是隐藏——
        /// 灰显能告诉玩家「这里本来有这个动作」，隐藏则让人以为功能不存在。
        /// </remarks>
        private void ConfigureMenuRows()
        {
            var isOwnContainer = !m_MenuHasSlot
                && (m_MenuSourceView.ContainerId == m_BackpackContainerId
                    || m_MenuSourceView.ContainerId == m_AmmoPouchContainerId);
            var medical = m_MenuHasSlot ? null : ResolveMedical(m_MenuItem);
            var containerId = m_MenuHasSlot ? 0 : m_MenuSourceView.ContainerId;

            SetRow(0, "使用", isOwnContainer && medical != null,
                () => m_RequestUseItem?.Invoke(containerId, m_MenuOrigin.X, m_MenuOrigin.Y));

            SetRow(1, "装备", CanEquip(m_MenuItem),
                () => DispatchQuickAction(m_MenuSourceView, m_MenuItem, m_MenuOrigin));

            SetRow(2, "卸下", m_MenuHasSlot,
                () => m_Router.Dispatch(new InventoryUnequipIntent(0, m_MenuSlot)));

            SetRow(3, "拆分", !m_MenuHasSlot && m_MenuItem.StackCount > 1,
                () => m_Router.Dispatch(new InventorySplitIntent(
                    0, containerId, m_MenuOrigin.X, m_MenuOrigin.Y, m_MenuItem.StackCount / 2)));
        }

        /// <summary>写入一行的文字与可用状态。</summary>
        private void SetRow(int index, string caption, bool enabled, Action action)
        {
            var row = m_MenuRows[index];
            row.Label.text = caption;
            row.Label.color = enabled ? UiPalette.Ink : UiPalette.InkDisabled;
            row.Background.color = MenuRowColor;
            row.Enabled = enabled;
            row.Action = action;
        }

        /// <summary>能否装备。只按分类判断，具体装到哪个槽由快速操作规则决定。</summary>
        private static bool CanEquip(ItemInstance item)
        {
            if (item == null)
            {
                return false;
            }

            var category = item.Definition.Category;
            return category == ItemCategory.Weapon
                || category == ItemCategory.Helmet
                || category == ItemCategory.BodyArmor
                || category == ItemCategory.Backpack;
        }

        /// <summary>取物品定义上的医疗行为；走物品目录，因为接口刻意不认识行为。</summary>
        private Data.MedicalBehavior ResolveMedical(ItemInstance item)
        {
            if (item == null || m_ItemCatalog == null)
            {
                return null;
            }

            var definition = m_ItemCatalog.Get(item.Definition.Id);
            return definition != null ? definition.Behavior as Data.MedicalBehavior : null;
        }

        /// <summary>把菜单摆在鼠标位置，并保证不超出画布边界。</summary>
        private void PositionMenu(Vector2 pointer)
        {
            var canvasRect = (RectTransform)m_CanvasHost.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, pointer, null, out var local))
            {
                m_MenuRoot.anchoredPosition = Vector2.zero;
                return;
            }

            // 画布原点在中心，而菜单锚在左上角：先换算到左上角坐标系再做边界收敛。
            var x = local.x + (canvasRect.rect.width * 0.5f);
            var y = local.y - (canvasRect.rect.height * 0.5f);
            var maxX = Mathf.Max(0f, canvasRect.rect.width - MenuWidth - 4f);
            var maxY = Mathf.Min(0f, -(canvasRect.rect.height - (MenuRowHeight * 4f) - 4f));
            m_MenuRoot.anchoredPosition = new Vector2(Mathf.Clamp(x, 4f, maxX), Mathf.Clamp(y, maxY, -4f));
        }

        /// <summary>
        /// 处理菜单上的鼠标交互。
        /// </summary>
        /// <returns>true 表示本帧的输入已被菜单消费，调用方不应再做拖拽等处理。</returns>
        private bool UpdateContextMenu(Vector2 pointer, bool leftPressed, bool rightPressed, bool escapePressed)
        {
            if (!IsContextMenuOpen)
            {
                return false;
            }

            if (escapePressed || rightPressed)
            {
                CloseContextMenu();
                return true;
            }

            MenuRow hovered = null;
            for (var i = 0; i < m_MenuRows.Count; i++)
            {
                var row = m_MenuRows[i];
                var over = RectTransformUtility.RectangleContainsScreenPoint(row.Rect, pointer, null);
                row.Background.color = over && row.Enabled ? MenuRowHoverColor : MenuRowColor;
                if (over)
                {
                    hovered = row;
                }
            }

            if (!leftPressed)
            {
                return true;
            }

            var action = hovered != null && hovered.Enabled ? hovered.Action : null;
            CloseContextMenu();
            action?.Invoke();
            return true;
        }
    }
}
