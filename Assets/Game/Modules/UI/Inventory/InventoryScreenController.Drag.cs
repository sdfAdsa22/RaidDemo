using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的拖拽与点击处理部分。
    /// </summary>
    /// <remarks>
    /// <para>拖拽的所有分支最终都汇聚成一条命令，交给 CommandRouter 执行，
    /// 本文件里没有任何一处直接修改容器数据。</para>
    /// <para>指针到格子的换算是"屏幕坐标除以格子边长"，不走 uGUI 的射线系统，
    /// 因为拖拽需要的是格子坐标而不是被命中的对象。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>判定双击的时间窗口（秒）。0.35 秒是常见操作系统的默认值附近，手感不紧不松。</summary>
        private const float DoubleClickSeconds = 0.35f;

        /// <summary>上一次被按下的物品，用于识别双击。为空表示上一次点击没有落在物品上。</summary>
        private ItemInstance m_LastClickItem;

        /// <summary>上一次按下的时刻（秒）。负值表示还没有过点击。</summary>
        private float m_LastClickTime = -1f;

        /// <summary>处理鼠标左键按下：可能是开始拖拽，也可能是点击装备槽卸下。</summary>
        private void BeginPointerDown(Vector2 pointer)
        {
            var slot = FindSlotAt(pointer);
            if (slot != null)
            {
                if (slot.Item != null)
                {
                    m_Router.Dispatch(new InventoryUnequipIntent(0, slot.Slot));
                }

                return;
            }

            var view = FindGridAt(pointer, out var cell);
            if (view == null)
            {
                return;
            }

            var item = view.Grid.GetAt(cell);
            if (item == null || !view.Grid.TryGetOrigin(item, out var origin))
            {
                return;
            }

            // 双击 = 快速转移。这是搜刮时最常用的操作：一件件拖太慢，
            // 而玩家在战利品箱与背包之间来回搬东西会做几十次。
            var now = Time.unscaledTime;
            var isDoubleClick = ReferenceEquals(item, m_LastClickItem)
                                && (now - m_LastClickTime) <= DoubleClickSeconds;
            m_LastClickItem = item;
            m_LastClickTime = now;

            if (isDoubleClick)
            {
                // 清掉记录，避免三连击被当成第二次双击又转移一次。
                m_LastClickItem = null;
                DispatchQuickTransfer(view, origin);
                return;
            }

            m_IsDragging = true;
            m_DragSource = view;
            m_DragItem = item;
            m_DragRotated = item.Rotated;
            m_DragGrabOffset = new GridPoint(cell.X - origin.X, cell.Y - origin.Y);
        }

        /// <summary>拖拽过程中刷新落点预览。</summary>
        private void UpdatePreview(Vector2 pointer)
        {
            ClearPreviews();
            m_HoveredSlot = FindSlotAt(pointer);
            if (m_HoveredSlot != null)
            {
                m_HoveredSlot.Background.color = SlotHighlightColor;
                return;
            }

            var view = FindGridAt(pointer, out var cell);
            if (view == null)
            {
                return;
            }

            var size = SizeForDrag();
            var grab = ClampGrabOffset(size);
            var origin = new GridPoint(cell.X - grab.X, cell.Y - grab.Y);
            var check = view.Grid.CanPlace(m_DragItem, origin, m_DragRotated);
            view.ShowPreview(origin, size, check.Success);
        }

        /// <summary>松开左键：把拖拽结果翻译成命令。</summary>
        private void EndDrag(Vector2 pointer)
        {
            var slot = FindSlotAt(pointer);
            if (slot != null)
            {
                DispatchEquip(slot.Slot);
                CancelDrag();
                return;
            }

            var view = FindGridAt(pointer, out var cell);
            // 坐标必须从**源**容器查，而不是松手时所在的容器：
            // 物品不在目标容器里，对目标容器查询坐标一定失败，命令就发不出去了。
            if (view != null && m_DragSource.Grid.TryGetOrigin(m_DragItem, out var origin))
            {
                var size = SizeForDrag();
                var grab = ClampGrabOffset(size);
                var target = new GridPoint(cell.X - grab.X, cell.Y - grab.Y);
                DispatchMove(view, target, origin);
            }

            CancelDrag();
        }

        /// <summary>
        /// 在背包与战利品箱之间一键搬运物品。
        /// </summary>
        /// <param name="sourceView">物品当前所在的容器视图。</param>
        /// <param name="origin">物品在源容器中的左上角坐标。</param>
        /// <remarks>
        /// 目标容器固定取"另一个"：在战利品箱里双击就是捡进背包，在背包里双击就是放回箱子。
        /// 这样双击的语义只依赖物品在哪，玩家不需要先想清楚要搬到哪儿去。
        /// </remarks>
        private void DispatchQuickTransfer(InventoryGridView sourceView, GridPoint origin)
        {
            var targetId = sourceView.ContainerId == m_BackpackContainerId
                ? m_LootContainerId
                : m_BackpackContainerId;

            if (targetId == 0)
            {
                return;
            }

            m_Router.Dispatch(new InventoryQuickTransferIntent(
                0, sourceView.ContainerId, targetId, origin.X, origin.Y));
        }

        /// <summary>右键：对堆叠执行拆分，对装备槽执行卸下。</summary>
        private void HandleRightClick(Vector2 pointer)
        {
            var slot = FindSlotAt(pointer);
            if (slot != null && slot.Item != null)
            {
                m_Router.Dispatch(new InventoryUnequipIntent(0, slot.Slot));
                return;
            }

            var view = FindGridAt(pointer, out var cell);
            if (view == null)
            {
                return;
            }

            var item = view.Grid.GetAt(cell);
            if (item == null || item.StackCount <= 1)
            {
                return;
            }

            // 拆一半是最常用的数量，先给一个一键可得的默认值。
            var half = item.StackCount / 2;
            view.Grid.TryGetOrigin(item, out var origin);
            m_Router.Dispatch(new InventorySplitIntent(
                0, view.ContainerId, origin.X, origin.Y, half));
        }

        /// <summary>发出移动命令。</summary>
        private void DispatchMove(InventoryGridView targetView, GridPoint target, GridPoint sourceOrigin)
        {
            m_Router.Dispatch(new InventoryMoveIntent(
                0,
                m_DragSource.ContainerId,
                targetView.ContainerId,
                sourceOrigin.X,
                sourceOrigin.Y,
                target.X,
                target.Y,
                m_DragRotated));
        }

        /// <summary>发出装备命令。</summary>
        private void DispatchEquip(EquipmentSlot slot)
        {
            if (!m_DragSource.Grid.TryGetOrigin(m_DragItem, out var origin))
            {
                return;
            }

            m_Router.Dispatch(new InventoryEquipIntent(
                0, m_DragSource.ContainerId, origin.X, origin.Y, slot));
        }

        /// <summary>发出整理命令。</summary>
        private void DispatchSort()
        {
            m_Router.Dispatch(new InventorySortIntent(0, m_BackpackContainerId));
        }

        /// <summary>结束拖拽状态并清掉所有预览。</summary>
        private void CancelDrag()
        {
            m_IsDragging = false;
            m_DragSource = null;
            m_DragItem = null;
            m_DragRotated = false;
            ClearPreviews();
        }

        /// <summary>清除两个网格与悬停装备槽的预览高亮。</summary>
        private void ClearPreviews()
        {
            m_BackpackView?.ClearPreview();
            m_LootView?.ClearPreview();
            if (m_HoveredSlot != null)
            {
                m_HoveredSlot.Background.color = SlotColor;
                m_HoveredSlot = null;
            }
        }

        /// <summary>当前拖拽使用的占地尺寸（考虑旋转）。</summary>
        private GridSize SizeForDrag()
        {
            var size = m_DragItem.Definition.GridSize;
            return m_DragRotated ? size.Rotated() : size;
        }

        /// <summary>把抓取偏移限制在旋转后的物品范围内，避免旋转时指针跑到物品外。</summary>
        private GridPoint ClampGrabOffset(GridSize size)
        {
            var x = Mathf.Clamp(m_DragGrabOffset.X, 0, size.Width - 1);
            var y = Mathf.Clamp(m_DragGrabOffset.Y, 0, size.Height - 1);
            return new GridPoint(x, y);
        }

        /// <summary>找出指针所在的网格视图与格子。</summary>
        private InventoryGridView FindGridAt(Vector2 pointer, out GridPoint cell)
        {
            cell = default;
            if (m_BackpackView != null && m_BackpackView.TryGetCellAt(pointer, out cell))
            {
                return m_BackpackView;
            }

            if (m_LootView != null && m_LootView.TryGetCellAt(pointer, out cell))
            {
                return m_LootView;
            }

            return null;
        }

        /// <summary>找出指针所在的装备槽。</summary>
        private SlotWidget FindSlotAt(Vector2 pointer)
        {
            for (var i = 0; i < m_Slots.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(m_Slots[i].Rect, pointer, null))
                {
                    return m_Slots[i];
                }
            }

            return null;
        }
    }
}
