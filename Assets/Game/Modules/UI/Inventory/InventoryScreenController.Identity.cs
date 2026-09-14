using System;
using RaidDemo.Data;
using RaidDemo.Inventory;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的"物品身份"部分：判断两次操作是不是落在同一件东西上。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一份文件：</b>这些判断看起来只是几个字段的比较，但它们决定了
    /// 双击能不能被识别、拖拽松手能不能发出命令，是最容易"看起来对、实机时好时坏"的一类代码，
    /// 因此连注释一起单独放，避免埋在拖拽流程中间。</para>
    ///
    /// <para><b>核心约定：物品对象不能当作身份。</b>联机时服务器每下发一次容器内容，
    /// 客户端就会把网格里的物品重新铺一遍，同一格、同一件东西在同步前后是两个对象
    /// （P4.5 实测：同一格物品的实例号在两次同步之间变化）。因此这里统一用
    /// "容器 + 格子 + 物品定义"来认人——它正好等于玩家眼里的"这一件"。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>上一次点击的物品所在的容器编号。</summary>
        private int m_LastClickContainerId;

        /// <summary>上一次点击的物品所在的格子。</summary>
        private GridPoint m_LastClickOrigin;

        /// <summary>上一次点击的物品定义编号。</summary>
        private string m_LastClickItemId;

        /// <summary>拖拽开始时物品所在的格子（松手时用于兜底识别）。</summary>
        private GridPoint m_DragStartOrigin;

        /// <summary>拖拽开始时物品的定义编号（兜底识别用，不随对象被换掉而失效）。</summary>
        private string m_DragItemId;

        /// <summary>
        /// 这次按下是否构成"双击同一件物品"。
        /// </summary>
        /// <param name="containerId">本次点击的物品所在容器。</param>
        /// <param name="item">本次点击的物品。</param>
        /// <param name="origin">本次点击的物品所在格子。</param>
        /// <param name="now">当前时刻（秒）。</param>
        /// <returns>是双击返回 true。</returns>
        /// <remarks>
        /// 只比对象引用时，只要有一次同步落在两次点击之间，双击就被当成两次单击——
        /// 玩家看到的是"双击有时候管用、有时候完全没反应"，而日志里什么都没有。
        /// </remarks>
        private bool IsSameItemAsLastClick(int containerId, ItemInstance item, GridPoint origin, float now)
        {
            return m_LastClickItem != null
                   && m_LastClickContainerId == containerId
                   && m_LastClickOrigin.X == origin.X
                   && m_LastClickOrigin.Y == origin.Y
                   && string.Equals(m_LastClickItemId, ItemIdOf(item), StringComparison.Ordinal)
                   && now - m_LastClickTime <= DoubleClickSeconds;
        }

        /// <summary>记下这次点击，供下一次点击判断是不是双击。</summary>
        /// <param name="containerId">物品所在容器。</param>
        /// <param name="item">物品实例。</param>
        /// <param name="origin">物品所在格子。</param>
        /// <param name="now">当前时刻（秒）。</param>
        private void RememberClick(int containerId, ItemInstance item, GridPoint origin, float now)
        {
            m_LastClickItem = item;
            m_LastClickContainerId = containerId;
            m_LastClickOrigin = origin;
            m_LastClickItemId = ItemIdOf(item);
            m_LastClickTime = now;
        }

        /// <summary>物品的定义编号；定义缺失时返回空串（不会与任何编号相等）。</summary>
        /// <param name="item">物品实例。</param>
        private static string ItemIdOf(ItemInstance item)
        {
            return item != null && item.Definition != null ? item.Definition.Id : string.Empty;
        }

        /// <summary>
        /// 取被拖拽物品在源容器里的坐标；对象已被服务器换掉时按"同一格同一件东西"兜底。
        /// </summary>
        /// <param name="origin">物品坐标。</param>
        /// <returns>能定位到物品返回 true。</returns>
        /// <remarks>
        /// 联机时一次同步会把网格里的物品重新铺一遍，正在被拖的那一件也可能换成新对象。
        /// 只按对象找坐标的话，这一刻松手就是"什么都没发生"（命令根本发不出去）。
        /// 兜底只认"同一个格子上的同一种物品"，因此不会把搬到别处的东西误认成这一件。
        /// </remarks>
        private bool TryResolveDragOrigin(out GridPoint origin)
        {
            origin = default;

            if (m_DragSource == null || m_DragSource.Grid == null || m_DragItem == null)
            {
                return false;
            }

            if (m_DragSource.Grid.TryGetOrigin(m_DragItem, out origin))
            {
                return true;
            }

            var atCell = m_DragSource.Grid.GetAt(m_DragStartOrigin);
            if (atCell == null
                || !string.Equals(ItemIdOf(atCell), m_DragItemId, StringComparison.Ordinal))
            {
                return false;
            }

            origin = m_DragStartOrigin;
            return true;
        }
    }
}
