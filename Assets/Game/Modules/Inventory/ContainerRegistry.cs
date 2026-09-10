using System.Collections.Generic;
using RaidDemo.Data;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 容器的种类。决定它在界面上属于哪个面板，也决定战局结束时怎么结算。
    /// </summary>
    public enum ContainerKind
    {
        /// <summary>角色随身背包。撤离时内容物归玩家所有。</summary>
        PlayerBackpack = 0,

        /// <summary>地图上的战利品容器（箱子、柜子、尸体）。</summary>
        Loot,

        /// <summary>局外仓库。跨战局保留。</summary>
        Stash,
    }

    /// <summary>
    /// 容器注册表：用整数 ID 定位一个 <see cref="InventoryGrid"/>。
    /// </summary>
    /// <remarks>
    /// <para>为什么需要它：命令里携带的是容器 ID 加格子坐标，而不是物品对象引用。
    /// 这是联机的前提。客户端只能描述"把 3 号容器 (2,1) 的东西移到 7 号容器 (0,0)"，
    /// 服务端据此在权威数据里查找。若命令直接携带物品对象，客户端就等于可以凭空构造物品。</para>
    ///
    /// <para>它同时负责记录容器物品与它内部网格的对应关系。
    /// 这层映射没有放在 <see cref="ItemInstance"/> 上，是因为那会让纯数据层反向依赖背包层，
    /// 形成循环依赖。放在这里，依赖方向保持单向。</para>
    /// </remarks>
    public sealed class ContainerRegistry
    {
        /// <summary>容器 ID 到网格的映射。</summary>
        private readonly Dictionary<int, InventoryGrid> m_Grids;

        /// <summary>容器 ID 到种类的映射。</summary>
        private readonly Dictionary<int, ContainerKind> m_Kinds;

        /// <summary>容器物品的实例号到其内部网格的映射。</summary>
        private readonly Dictionary<int, InventoryGrid> m_NestedByHostInstanceId;

        /// <summary>下一个可用的容器 ID。从 1 开始，0 保留为无效容器。</summary>
        private int m_NextId = 1;

        /// <summary>创建一个空的容器注册表。</summary>
        public ContainerRegistry()
        {
            m_Grids = new Dictionary<int, InventoryGrid>(16);
            m_Kinds = new Dictionary<int, ContainerKind>(16);
            m_NestedByHostInstanceId = new Dictionary<int, InventoryGrid>(8);
        }

        /// <summary>当前已登记的顶层容器 ID 列表，供调试面板与存档遍历使用。</summary>
        public IReadOnlyList<int> ContainerIds
        {
            get
            {
                var ids = new List<int>(m_Grids.Count);
                foreach (var pair in m_Grids)
                {
                    ids.Add(pair.Key);
                }

                // 字典的枚举顺序不保证稳定，排序后调试输出与存档才是可复现的。
                ids.Sort();
                return ids;
            }
        }

        /// <summary>
        /// 登记一个顶层容器。
        /// </summary>
        /// <param name="grid">容器网格。</param>
        /// <param name="kind">容器种类。</param>
        /// <returns>分配给它的运行时容器 ID；参数为 null 时返回 0。</returns>
        public int Register(InventoryGrid grid, ContainerKind kind)
        {
            if (grid == null)
            {
                return 0;
            }

            var id = m_NextId++;
            m_Grids[id] = grid;
            m_Kinds[id] = kind;
            return id;
        }

        /// <summary>按 ID 查找容器网格。</summary>
        /// <param name="containerId">容器 ID。</param>
        /// <param name="grid">找到的网格。</param>
        /// <returns>存在返回 true。</returns>
        public bool TryGetGrid(int containerId, out InventoryGrid grid)
        {
            return m_Grids.TryGetValue(containerId, out grid);
        }

        /// <summary>按 ID 查找容器种类。</summary>
        /// <param name="containerId">容器 ID。</param>
        /// <param name="kind">找到的种类。</param>
        /// <returns>存在返回 true。</returns>
        public bool TryGetKind(int containerId, out ContainerKind kind)
        {
            return m_Kinds.TryGetValue(containerId, out kind);
        }

        /// <summary>
        /// 登记一个容器物品及其内部网格（例如背包与它的内部空间）。
        /// </summary>
        /// <param name="containerItem">容器物品。</param>
        /// <param name="grid">它的内部网格。</param>
        /// <returns>该容器物品的实例号；参数非法时返回 0。</returns>
        public int RegisterNested(ItemInstance containerItem, InventoryGrid grid)
        {
            if (containerItem == null || grid == null)
            {
                return 0;
            }

            m_NestedByHostInstanceId[containerItem.InstanceId] = grid;
            return containerItem.InstanceId;
        }

        /// <summary>按容器物品的实例号查找它的内部网格。</summary>
        /// <param name="hostItemInstanceId">容器物品的实例号。</param>
        /// <param name="grid">找到的内部网格。</param>
        /// <returns>存在返回 true。</returns>
        public bool TryGetNested(int hostItemInstanceId, out InventoryGrid grid)
        {
            return m_NestedByHostInstanceId.TryGetValue(hostItemInstanceId, out grid);
        }

        /// <summary>
        /// 取出容器物品的内部网格，没有则按物品定义创建一个。
        /// </summary>
        /// <param name="containerItem">容器物品。</param>
        /// <returns>内部网格；物品不是容器时返回 null。</returns>
        /// <remarks>
        /// <para>容器物品的内部空间是**按需创建**的：玩家捡到 20 个背包，
        /// 只有真正打开过的那些才会占用内存。这是深度限制之外的第二层防护，
        /// 地图上再多容器也不会产生额外开销。</para>
        ///
        /// <para>新网格的深度固定为 1。因为嵌套上限是 2 层，
        /// 深度为 1 的网格本身已经是最后一层，不需要再往上累加。</para>
        /// </remarks>
        public InventoryGrid GetOrCreateNested(ItemInstance containerItem)
        {
            if (containerItem == null || !containerItem.Definition.IsContainer)
            {
                return null;
            }

            if (m_NestedByHostInstanceId.TryGetValue(containerItem.InstanceId, out var existing))
            {
                return existing;
            }

            var size = containerItem.Definition.ContainerGridSize;
            var label = containerItem.Definition.DisplayName + " 内部";
            var grid = new InventoryGrid(size.Width, size.Height, label, depth: 1, hostItem: containerItem);
            m_NestedByHostInstanceId[containerItem.InstanceId] = grid;
            return grid;
        }

        /// <summary>清空注册表。仅供测试用例之间隔离使用。</summary>
        public void Clear()
        {
            m_Grids.Clear();
            m_Kinds.Clear();
            m_NestedByHostInstanceId.Clear();
            m_NextId = 1;
        }
    }
}
