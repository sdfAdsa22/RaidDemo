using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Meta;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面的出售部分：右键快捷出售 + 仓库下方的批量出售模式。
    /// </summary>
    /// <remarks>
    /// <para><b>不再有独立的出售页。</b>出售入口常驻在右侧仓库下方，
    /// 因为出售的宾语永远是仓库物品，玩家不需要先切页再去找东西。</para>
    ///
    /// <para>两条入口最终都派发 <c>SellItemsIntent</c>：
    /// 单件是"只有一个引用的批量"，金额、原子性与存档时机完全一致。</para>
    /// </remarks>
    public sealed partial class MerchantScreenController
    {
        /// <summary>批量出售模式下的一个候选项。</summary>
        private sealed class SellCandidate
        {
            public ItemInstance Item;
            public GridPoint Cell;
        }

        /// <summary>刷新仓库下方的出售提示与按钮。</summary>
        private void RefreshSellControls()
        {
            if (m_SellInfoLabel == null || m_SellToggleButton == null)
            {
                return;
            }

            var count = m_SellSelection.Count;
            var total = CalculateSelectionTotal();
            if (m_SellMode)
            {
                m_SellInfoLabel.text = count == 0
                    ? "出售模式：左键点击仓库物品多选；右键或 Esc 退出。"
                    : $"已选 {count} 件，预计收入 {total:N0} 金币。";
                m_SellToggleButton.Label.text = count > 0 ? $"确认出售 ({count})" : "确认出售";
            }
            else
            {
                m_SellInfoLabel.text = "右键仓库物品可直接出售；点击「出售」可多选批量出售。";
                m_SellToggleButton.Label.text = "出售";
            }

            m_SellCancelButton.Rect.gameObject.SetActive(m_SellMode);
            ApplySellSelectionHighlight();
        }

        /// <summary>进入批量出售模式。</summary>
        private void EnterSellMode()
        {
            m_SellMode = true;
            m_SellSelection.Clear();
            CloseSellMenu();
            RefreshSellControls();
        }

        /// <summary>退出批量出售模式并清空选择。</summary>
        private void ExitSellMode()
        {
            m_SellMode = false;
            m_SellSelection.Clear();
            m_ContextItem = null;
            m_ContextCell = default;
            CloseSellMenu();
            RefreshSellControls();
        }

        /// <summary>出售/确认按钮与取消按钮的输入。</summary>
        private void UpdateSellControlsInput(Vector2 pointer)
        {
            if (m_SellToggleButton == null)
            {
                return;
            }

            var hovered = m_SellToggleButton.Contains(pointer);
            var highlighted = m_SellMode && m_SellSelection.Count > 0;
            m_SellToggleButton.Background.color = hovered
                ? ButtonHoverColor
                : highlighted ? new Color(0.24f, 0.66f, 0.40f) : ButtonColor;

            if (hovered && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (m_SellMode)
                {
                    RequestSell(m_SellSelection, out var message);
                    if (message != null)
                    {
                        ShowStatus(message, false);
                    }
                }
                else
                {
                    EnterSellMode();
                }

                return;
            }

            if (m_SellCancelButton == null || !m_SellMode)
            {
                return;
            }

            var cancelHovered = m_SellCancelButton.Contains(pointer);
            m_SellCancelButton.SetHovered(cancelHovered);
            if (cancelHovered && Mouse.current.leftButton.wasPressedThisFrame)
            {
                ExitSellMode();
            }
        }

        /// <summary>出售模式：左键多选，右键退出。</summary>
        private void UpdateSellModeInput(Vector2 pointer)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                ExitSellMode();
                return;
            }

            if (!Mouse.current.leftButton.wasPressedThisFrame
                || m_StashView == null
                || !m_StashView.TryGetCellAt(pointer, out var cell))
            {
                return;
            }

            var item = m_StashView.Grid.GetAt(cell);
            if (item == null)
            {
                return;
            }

            ToggleSellSelection(item, cell);
        }

        /// <summary>切换一件物品的选中状态。</summary>
        private void ToggleSellSelection(ItemInstance item, GridPoint cell)
        {
            var index = -1;
            for (var i = 0; i < m_SellSelection.Count; i++)
            {
                if (ReferenceEquals(m_SellSelection[i].Item, item))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                m_SellSelection.RemoveAt(index);
            }
            else
            {
                m_SellSelection.Add(new SellCandidate { Item = item, Cell = cell });
            }

            RefreshSellControls();
        }

        /// <summary>
        /// 请求出售一组物品。
        /// </summary>
        /// <param name="candidates">要出售的候选项。</param>
        /// <param name="message">需要展示的提示；没有问题时为 null。</param>
        private void RequestSell(IReadOnlyList<SellCandidate> candidates, out string message)
        {
            message = null;
            if (candidates == null || candidates.Count == 0)
            {
                message = "先选择要出售的物品。";
                return;
            }

            var refs = BuildSellRefs(candidates);
            var total = CalculateTotal(candidates);
            if (total >= HighValueSellThreshold)
            {
                m_PendingSellRefs = refs;
                m_PendingSellTotal = total;
                m_ConfirmLabel.text = candidates.Count > 1
                    ? $"确定批量出售 {candidates.Count} 件物品？\n合计 {total:N0} 金币，此操作不可撤销。"
                    : $"确定出售 {candidates[0].Item.Definition.DisplayName}？\n"
                      + $"收购价 {total:N0} 金币，此操作不可撤销。";
                m_ConfirmRoot.SetActive(true);
                return;
            }

            DispatchSell(refs, total);
        }

        /// <summary>高价值出售的二次确认。</summary>
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
                var refs = m_PendingSellRefs;
                var total = m_PendingSellTotal;
                CancelSellConfirm();
                if (refs != null)
                {
                    DispatchSell(refs, total);
                }
            }
            else if (overCancel)
            {
                CancelSellConfirm();
            }
        }

        /// <summary>取消二次确认，不执行出售。</summary>
        private void CancelSellConfirm()
        {
            m_PendingSellRefs = null;
            m_PendingSellTotal = 0;
            if (m_ConfirmRoot != null)
            {
                m_ConfirmRoot.SetActive(false);
            }
        }

        /// <summary>派发批量出售命令。</summary>
        private void DispatchSell(SellItemRef[] refs, int total)
        {
            var result = m_Router.Dispatch(new SellItemsIntent(
                0,
                m_StashContainerId,
                refs));
            if (result.Success)
            {
                ShowStatus($"已出售 {refs.Length} 件物品，获得 {total:N0} 金币。", true);
                // 批量出售成功后退出出售模式，避免连续误卖。
                ExitSellMode();
            }
            else
            {
                ShowStatus(result.Message, false);
            }
        }

        /// <summary>把候选项转换成命令引用。</summary>
        private static SellItemRef[] BuildSellRefs(IReadOnlyList<SellCandidate> candidates)
        {
            var refs = new SellItemRef[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                refs[i] = new SellItemRef(candidates[i].Cell.X, candidates[i].Cell.Y);
            }

            return refs;
        }

        /// <summary>计算一组候选项的预计收入。</summary>
        private static int CalculateTotal(IReadOnlyList<SellCandidate> candidates)
        {
            var total = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                var item = candidates[i].Item;
                total += TraderPricing.GetSellPrice(item.Definition, item.StackCount);
            }

            return total;
        }

        private int CalculateSelectionTotal()
        {
            var total = 0;
            for (var i = 0; i < m_SellSelection.Count; i++)
            {
                var item = m_SellSelection[i].Item;
                total += TraderPricing.GetSellPrice(item.Definition, item.StackCount);
            }

            return total;
        }

        /// <summary>把选中状态画到仓库格子上。</summary>
        private void ApplySellSelectionHighlight()
        {
            if (m_StashView == null)
            {
                return;
            }

            var views = m_StashView.ItemViews;
            for (var i = 0; i < views.Count; i++)
            {
                var selected = false;
                for (var j = 0; j < m_SellSelection.Count; j++)
                {
                    if (ReferenceEquals(m_SellSelection[j].Item, views[i].Item))
                    {
                        selected = true;
                        break;
                    }
                }

                views[i].SetSelected(selected);
                // B + C 方案：未选中压暗，选中保留品质色并使用白色描边与勾。
                views[i].SetDimmed(m_SellMode && !selected);
            }
        }
    }
}
