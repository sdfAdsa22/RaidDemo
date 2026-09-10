using System;

namespace RaidDemo.Kernel
{
    /// <summary>
    /// 随机数提供者接口。
    /// </summary>
    /// <remarks>
    /// <para>业务代码一律通过本接口获取随机数，不直接使用 UnityEngine.Random 或 System.Random。
    /// 这样做有三个收益：</para>
    /// <list type="number">
    /// <item><description>测试可以注入固定种子，让随机逻辑变得可复现、可断言。</description></item>
    /// <item><description>联机时由服务端持有权威种子，客户端与服务端得到一致的随机结果，避免两端表现不一致。</description></item>
    /// <item><description>算法与平台解耦，不依赖引擎实现细节，服务端无头环境同样可用。</description></item>
    /// </list>
    /// </remarks>
    public interface IRandomProvider
    {
        /// <summary>当前种子。读档或重连后可用它恢复相同的随机序列。</summary>
        uint Seed { get; }

        /// <summary>返回 [0, 1) 区间内的浮点数。</summary>
        float NextFloat();

        /// <summary>返回 [minInclusive, maxExclusive) 区间内的整数。</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>返回 [minInclusive, maxInclusive] 区间内的浮点数。</summary>
        float NextFloat(float minInclusive, float maxInclusive);

        /// <summary>以给定概率返回 true。probability 会被限制在 [0, 1]。</summary>
        bool NextChance(float probability);

        /// <summary>按权重随机选择一个下标。权重之和为 0 时返回 0。</summary>
        int NextWeightedIndex(ReadOnlySpan<int> weights);
    }

    /// <summary>
    /// 确定性的伪随机数生成器，算法为 Mersenne Twister（MT19937）。
    /// </summary>
    /// <remarks>
    /// <para>为什么选用 MT19937 而不是 System.Random：</para>
    /// <list type="bullet">
    /// <item><description>算法完全确定，不随 .NET 版本或平台变化。System.Random 的实现由运行时决定，
    /// 跨平台一致性无法保证，而联机场景要求客户端与服务端产生完全相同的随机序列。</description></item>
    /// <item><description>周期极长（2^19937-1），不存在游戏中可能触及的重复问题。</description></item>
    /// <item><description>实现仅约百行，便于逐行审阅与单元测试。</description></item>
    /// </list>
    ///
    /// <para>本类不是线程安全的。游戏主循环为单线程，若确实需要多线程使用，
    /// 应为每个线程创建独立实例，而不是给本类加锁。</para>
    /// </remarks>
    public sealed class DeterministicRandom : IRandomProvider
    {
        /// <summary>MT19937 的状态数组长度。</summary>
        private const int StateSize = 624;

        /// <summary>状态数组中的中间位置，用于计算 twist 的分段索引。</summary>
        private const int MiddleState = 397;

        /// <summary>MT19937 的矩阵参数。</summary>
        private const uint MatrixA = 0x9908B0DFu;

        /// <summary>状态初始化的乘数，取自参考文献的常数。</summary>
        private const uint InitMultiplier = 1812433253u;

        /// <summary>状态初始化时使用的上位掩码。</summary>
        private const uint InitMask = 0xFFFFFFFFu;

        /// <summary>生成结果时的两个 tempering 掩码。</summary>
        private const uint TemperMaskB = 0x9D2C5680u;
        private const uint TemperMaskC = 0xEFC60000u;

        private readonly uint[] m_State = new uint[StateSize];
        private int m_Index = StateSize;

        /// <summary>
        /// 以指定种子创建生成器。
        /// </summary>
        /// <param name="seed">
        /// 随机种子。同一个种子必然产生同一串结果，因此服务端应把种子随存档与战局记录一起保存，
        /// 以便复现问题或校验客户端状态。
        /// </param>
        public DeterministicRandom(uint seed)
        {
            Seed = seed;
            Initialize(seed);
        }

