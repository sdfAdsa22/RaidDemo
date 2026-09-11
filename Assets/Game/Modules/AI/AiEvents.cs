using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// AI 状态变化事件。
    /// </summary>
    /// <remarks>
    /// <para>它是"AI 在干什么"的唯一对外信号，有三个订阅方：
    /// 表现层（灰盒颜色）、调试日志、以及后续的统计与任务系统。</para>
    ///
    /// <para>带上位置与理由，是为了让日志本身就能回答"它为什么突然回头"，
    /// 而不必再去关联别的日志行。事件只在**状态发生变化**时发布，
    /// 不是每帧发布，因此不会造成日志洪水。</para>
    /// </remarks>
    public readonly struct AiStateChangedEvent : IEventEnvelope
    {
        /// <summary>创建状态变化事件。</summary>
        /// <param name="combatantId">AI 在战斗层中的单位标识。</param>
        /// <param name="previous">变化前的状态。</param>
        /// <param name="current">变化后的状态。</param>
        /// <param name="reason">迁移理由。</param>
        /// <param name="position">变化发生时的平面位置。</param>
        /// <param name="time">战局内的时刻（秒）。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        public AiStateChangedEvent(
            int combatantId,
            AiStateId previous,
            AiStateId current,
            string reason,
            Vector2F position,
            float time = 0f,
            double timestamp = 0d)
        {
            CombatantId = combatantId;
            Previous = previous;
            Current = current;
            Reason = reason;
            Position = position;
            Time = time;
            Timestamp = timestamp;
        }

        /// <summary>AI 的单位标识。</summary>
        public int CombatantId { get; }

        /// <summary>变化前的状态。</summary>
        public AiStateId Previous { get; }

        /// <summary>变化后的状态。</summary>
        public AiStateId Current { get; }

        /// <summary>迁移理由。</summary>
        public string Reason { get; }

        /// <summary>发生位置。</summary>
        public Vector2F Position { get; }

        /// <summary>战局内时刻（秒）。</summary>
        public float Time { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "ai.state"; }
        }

        /// <inheritdoc />
        public uint Sequence
        {
            get { return 0u; }
        }

        public override string ToString()
        {
            return $"AI#{CombatantId} {Previous} → {Current}（{Reason}）@{Position}";
        }
    }
}
