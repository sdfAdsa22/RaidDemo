using System;

namespace RaidDemo.Shared
{
    /// <summary>
    /// 二维浮点向量。
    /// </summary>
    /// <remarks>
    /// <para>为什么不用 UnityEngine.Vector2：共享层（RaidDemo.Shared）禁止引用 UnityEngine 的运行时类型，
    /// 原因是服务端需要以无头模式运行，不应依赖渲染相关程序集；同时这样也能让共享层的逻辑
    /// 在普通单元测试里直接使用，无需启动 Unity。</para>
    ///
    /// <para>本类型与 Unity 的 Vector2 / Vector3 之间通过显式转换衔接，转换代码集中写在
    /// 表现层的适配处，避免两边到处手写 x、y 拆装。</para>
    /// </remarks>
    [Serializable]
    public struct Vector2F : IEquatable<Vector2F>
    {
        /// <summary>水平分量。</summary>
        public float X;

        /// <summary>垂直分量。</summary>
        public float Y;

        public Vector2F(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>零向量，代表无输入。</summary>
        public static Vector2F Zero => new Vector2F(0f, 0f);

        /// <summary>单位向量：向右。</summary>
        public static Vector2F Right => new Vector2F(1f, 0f);

        /// <summary>单位向量：向上。</summary>
        public static Vector2F Up => new Vector2F(0f, 1f);

        /// <summary>向量长度。</summary>
        public float Magnitude => MathF.Sqrt((X * X) + (Y * Y));

        /// <summary>长度的平方。比较向量长度时应优先使用它，避免开方运算。</summary>
        public float SqrMagnitude => (X * X) + (Y * Y);

        /// <summary>是否为近零向量。</summary>
        public bool IsNearlyZero => SqrMagnitude < 1e-8f;

        /// <summary>
        /// 返回同方向的单位向量。零向量返回零向量，而不是产生除零错误。
        /// </summary>
        public Vector2F Normalized
        {
            get
            {
                var magnitude = Magnitude;
                return magnitude < 1e-6f ? Zero : new Vector2F(X / magnitude, Y / magnitude);
            }
        }

        /// <summary>
        /// 把长度限制在指定最大值以内。
        /// </summary>
        /// <param name="maxLength">允许的最大长度，必须为非负值。</param>
        /// <remarks>
        /// 输入向量做归一化后乘以速度，是移动逻辑中的常见做法；
        /// 但手柄摇杆的模拟输入需要保留力度，此时应当用本方法限制长度而不是直接归一化。
        /// </remarks>
        public Vector2F ClampedTo(float maxLength)
        {
            if (maxLength < 0f)
            {
                return Zero;
            }

            var sqr = SqrMagnitude;
            if (sqr <= maxLength * maxLength || sqr < 1e-8f)
            {
                return this;
            }

            var scale = maxLength / MathF.Sqrt(sqr);
            return new Vector2F(X * scale, Y * scale);
        }

        /// <summary>两向量点积。</summary>
        public static float Dot(Vector2F a, Vector2F b)
        {
            return (a.X * b.X) + (a.Y * b.Y);
        }

        /// <summary>两向量间的距离。</summary>
        public static float Distance(Vector2F a, Vector2F b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>两向量间距离的平方。仅需比较远近时使用，可省去开方。</summary>
        public static float SqrDistance(Vector2F a, Vector2F b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (dx * dx) + (dy * dy);
        }

        /// <summary>线性插值。</summary>
        /// <param name="t">插值系数，会被限制在 [0, 1]。</param>
        public static Vector2F Lerp(Vector2F a, Vector2F b, float t)
        {
            var clamped = t < 0f ? 0f : t > 1f ? 1f : t;
            return new Vector2F(a.X + ((b.X - a.X) * clamped), a.Y + ((b.Y - a.Y) * clamped));
        }

        /// <summary>把方向向量转换为角度（度），与 Unity 的约定一致：0 度指向右，逆时针为正。</summary>
        public float ToDegrees()
        {
            return MathF.Atan2(Y, X) * (180f / MathF.PI);
        }

        /// <summary>由角度（度）构造单位向量。</summary>
        public static Vector2F FromDegrees(float degrees)
        {
            var radians = degrees * (MathF.PI / 180f);
            return new Vector2F(MathF.Cos(radians), MathF.Sin(radians));
        }

        public static Vector2F operator +(Vector2F a, Vector2F b)
        {
            return new Vector2F(a.X + b.X, a.Y + b.Y);
        }

        public static Vector2F operator -(Vector2F a, Vector2F b)
        {
            return new Vector2F(a.X - b.X, a.Y - b.Y);
        }

        public static Vector2F operator *(Vector2F a, float scalar)
        {
            return new Vector2F(a.X * scalar, a.Y * scalar);
        }

        public static Vector2F operator *(float scalar, Vector2F a)
        {
            return new Vector2F(a.X * scalar, a.Y * scalar);
        }

        public static Vector2F operator /(Vector2F a, float scalar)
        {
            return scalar == 0f ? Zero : new Vector2F(a.X / scalar, a.Y / scalar);
        }

        public static bool operator ==(Vector2F a, Vector2F b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Vector2F a, Vector2F b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// 精确相等比较。
        /// </summary>
        /// <remarks>
        /// 浮点数的精确比较对游戏逻辑通常没有意义。需要判断两个位置是否足够接近时，
        /// 请使用 <see cref="SqrDistance"/> 与容差比较，而不是依赖本方法。
        /// 本方法主要用于集合查找与测试中的确定性断言。
        /// </remarks>
        public bool Equals(Vector2F other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is Vector2F other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        public override string ToString()
        {
            return $"({X:F3}, {Y:F3})";
        }
    }
}
