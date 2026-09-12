using RaidDemo.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面的右键出售菜单。
    /// </summary>
    /// <remarks>
    /// 右键菜单与批量出售模式共用同一套结算入口：菜单里的「出售」只是
    /// 构造一个只含单件物品的候选项列表，然后交给批量出售流程。
    /// </remarks>
    public sealed partial class MerchantScreenController
    {
        /// <summary>右键物品弹出出售菜单。</summary>
        private void TryOpenSellMenu(Vector2 pointer)
        {
            if (m_SellMenuRoot == null
                || m_StashView == null
                || !Mouse.current.rightButton.wasPressedThisFrame
                || !m_StashView.TryGetCellAt(pointer, out var cell))
            {
                return;
            }

            var item = m_StashView.Grid.GetAt(cell);
            if (item == null)
            {
                return;
            }

            m_ContextItem = item;
            m_ContextCell = cell;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)m_Root.transform,
                    pointer,
                    null,
                    out var local))
            {
                ((RectTransform)m_SellMenuRoot.transform).anchoredPosition = local;
            }

            m_SellMenuRoot.SetActive(true);
        }

        /// <summary>右键出售菜单的输入。</summary>
        private void UpdateSellMenuInput(Vector2 pointer)
        {
            var overSell = m_SellMenuButton.Contains(pointer);
            var overCancel = m_SellMenuCancelButton.Contains(pointer);
            m_SellMenuButton.SetHovered(overSell);
            m_SellMenuCancelButton.SetHovered(overCancel);

            if (!Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (Mouse.current.rightButton.wasPressedThisFrame)
                {
                    CloseSellMenu();
                }

                return;
            }

            if (overSell)
            {
                var item = m_ContextItem;
                var cell = m_ContextCell;
                CloseSellMenu();
                RequestSell(new[] { new SellCandidate { Item = item, Cell = cell } }, out var message);
                if (message != null)
                {
                    ShowStatus(message, false);
                }

                return;
            }

            if (overCancel)
            {
                CloseSellMenu();
                return;
            }

            // 点击菜单外任意位置关闭，避免菜单一直挂在屏幕上。
            CloseSellMenu();
        }

        /// <summary>关闭右键出售菜单。</summary>
        private void CloseSellMenu()
        {
            if (m_SellMenuRoot != null)
            {
                m_SellMenuRoot.SetActive(false);
            }
        }
    }
}
