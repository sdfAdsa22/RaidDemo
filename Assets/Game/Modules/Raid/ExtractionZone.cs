using System;
using RaidDemo.Shared;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 一个撤离点：圆心加半径。
    /// </summary>
    /// <remarks>
    /// <para>判定用「圆心 + 半径」的圆形区域，而不是矩形或触发器：
    /// 圆形对距离的判定没有方向偏好，玩家从任何方向走进来都是一样的体验；
    /// 而且纯数学判定可以在 EditMode 测试与无头服务端里直接跑。</para>
    ///
    /// <para>半径取 3.5 米是刻意的：它明显大于角色体积，
    /// 玩家站进去之后不会因为贴着边缘轻微移动而反复进出、进度被反复重置。</para>
    /// </remarks>
    public sealed class ExtractionZone
    {
        private readonly float m_SquaredRadius;

        /// <summary>创建一个撤离点。</summary>
        /// <param name="id">编号，同一张地图内唯一。</param>
        /// <param name="displayName">显示名，用于界面提示。</param>
        /// <param name="center">圆心（水平面坐标）。</param>
        /// <param name="radius">判定半径（米），必须大于 0。</param>
        public ExtractionZone(int id, string displayName, Vector2F center, float radius)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    radius,
                    "撤离点半径必须大于 0，否则玩家永远无法进入。");
            }

            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? $"撤离点 {id}" : displayName;
            Center = center;
            Radius = radius;

            // 平方半径预先算好：判定每帧都要做，开方没有意义。
            m_SquaredRadius = radius * radius;
        }

        /// <summary>撤离点编号。</summary>
        public int Id { get; }

        /// <summary>显示名。</summary>
        public string DisplayName { get; }

        /// <summary>圆心。</summary>
        public Vector2F Center { get; }

        /// <summary>判定半径（米）。</summary>
        public float Radius { get; }

        /// <summary>判断一个位置是否落在本撤离点内。</summary>
        /// <param name="position">待判定的位置。</param>
        /// <returns>在范围内返回 true（边界计入）。</returns>
        public bool Contains(Vector2F position)
        {
            var dx = position.X - Center.X;
            var dy = position.Y - Center.Y;
            return ((dx * dx) + (dy * dy)) <= m_SquaredRadius;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{DisplayName}({Id}) 半径 {Radius:F1} 米";
        }
    }
}