        /// <summary>无参构造使用系统时间作为种子，适用于纯单机且无需复现的场景。</summary>
        public DeterministicRandom()
            : this((uint)DateTime.UtcNow.Ticks)
        {
        }

        /// <inheritdoc />
        public uint Seed { get; private set; }

        /// <summary>重置随机序列。传入相同种子会得到与初始创建时完全相同的序列。</summary>
        public void Reseed(uint seed)
        {
            Seed = seed;
            Initialize(seed);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 使用 24 位精度（除以 2^24）而不是完整的 32 位，原因是 float 的有效位数只有 24 位。
        /// 若用完整的 32 位做除法，低位会被舍入，结果可能出现恰好等于 1.0 的边界值。
        /// </remarks>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 使用取模实现。当区间跨度较小时（游戏数值几乎都属于此情况），
        /// 取模引入的分布偏差极小，换来的是简单且完全确定的实现。
        /// </remarks>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                return minInclusive;
            }

            var range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        /// <inheritdoc />
        public float NextFloat(float minInclusive, float maxInclusive)
        {
            if (minInclusive >= maxInclusive)
            {
                return minInclusive;
            }

            return minInclusive + (NextFloat() * (maxInclusive - minInclusive));
        }

        /// <inheritdoc />
        public bool NextChance(float probability)
        {
            if (probability <= 0f)
            {
                return false;
            }

            if (probability >= 1f)
            {
                return true;
            }

            return NextFloat() < probability;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 战利品掉落表的核心算法。权重非负，权重之和为 0 时退化为返回第一个下标，
        /// 而不是抛出异常——因为配置表为空或全零属于策划数据问题，
        /// 在游戏中应当表现为不生成物品，而不是让战局崩溃。
        /// </remarks>
        public int NextWeightedIndex(ReadOnlySpan<int> weights)
        {
            if (weights.Length == 0)
            {
                return -1;
            }

            if (weights.Length == 1)
            {
                return 0;
            }

            var total = 0L;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0)
                {
                    total += weights[i];
                }
            }

            if (total <= 0L)
            {
                return 0;
            }

            var roll = (long)(NextFloat() * total);
            if (roll >= total)
            {
                // 浮点舍入可能使 roll 恰好等于 total，此处兜底保证始终落在有效区间内。
                roll = total - 1;
            }

            var accumulated = 0L;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0)
                {
                    continue;
                }

                accumulated += weights[i];
                if (roll < accumulated)
                {
                    return i;
                }
            }

            // 理论上不可达。保留兜底分支，避免未来修改引入静默错误。
            return weights.Length - 1;
        }

        /// <summary>按标准算法初始化状态数组。</summary>
        private void Initialize(uint seed)
        {
            m_State[0] = seed;
            for (var i = 1; i < StateSize; i++)
            {
                var previous = m_State[i - 1] ^ (m_State[i - 1] >> 30);
                m_State[i] = unchecked((InitMultiplier * previous) + (uint)i) & InitMask;
            }

            m_Index = StateSize;
        }

        /// <summary>生成一个 32 位随机数。</summary>
        private uint NextUInt()
        {
            if (m_Index >= StateSize)
            {
                Twist();
            }

            var value = m_State[m_Index++];

            // tempering：打散状态中的线性相关性，使其通过随机性检验。
            value ^= value >> 11;
            value ^= (value << 7) & TemperMaskB;
            value ^= (value << 15) & TemperMaskC;
            value ^= value >> 18;
            return value;
        }

        /// <summary>执行状态轮换（twist），为接下来的 624 次取值准备好新状态。</summary>
        private void Twist()
        {
            for (var i = 0; i < StateSize; i++)
            {
                var y = (m_State[i] & 0x80000000u) | (m_State[(i + 1) % StateSize] & 0x7FFFFFFFu);
                var next = m_State[(i + MiddleState) % StateSize] ^ (y >> 1);

                if ((y & 1u) != 0u)
                {
                    next ^= MatrixA;
                }

                m_State[i] = next;
            }

            m_Index = 0;
        }
    }
}
