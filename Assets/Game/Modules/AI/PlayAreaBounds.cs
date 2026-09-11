using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// AI 可以活动的地面矩形范围。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>撤退状态会沿"背离威胁"的方向推一个目标点。
    /// 如果 AI 正好背对地图边界，这个点会落到地图之外——
    /// 有导航网格时表现为寻路失败（AI 站住不动），没有导航网格时 AI 会直接走出地图。
    /// 在逻辑层先夹一次，是在两个世界里都成立的做法。</para>
    ///
    /// <para>范围由启动层按地图尺寸注入，AI 逻辑因此不需要知道地图有多大。
    /// M5 换成正式地图时只需换一个数值。</para>
    /// </remarks>
    public readonly struct PlayAreaBounds
    {
        /// <summary>创建一个矩形活动范围。</summary>
        /// <param name="min">最小角（含）。</param>
        /// <param name="max">最大角（含）。</param>
        public PlayAreaBounds(Vector2F min, Vector2F max)
        {
            Min = new Vector2F(min.X < max.X ? min.X : max.X, min.Y < max.Y ? min.Y : max.Y);
            Max = new Vector2F(min.X < max.X ? max.X : min.X, min.Y < max.Y ? max.Y : min.Y);
        }

        /// <summary>最小角。</summary>
        public Vector2F Min { get; }

        /// <summary>最大角。</summary>
        public Vector2F Max { get; }

        /// <summary>是否不限制范围。默认值即为不限制，便于测试里省略。</summary>
        public bool IsUnbounded
        {
            get { return Min.IsNearlyZero && Max.IsNearlyZero; }
        }

        /// <summary>把点夹进范围内。不限制范围时原样返回。</summary>
        public Vector2F Clamp(Vector2F point)
        {
            if (IsUnbounded)
            {
                return point;
            }

            return new Vector2F(
                Clamp(point.X, Min.X, Max.X),
                Clamp(point.Y, Min.Y, Max.Y));
        }

        /// <summary>点是否落在范围内（含边界）。</summary>
        public bool Contains(Vector2F point)
        {
            if (IsUnbounded)
            {
                return true;
            }

            return point.X >= Min.X && point.X <= Max.X
                && point.Y >= Min.Y && point.Y <= Max.Y;
        }

        public override string ToString()
        {
            return IsUnbounded ? "PlayAreaBounds(无限制)" : $"PlayAreaBounds({Min} ~ {Max})";
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
