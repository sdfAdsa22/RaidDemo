using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的「整理仓库」入口（标题栏右侧）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一个文件：</b>本界面的主文件与布局文件都已接近单文件 400 行上限，
    /// 而"按钮什么时候显示、怎么响应鼠标"是一组独立且完整的行为，拆在这里对既有代码的改动最小。</para>
    ///
    /// <para><b>为什么只在仓库上出现：</b>战利品箱里是别人的东西，排它没有意义；
    /// 而随身背包的整理已经由 F 键承担（高频动作放在键上比移鼠标更快）。</para>
    ///
    /// <para><b>为什么按钮每帧重新判断显隐而不是打开时设置一次：</b>右栏容器会在
    /// 「战利品 ↔ 仓库」之间切换，切换路径有搜刮、按 E 开仓库、以及整块布局重建三条，
    /// 逐个入口去同步显隐漏一处就会出现"搜刮时也显示整理仓库"。每帧按当前状态判断，
    /// 只有一个真值来源。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>按钮宽度：容得下"整理仓库"四个字与内边距。</summary>
        private const float SortButtonWidth = 150f;

        /// <summary>按钮高度：与商人界面那一排按钮同高，两处观感一致。</summary>
        private const float SortButtonHeight = 40f;

        /// <summary>按钮上边距：与标题文字同一行居中。</summary>
        private const float SortButtonTop = 12f;

        /// <summary>按钮右边距：与面板右缘保持和内容区一致的留白。</summary>
        private const float SortButtonRightMargin = 28f;

        /// <summary>标题栏上的「整理仓库」按钮。界面重建后会被销毁，由下一帧重新创建。</summary>
        private UiButton m_SortStashButton;

        /// <summary>
        /// 右栏当前显示的容器就是仓库。
        /// </summary>
        /// <remarks>只有"准备界面的仓库"才算：战局里没有仓库，这个条件自然为假。</remarks>
        private bool IsShowingStash
        {
            get
            {
                return m_PrepStashContainerId > 0
                    && m_LootContainerId == m_PrepStashContainerId;
            }
        }

        /// <summary>
        /// 刷新「整理仓库」按钮的显隐与悬停，并在点击时发出整理命令。
        /// </summary>
        /// <param name="pointer">鼠标屏幕坐标。</param>
        /// <param name="clicked">本帧是否按下了左键。</param>
        /// <returns>是否消费了本次点击。为真时调用方应当跳过本帧后续的拖拽处理。</returns>
        private bool UpdateSortStashButton(Vector2 pointer, bool clicked)
        {
            EnsureSortStashButton();
            if (m_SortStashButton == null)
            {
                return false;
            }

            var visible = IsShowingStash;
            m_SortStashButton.Rect.gameObject.SetActive(visible);
            if (!visible)
            {
                return false;
            }

            var hovered = m_SortStashButton.Contains(pointer);
            m_SortStashButton.SetHovered(hovered);

            if (!hovered || !clicked)
            {
                return false;
            }

            // 整理的是**右栏的仓库**，而不是 F 键那只整理随身背包：
            // 玩家点的是"整理仓库"这四个字，结果必须与字面一致。
            DispatchSort(m_LootContainerId);
            return true;
        }

        /// <summary>
        /// 保证按钮存在。布局重建会销毁整棵画布，这里检测到旧引用已销毁就重新创建。
        /// </summary>
        private void EnsureSortStashButton()
        {
            if (m_SortStashButton != null && m_SortStashButton.Rect != null)
            {
                return;
            }

            if (m_Root == null)
            {
                return;
            }

            m_SortStashButton = UiFactory.CreateButton(
                (RectTransform)m_Root.transform,
                "整理仓库",
                new Vector2(PanelWidth - SortButtonRightMargin - SortButtonWidth, SortButtonTop),
                new Vector2(SortButtonWidth, SortButtonHeight));
        }
    }
}
