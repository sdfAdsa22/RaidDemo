using RaidDemo.Inventory;
using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的"右栏容器"部分：打开战利品 / 仓库、设置准备界面的容器、关闭战利品面板。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么拆出来：</b>主文件负责"三栏怎么排、输入怎么走"，
    /// 这里负责"右侧那一栏显示哪个容器"——两者改动原因完全不同
    /// （一个随界面调整，一个随搜刮 / 仓库规则调整），而且主文件已接近单文件 400 行上限。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>
        /// 打开指定战利品容器。
        /// </summary>
        /// <param name="containerId">容器 ID。</param>
        /// <param name="displayName">容器显示名，用于标题。</param>
        /// <remarks>
        /// <para>打开动作包含「顺便把界面也显示出来」：搜刮读条完成之后玩家期待的
        /// 就是能立刻搬东西，若还要再按一次 Tab，读条的意义会被这次多余操作冲淡。</para>
        ///
        /// <para>换一个容器时会重建视图而不是复用：视图与容器是一一对应的，
        /// 复用需要把内部状态全部重绑，出错风险远高于重建一个小小的网格面板。</para>
        /// </remarks>
        public void OpenLootContainer(int containerId, string displayName)
        {
            var title = string.IsNullOrEmpty(displayName) ? "战利品" : $"战利品：{displayName}";
            OpenContainerView(containerId, title);
        }

        /// <summary>
        /// 打开仓库（出击准备）。
        /// </summary>
        /// <param name="containerId">仓库容器 ID。</param>
        /// <remarks>
        /// 与战利品共用同一块面板，只是标题不同：对它来说两者都只是「一个容器」。
        /// 标题必须区分开——在准备界面看到「战利品」会让玩家以为自己进了战局。
        /// </remarks>
        public void OpenStash(int containerId)
        {
            OpenContainerView(containerId, "仓库");
        }

        /// <summary>
        /// 设置「准备界面」要显示的仓库容器。
        /// </summary>
        /// <param name="containerId">仓库容器 ID；0 表示本场景没有仓库。</param>
        /// <remarks>由装配层在登记仓库之后调用一次。</remarks>
        public void SetStashContainer(int containerId)
        {
            m_PrepStashContainerId = containerId;
        }

        /// <summary>打开指定容器并显示界面。</summary>
        private void OpenContainerView(int containerId, string title)
        {
            if (containerId <= 0)
            {
                return;
            }

            if (!m_Registry.TryGetGrid(containerId, out var grid))
            {
                return;
            }

            // 右栏宽度参与三栏整体居中：容器列数变化时必须重建整块布局，
            // 只替换右侧网格会让内容仍按旧宽度预留，右侧空出一大块。
            if (m_RightContainerColumns != grid.Width)
            {
                RebuildLayout(
                    m_Loadout.Backpack,
                    m_BackpackContainerId,
                    containerId,
                    title,
                    show: true);
                return;
            }

            if (m_LootContainerId != containerId)
            {
                DestroyLootView();
                m_LootContainerId = containerId;
                m_LootView = CreateGridView(
                    (RectTransform)m_Root.transform,
                    grid,
                    containerId,
                    title,
                    m_LootAnchorTopLeft);
            }

            m_RightContainerTitle = title;

            // 每次打开都主动重画一次：商人买卖只改变了仓库数据，
            // 若界面恰好没收到对应事件，旧视图会显示过期内容。
            RefreshAll();
            SetVisible(true);
        }

        /// <summary>
        /// 关闭战利品面板。
        /// </summary>
        /// <remarks>
        /// 只销毁界面，容器里的物品留在原处：搜刮的产物是「物品换了位置」，
        /// 而不是「界面被关掉了」。这条区别决定了再打开一次箱子时东西还在不在。
        /// </remarks>
        public void CloseLootContainer()
        {
            DestroyLootView();
            m_LootContainerId = 0;
            m_RightContainerTitle = null;
        }

        /// <summary>销毁战利品视图。没有视图时不做任何事。</summary>
        private void DestroyLootView()
        {
            if (m_LootView != null)
            {
                Destroy(m_LootView.gameObject);
                m_LootView = null;
            }
        }
    }
}
