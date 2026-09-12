using RaidDemo.Data;
using RaidDemo.Meta;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面的出售部分：选中、右键快速出售与高价值确认。
    /// </summary>
    /// <remarks>
    /// 出售动作的输入与显示都集中在这里。拆出来的直接原因是动作文件超过了
    /// 工程规定的 400 行上限，但边界本身也成立：买、卖、任务三条交互链路
    /// 各自负责一页，主文件只保留页签切换与公共刷新。
    /// </remarks>
    public sealed partial class MerchantScreenController
    {
        /// <summary>刷新出售页的选中物品信息。</summary>
        private void RefreshSellPanel()
        {
            if (m_SellSelectionLabel == null)
            {
                return;
            }

            if (m_SelectedStashItem == null)
            {
                m_SellSelectionLabel.text = "未选择物品";
                m_SellDetailLabel.text = "左键点选仓库物品，或直接用右键出售。";
                return;
            }

            var item = m_SelectedStashItem;
            var total = TraderPricing.GetSellPrice(item.Definition, item.StackCount);
            m_SellSelectionLabel.text = item.StackCount > 1
                ? $"{item.Definition.DisplayName} x{item.StackCount}"
                : item.Definition.DisplayName;
            m_SellDetailLabel.text =
                $"账面价值 {item.TotalValue:N0}　商人收购 {total:N0}";
        }

        /// <summary>出售页：选中、右键快速出售与确认。</summary>
        private void UpdateSellInput(Vector2 pointer)
        {
            if (m_SellButton != null)
            {
                var hovered = m_SellButton.Contains(pointer);
                m_SellButton.SetHovered(hovered);
                if (hovered && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    BeginSell(m_SelectedStashItem, m_SelectedStashCell, out var message);
                    if (message != null)
                    {
                        ShowStatus(message, false);
                    }

                    return;
                }
            }

            if (m_StashView == null || !m_StashView.TryGetCellAt(pointer, out var cell))
            {
                return;
            }

            var item = m_StashView.Grid.GetAt(cell);
            if (item == null)
            {
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                m_SelectedStashItem = item;
                m_SelectedStashCell = cell;
                RefreshSellPanel();
                return;
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                BeginSell(item, cell, out var message);
                if (message != null)
                {
                    ShowStatus(message, false);
                }
            }
        }

        /// <summary>出售确认面板的输入。</summary>
        private void UpdateConfirmInput(Vector2 pointer)
        {
            var overConfirm = m_ConfirmButton.Contains(pointer);
            var overCancel = m_CancelButton.Contains(pointer);
            m_ConfirmButton.SetHovered(overConfirm);
            m_CancelButton.SetHovered(overCancel);

            if (!Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (overConfirm)
            {
                var item = m_PendingSellItem;
                var cell = m_PendingSellCell;
                CancelSellConfirm();
                DispatchSell(item, cell);
            }
            else if (overCancel)
            {
                CancelSellConfirm();
            }
        }

        /// <summary>
        /// 发起一次出售。
        /// </summary>
        /// <param name="item">要出售的物品。</param>
        /// <param name="cell">物品所在格。</param>
        /// <param name="message">需要提示的错误；不需要提示时为 null。</param>
        private void BeginSell(ItemInstance item, GridPoint cell, out string message)
        {
            message = null;
            if (item == null)
            {
                message = "先选择一件要出售的物品。";
                return;
            }

            var total = TraderPricing.GetSellPrice(item.Definition, item.StackCount);
            if (total >= HighValueSellThreshold)
            {
                m_PendingSellItem = item;
                m_PendingSellCell = cell;
                m_ConfirmLabel.text =
                    $"确定出售 {item.Definition.DisplayName}？\n"
                    + $"收购价 {total:N0} 金币，此操作不可撤销。";
                m_ConfirmRoot.SetActive(true);
                return;
            }

            DispatchSell(item, cell);
        }

        /// <summary>真正派发出售命令。</summary>
        private void DispatchSell(ItemInstance item, GridPoint cell)
        {
            if (item == null || m_Progress == null)
            {
                return;
            }

            var total = TraderPricing.GetSellPrice(item.Definition, item.StackCount);
            var result = m_Router.Dispatch(new SellItemIntent(
                0, m_StashContainerId, cell.X, cell.Y));
            if (result.Success)
            {
                ShowStatus(
                    $"已出售 {item.Definition.DisplayName}，获得 {total:N0} 金币。",
                    true);
                ClearSelection();
            }
            else
            {
                ShowStatus(result.Message, false);
            }
        }

        private void CancelSellConfirm()
        {
            m_PendingSellItem = null;
            m_PendingSellCell = default;
            if (m_ConfirmRoot != null)
            {
                m_ConfirmRoot.SetActive(false);
            }
        }
    }
}
