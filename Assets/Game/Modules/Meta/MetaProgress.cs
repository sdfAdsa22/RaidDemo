using RaidDemo.Inventory;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 局外进度：跨战局、跨场景存在的那部分玩家资产。
    /// </summary>
    /// <remarks>
    /// <para><b>它必须挂在跨场景存活的对象上。</b>本项目每开一局都重新加载场景，
    /// 场景里的一切（容器注册表、战局会话、界面）都会被销毁重建；
    /// 只有挂在 <c>DontDestroyOnLoad</c> 对象上的东西才能活过一局。</para>
    ///
    /// <para>仓库网格本身是**同一个对象**被反复登记：每个场景启动时把它登记进
    /// 该场景的容器注册表（用 <see cref="ContainerKind.Stash"/>），
    /// 于是界面与命令层照常按容器 ID 访问，而网格里的物品跨局保留。
    /// 这也是为什么不需要给「仓库」写一套独立的物品搬迁逻辑。</para>
    /// </remarks>
    public sealed class MetaProgress
    {
        /// <summary>仓库网格的尺寸（列 x 行）。</summary>
        /// <remarks>
        /// 批次 1 先做成固定的较大网格而不是无限容量：无限容量需要一套分页或滚动界面，
        /// 而这一批的重点是「战利品有归宿」，不是仓库管理本身。
        /// </remarks>
        public const int StashWidth = 10;

        public const int StashHeight = 8;

        /// <summary>创建一个空的局外进度。</summary>
        public MetaProgress()
        {
            Stash = new InventoryGrid(StashWidth, StashHeight, "仓库");
        }

        /// <summary>仓库网格。跨战局保留，每个场景重新登记进容器注册表。</summary>
        public InventoryGrid Stash { get; }
    }
}
