using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 感知记忆：记住"最后一次看到或听到目标的位置"，并随时间失效。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要记忆而不是"看不见就当没发生"：</b>那样的话，AI 只要被掩体挡住一帧
    /// 就会立刻忘掉玩家，表现为"跑两步就摆脱"，战斗毫无压力。
    /// 有了记忆，AI 会朝着你消失的方向追过来——这正是搜打撤里最让人紧张的一段。</para>
    ///
    /// <para><b>时间是外部传入的：</b>记忆不知道帧率、不知道 Unity 的存在，
    /// 只知道自己记录的"事件时刻"和调用方给的"现在"。因此测试里可以
    /// 直接构造"过了 10 秒"的断言，而不必真的等 10 秒。</para>
    ///
    /// <para>本类不做任何寻路与决策，它只是一个带时间戳的坐标。</para>
    /// </remarks>
    public sealed class AISensesMemory
    {
        private Vector2F m_LastKnownPosition;
        private float m_LastKnownTime;
        private bool m_HasMemory;

        /// <summary>是否记录过任何目标。</summary>
        public bool HasMemory
        {
            get { return m_HasMemory; }
        }

        /// <summary>最后已知的目标位置。没有记忆时为零向量。</summary>
        public Vector2F LastKnownPosition
        {
            get { return m_LastKnownPosition; }
        }

        /// <summary>最后一次更新记忆的时刻（秒）。</summary>
        public float LastKnownTime
        {
            get { return m_LastKnownTime; }
        }

        /// <summary>
        /// 记录一次目标位置。
        /// </summary>
        /// <param name="position">目标位置。</param>
        /// <param name="now">当前时刻（秒）。</param>
        public void Remember(Vector2F position, float now)
        {
            m_LastKnownPosition = position;
            m_LastKnownTime = now;
            m_HasMemory = true;
        }

        /// <summary>清空记忆。用于目标死亡或战局重置。</summary>
        public void Clear()
        {
            m_LastKnownPosition = Vector2F.Zero;
            m_LastKnownTime = 0f;
            m_HasMemory = false;
        }

        /// <summary>距离最后一次记录过去了多久（秒）。没有记忆时返回正无穷。</summary>
        /// <param name="now">当前时刻（秒）。</param>
        public float TimeSinceLastKnown(float now)
        {
            return m_HasMemory ? now - m_LastKnownTime : float.PositiveInfinity;
        }

        /// <summary>
        /// 记忆是否仍然"新鲜"，即目标跟丢的时间还没超过阈值。
        /// </summary>
        /// <param name="now">当前时刻（秒）。</param>
        /// <param name="memorySeconds">记忆保留时长（秒）。</param>
        public bool IsFresh(float now, float memorySeconds)
        {
            if (!m_HasMemory)
            {
                return false;
            }

            return TimeSinceLastKnown(now) <= memorySeconds;
        }

        /// <summary>
        /// 如果旧记忆已经过期，则把它丢弃。
        /// </summary>
        /// <param name="now">当前时刻（秒）。</param>
        /// <param name="memorySeconds">记忆保留时长（秒）。</param>
        /// <returns>本次调用是否丢弃了记忆。</returns>
        /// <remarks>
        /// 显式丢弃而不是放任不管，是为了让"是否还有目标"这件事只有一个判据。
        /// 否则某个状态判断"记忆存在"、另一个状态判断"记忆新鲜"，两者会给出不同答案。
        /// </remarks>
        public bool DiscardIfExpired(float now, float memorySeconds)
        {
            if (!m_HasMemory || TimeSinceLastKnown(now) <= memorySeconds)
            {
                return false;
            }

            Clear();
            return true;
        }

        public override string ToString()
        {
            return m_HasMemory
                ? $"AISensesMemory({m_LastKnownPosition} @ {m_LastKnownTime:F2}s)"
                : "AISensesMemory(空)";
        }
    }
}
