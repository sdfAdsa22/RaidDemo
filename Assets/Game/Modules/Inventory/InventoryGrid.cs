using System;
using System.Collections.Generic;
using RaidDemo.Data;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 网格容器：一个矩形的格子空间，物品按占地区域摆放在其中。
    /// </summary>
    /// <remarks>
    /// <para>本类是整个背包系统的规则核心，承担四件事：占用判定、放置、搬运、整理。
    /// 「角色背包」「战利品箱」「仓库」「背包里的背包」全都是它的实例，
    /// 因此规则只写一遍，测试也只写一遍。</para>
    ///
    /// <para><b>本类不引用 UnityEngine，也不持有任何 UI 引用。</b>
    /// 它可以在 EditMode 测试里直接构造并断言，也能在 M9 的无头服务端上运行。
    /// 界面只是它的一个观察者——所有界面操作都必须经由命令走到这里，
    /// 而不是反过来在 UI 里改数据。</para>
    ///
    /// <para><b>原子性约定：任何返回失败的公开操作，都必须让容器状态与调用前完全一致。</b>
    /// 做法是先全量校验、再提交变更，绝不允许出现"源已移除、目标又没放进去"的中间态。
    /// 因为界面每帧都可能触发预判，部分变更会立刻在玩家眼前暴露成物品凭空消失。</para>
    /// </remarks>
    public sealed partial class InventoryGrid
    {
        /// <summary>宽度（列数）。</summary>
        private readonly int m_Width;

        /// <summary>高度（行数）。</summary>
        private readonly int m_Height;

        /// <summary>便于日志与调试辨认的标签，例如「角色背包」「3 号储物箱」。</summary>
        private readonly string m_Label;

        /// <summary>嵌套深度。顶层容器为 0，容器内的容器为 1，以此类推。</summary>
        private readonly int m_Depth;

        /// <summary>
        /// 拥有本网格的容器物品（例如"这个网格是那个背包的内部空间"）。顶层容器为 null。
        /// </summary>
        /// <remarks>它的唯一用途是拦下"把容器放进它自己的内部网格"这种自环操作。</remarks>
        private readonly ItemInstance m_HostItem;

        /// <summary>网格内的全部物品。顺序不代表摆放顺序，摆放位置以占用表为准。</summary>
        private readonly List<ItemInstance> m_Items;

        /// <summary>
        /// 占用表：每个格子记录占用它的物品，null 表示空格。
        /// </summary>
        /// <remarks>
        /// 用一维数组而不是二维数组或字典：索引计算是 <c>y * width + x</c>，比字典快，
        /// 且占用的内存对背包这种规模（通常不超过几百格）完全可忽略。
        /// 同时它天然支持 O(1) 的"这一格是谁"查询，这正是拖拽预览每帧都要做的事。
        /// </remarks>
        private readonly ItemInstance[] m_Occupancy;

        /// <summary>
        /// 创建一个网格容器。
        /// </summary>
        /// <param name="width">宽度（列数），必须大于 0。</param>
        /// <param name="height">高度（行数），必须大于 0。</param>
        /// <param name="label">调试标签。留空时自动生成尺寸描述。</param>
        /// <param name="depth">嵌套深度，顶层容器为 0。</param>
        /// <param name="hostItem">拥有本网格的容器物品，顶层容器留空。</param>
        public InventoryGrid(
            int width,
            int height,
            string label = null,
            int depth = 0,
            ItemInstance hostItem = null)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "容器宽度必须大于 0。");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "容器高度必须大于 0。");
            }

            if (depth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(depth), depth, "嵌套深度不能为负。");
            }

            m_Width = width;
            m_Height = height;
            m_Depth = depth;
            m_HostItem = hostItem;
            m_Label = string.IsNullOrEmpty(label) ? $"{width}x{height}" : label;
            m_Items = new List<ItemInstance>(16);
            m_Occupancy = new ItemInstance[width * height];
        }

        /// <summary>宽度（列数）。</summary>
        public int Width
        {
            get { return m_Width; }
        }

        /// <summary>高度（行数）。</summary>
        public int Height
        {
            get { return m_Height; }
        }

        /// <summary>调试标签。</summary>
        public string Label
        {
            get { return m_Label; }
        }

        /// <summary>嵌套深度。顶层容器为 0。</summary>
        public int Depth
        {
            get { return m_Depth; }
        }

        /// <summary>拥有本网格的容器物品，顶层容器为 null。</summary>
        public ItemInstance HostItem
        {
            get { return m_HostItem; }
        }

        /// <summary>网格内的全部物品，供 UI 与存档遍历。</summary>
        public IReadOnlyList<ItemInstance> Items
        {
            get { return m_Items; }
        }

        /// <summary>容器内的总重量（千克）。负重系统读取的是它。</summary>
        public float TotalWeightKg
        {
            get
            {
                var total = 0f;
                for (var i = 0; i < m_Items.Count; i++)
                {
                    total += m_Items[i].WeightKg;
                }

                return total;
            }
        }

        /// <summary>格子总数。</summary>
        public int CellCount
        {
            get { return m_Width * m_Height; }
        }

        /// <summary>当前被占用的格子数，用于调试与容量提示。</summary>
        public int OccupiedCellCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < m_Occupancy.Length; i++)
                {
                    if (m_Occupancy[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 查询某个格子上放着什么。
        /// </summary>
        /// <param name="cell">格子坐标。</param>
        /// <returns>占用该格的物品；空格或越界返回 null。</returns>
        /// <remarks>越界返回 null 而不是抛异常：UI 的悬停查询每帧都会跑，越界是常态。</remarks>
        public ItemInstance GetAt(GridPoint cell)
        {
            if (!IsInside(cell))
            {
                return null;
            }

            return m_Occupancy[ToIndex(cell)];
        }

        /// <summary>按行列查询格子内容，等价于 <see cref="GetAt(GridPoint)"/>。</summary>
        public ItemInstance GetAt(int x, int y)
        {
            return GetAt(new GridPoint(x, y));
        }

        /// <summary>判断物品是否在本容器内。</summary>
        public bool Contains(ItemInstance item)
        {
            return item != null && m_Items.Contains(item);
        }

        /// <summary>
        /// 查询物品的左上角坐标。
        /// </summary>
        /// <param name="item">要查询的物品。</param>
        /// <param name="origin">物品左上角坐标；找不到时返回 (0,0)。</param>
        /// <returns>物品在容器内返回 true。</returns>
        /// <remarks>
        /// 直接扫描占用表而不是额外维护一张"物品到坐标"的字典：
        /// 物品占的格子是连续的，第一个遇到的格必然是左上角，
        /// 因此返回第一个命中的索引即可，无需记录与同步第二份数据。
        /// </remarks>
        public bool TryGetOrigin(ItemInstance item, out GridPoint origin)
        {
            origin = default;
            if (item == null)
            {
                return false;
            }

            for (var i = 0; i < m_Occupancy.Length; i++)
            {
                if (!ReferenceEquals(m_Occupancy[i], item))
                {
                    continue;
                }

                origin = new GridPoint(i % m_Width, i / m_Width);
                return true;
            }

            return false;
        }

        /// <summary>判断坐标是否落在网格范围内。</summary>
        public bool IsInside(GridPoint cell)
        {
            return cell.X >= 0 && cell.Y >= 0 && cell.X < m_Width && cell.Y < m_Height;
        }

        /// <summary>判断以 origin 为左上角、尺寸为 size 的矩形是否完全落在网格内。</summary>
        public bool IsInside(GridPoint origin, GridSize size)
        {
            return origin.X >= 0
                   && origin.Y >= 0
                   && size.Width > 0
                   && size.Height > 0
                   && origin.X + size.Width <= m_Width
                   && origin.Y + size.Height <= m_Height;
        }

        /// <summary>把格子坐标换算成占用表索引。</summary>
        private int ToIndex(GridPoint cell)
        {
            return (cell.Y * m_Width) + cell.X;
        }

        /// <summary>取占用表中指定格子的占用者。</summary>
        private ItemInstance GetOccupant(GridPoint cell)
        {
            return m_Occupancy[ToIndex(cell)];
        }

        /// <summary>
        /// 计算物品在指定旋转状态下的占地尺寸。
        /// </summary>
        /// <remarks>
        /// 刻意不复用 <see cref="ItemInstance.OccupiedSize"/>：那是物品**当前**的旋转状态，
        /// 而预判与拖拽需要计算"假如转成另一个方向会占多少格"。
        /// 若复用它，就会出现"拖动时已经把物品转过去了"这类难以追查的副作用。
        /// </remarks>
        private static GridSize SizeFor(IItemDefinition definition, bool rotated)
        {
            return rotated ? definition.GridSize.Rotated() : definition.GridSize;
        }

        /// <summary>把物品写入占用表的指定区域。</summary>
        private void FillCells(ItemInstance item, GridPoint origin, GridSize size)
        {
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    m_Occupancy[ToIndex(new GridPoint(origin.X + x, origin.Y + y))] = item;
                }
            }
        }

        /// <summary>把指定物品在占用表中的所有格子清空。</summary>
        private void ClearCells(ItemInstance item)
        {
            for (var i = 0; i < m_Occupancy.Length; i++)
            {
                if (ReferenceEquals(m_Occupancy[i], item))
                {
                    m_Occupancy[i] = null;
                }
            }
        }

        /// <summary>
        /// 清空容器内的一切。
        /// </summary>
        /// <remarks>
        /// 仅供 <c>Sort</c> 这种"先全部取出再重新摆放"的整批操作使用。
        /// 调用方必须先备份物品与坐标，否则物品会丢失——这是本类里唯一破坏守恒的地方，
        /// 因此它保持私有，不外泄给业务代码。
        /// </remarks>
        private void ClearAll()
        {
            m_Items.Clear();
            Array.Clear(m_Occupancy, 0, m_Occupancy.Length);
        }
    }
}
