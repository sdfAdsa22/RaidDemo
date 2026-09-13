using System;
using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 远端角色的插值缓冲：把 20 Hz 的快照还原成平滑的每帧位置。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须插值：</b>服务器以 20 Hz 下发位置，而画面要跑 60 Hz 以上。
    /// 直接把快照贴到 Transform 上，远端角色会以每秒 20 次的节奏跳跃前进。</para>
    ///
    /// <para><b>为什么渲染要刻意滞后：</b>插值需要一个前后各有一条快照的时间点。
    /// 因此渲染时间取「当前时间 − 延迟」，延迟典型值 100 ms（两条快照的间隔）。
    /// 代价是远端角色比真实位置晚 0.1 秒——这在 PVE 合作里完全可以接受，
    /// 换来的是连续、不抖动的运动轨迹。</para>
    ///
    /// <para>缓冲区按时间单调递增接收快照；乱序或重复到达的快照会被丢弃。</para>
    /// </remarks>
    public sealed class MovementInterpolationBuffer
    {
        private struct Entry
        {
            public double Time;
            public PlayerMoveState State;
        }

        /// <summary>默认容量：20 Hz 下约 1.6 秒，足以覆盖常见网络抖动。</summary>
        public const int DefaultCapacity = 32;

        private readonly Entry[] m_Entries;
        private int m_Count;

        /// <summary>创建插值缓冲。</summary>
        /// <param name="capacity">容量（条）。</param>
        public MovementInterpolationBuffer(int capacity = DefaultCapacity)
        {
            if (capacity < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "插值至少需要 2 条快照。");
            }

            m_Entries = new Entry[capacity];
        }

        /// <summary>缓冲区内的快照条数。</summary>
        public int Count => m_Count;

        /// <summary>是否还没有任何快照。</summary>
        public bool IsEmpty => m_Count == 0;

        /// <summary>最旧一条快照的时间戳；为空时为 0。</summary>
        public double OldestTime => m_Count == 0 ? 0d : m_Entries[0].Time;

        /// <summary>最新一条快照的时间戳；为空时为 0。</summary>
        public double NewestTime => m_Count == 0 ? 0d : m_Entries[m_Count - 1].Time;

        /// <summary>
        /// 收入一条快照。
        /// </summary>
        /// <param name="time">快照时间戳（秒，必须单调递增）。</param>
        /// <param name="state">该时刻的状态。</param>
        /// <returns>被接受时返回 true；时间戳不大于最新一条时返回 false。</returns>
        public bool Push(double time, in PlayerMoveState state)
        {
            if (m_Count > 0 && time <= m_Entries[m_Count - 1].Time)
            {
                return false;
            }

            if (m_Count == m_Entries.Length)
            {
                Array.Copy(m_Entries, 1, m_Entries, 0, m_Entries.Length - 1);
                m_Count--;
            }

            m_Entries[m_Count] = new Entry { Time = time, State = state };
            m_Count++;
            return true;
        }

        /// <summary>清空缓冲。角色进入视野或重生时调用。</summary>
        public void Clear()
        {
            m_Count = 0;
        }

        /// <summary>
        /// 按渲染时间取样。
        /// </summary>
        /// <param name="renderTime">当前渲染时间（秒）。</param>
        /// <param name="delaySeconds">刻意引入的延迟，典型 0.1 秒。</param>
        /// <param name="sampled">取样结果。</param>
        /// <returns>缓冲区非空时返回 true。</returns>
        /// <remarks>
        /// 目标时间落在缓冲区两端之外时取最近的一端，而不是报告失败：
        /// 刚进视野或长时间没有新快照时，宁可显示一个静止的角色，
        /// 也不要让它凭空消失或卡住不动。
        /// </remarks>
        public bool TrySample(double renderTime, float delaySeconds, out PlayerMoveState sampled)
        {
            if (m_Count == 0)
            {
                sampled = default;
                return false;
            }

            var target = renderTime - delaySeconds;

            if (m_Count == 1 || target <= m_Entries[0].Time)
            {
                sampled = m_Entries[0].State;
                return true;
            }

            if (target >= m_Entries[m_Count - 1].Time)
            {
                sampled = m_Entries[m_Count - 1].State;
                return true;
            }

            for (var i = 0; i < m_Count - 1; i++)
            {
                var older = m_Entries[i];
                var newer = m_Entries[i + 1];
                if (target < older.Time || target > newer.Time)
                {
                    continue;
                }

                var span = newer.Time - older.Time;
                var t = span <= 0d ? 0f : (float)((target - older.Time) / span);
                sampled = Interpolate(older.State, newer.State, t);
                return true;
            }

            // 理论上前面的分支已覆盖所有情况，这里只是保底：取最新一条。
            sampled = m_Entries[m_Count - 1].State;
            return true;
        }

        /// <summary>
        /// 丢掉指定时间之前的快照，但至少保留若干条。
        /// </summary>
        /// <param name="time">早于该时间的快照可被丢弃。</param>
        /// <param name="keepAtLeast">至少保留的条数。</param>
        /// <returns>被丢弃的条数。</returns>
        /// <remarks>
        /// 插值始终需要目标时间之前的那一条，因此不能把旧快照全部丢掉——
        /// 这也是本方法带保留条数参数的原因。
        /// </remarks>
        public int TrimBefore(double time, int keepAtLeast = 2)
        {
            var keep = keepAtLeast < 1 ? 1 : keepAtLeast;
            var drop = 0;

            while (m_Count - drop > keep && m_Entries[drop + 1].Time <= time)
            {
                drop++;
            }

            if (drop <= 0)
            {
                return 0;
            }

            Array.Copy(m_Entries, drop, m_Entries, 0, m_Count - drop);
            m_Count -= drop;
            return drop;
        }

        /// <summary>
        /// 两条状态之间插值。
        /// </summary>
        /// <remarks>
        /// 位置与速度做线性插值；朝向按向量插值后重新归一化——两条快照间隔 50 ms 时，
        /// 向量插值与角度插值的肉眼差别可以忽略，而实现简单得多；
        /// 状态位取较早的一条，因为那才是渲染时刻真正成立的状态。
        /// </remarks>
        private static PlayerMoveState Interpolate(in PlayerMoveState older, in PlayerMoveState newer, float t)
        {
            var facing = Vector2F.Lerp(older.Facing, newer.Facing, t);
            facing = facing.IsNearlyZero ? older.Facing : facing.Normalized;

            return new PlayerMoveState
            {
                Position = Vector2F.Lerp(older.Position, newer.Position, t),
                Facing = facing,
                CurrentSpeed = older.CurrentSpeed + ((newer.CurrentSpeed - older.CurrentSpeed) * t),
                Stamina = older.Stamina,
                IsExhausted = older.IsExhausted,
                IsSprinting = older.IsSprinting,
            };
        }
    }
}
