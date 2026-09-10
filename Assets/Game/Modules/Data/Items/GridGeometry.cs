using System;

namespace RaidDemo.Data
{
    /// <summary>
    /// 网格坐标：整数二维坐标，原点 (0,0) 位于容器左上角，X 向右、Y 向下。
    /// </summary>
    /// <remarks>
    /// <para>为什么不用 Unity 的 <c>Vector2Int</c>：本程序集（<c>RaidDemo.Data</c>）是纯逻辑层，
    /// 不允许引用 UnityEngine，这样同一份规则代码将来可以在无头服务端上运行。</para>
    ///
    /// <para>坐标方向与 UI 一致（Y 向下），而不是与数学课本一致（Y 向上）。
    /// 这一选择是为了让"背包第 2 行第 3 列"在代码、UI 与日志里指的是同一个格子，
    /// 省掉一次每次都要在脑子里做的翻转。</para>
    /// </remarks>
    public readonly struct GridPoint : IEquatable<GridPoint>
    {
        /// <summary>创建网格坐标。</summary>
        /// <param name="x">横向坐标，0 表示最左列。</param>
        /// <param name="y">纵向坐标，0 表示最上行。</param>
        public GridPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>横向坐标（列）。</summary>
        public int X { get; }

        /// <summary>纵向坐标（行）。</summary>
        public int Y { get; }

        /// <inheritdoc />
        public bool Equals(GridPoint other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GridPoint other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // 17 与 31 是经典的哈希混合因子：小质数足以在这里打散"行优先"坐标的规律性。
            unchecked
            {
                return (X * 31) ^ (Y * 17);
            }
        }

        /// <summary>相等比较。</summary>
        public static bool operator ==(GridPoint left, GridPoint right)
        {
            return left.Equals(right);
        }

        /// <summary>不等比较。</summary>
        public static bool operator !=(GridPoint left, GridPoint right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"({X},{Y})";
        }
    }

    /// <summary>
    /// 网格尺寸：宽与高的格子数。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GridPoint"/> 一样是纯逻辑类型，不依赖引擎。
    /// 物品的占地尺寸与容器的容量都用它表达，因此旋转只是宽高互换这一个操作。
    /// </remarks>
    public readonly struct GridSize : IEquatable<GridSize>
    {
        /// <summary>创建网格尺寸。</summary>
        /// <param name="width">宽度（列数），必须大于 0。</param>
        /// <param name="height">高度（行数），必须大于 0。</param>
        public GridSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        /// <summary>宽度（列数）。</summary>
        public int Width { get; }

        /// <summary>高度（行数）。</summary>
        public int Height { get; }

        /// <summary>占用的格子总数。用于自动整理时按"占地从大到小"排序。</summary>
        public int CellCount
        {
            get { return Width * Height; }
        }

        /// <summary>尺寸是否合法（宽高均为正）。</summary>
        public bool IsValid
        {
            get { return Width > 0 && Height > 0; }
        }

        /// <summary>
        /// 返回宽高互换后的尺寸，即旋转 90 度的结果。
        /// </summary>
        /// <remarks>
        /// 旋转在数据上只影响占地区域，不改变物品本身的任何属性，
        /// 因此这个方法可以放心地用在"预判"路径上——它不产生任何副作用。
        /// </remarks>
        public GridSize Rotated()
        {
            return new GridSize(Height, Width);
        }

        /// <inheritdoc />
        public bool Equals(GridSize other)
        {
            return Width == other.Width && Height == other.Height;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GridSize other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Width * 31) ^ (Height * 17);
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Width}x{Height}";
        }
    }
}
