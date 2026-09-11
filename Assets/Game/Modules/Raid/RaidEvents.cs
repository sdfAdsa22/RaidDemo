using RaidDemo.Kernel;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 战局开始事件。
    /// </summary>
    /// <remarks>
    /// 界面用它来初始化倒计时与击杀数显示。把「开始了」做成事件而不是让界面轮询，
    /// 是为了让重开一局的流程不需要显式通知每一个订阅方。
    /// </remarks>
    public readonly struct RaidStartedEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        /// <param name="playerCombatantId">玩家的战斗单位标识。</param>
        /// <param name="durationSeconds">本局总时长（秒）。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public RaidStartedEvent(
            int playerCombatantId,
            float durationSeconds,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            PlayerCombatantId = playerCombatantId;
            DurationSeconds = durationSeconds;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>玩家的战斗单位标识。</summary>
        public int PlayerCombatantId { get; }

        /// <summary>本局总时长（秒）。</summary>
        public float DurationSeconds { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.session"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"RaidStartedEvent(时长={DurationSeconds:F0} 秒)";
        }
    }

    /// <summary>
    /// 战局结束事件。
    /// </summary>
    /// <remarks>
    /// <para>结算界面、统计与后续的仓库系统都从这里取结果。
    /// 事件里同时带上击杀数与存活时长，是为了让订阅方不必再去反查战局会话——
    /// 事件自带全部结论，订阅方就不需要持有会话引用，耦合更少。</para>
    /// </remarks>
    public readonly struct RaidEndedEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        /// <param name="outcome">结局。</param>
        /// <param name="elapsedSeconds">存活时长（秒）。</param>
        /// <param name="kills">击杀数。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public RaidEndedEvent(
            RaidOutcome outcome,
            float elapsedSeconds,
            int kills,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            Outcome = outcome;
            ElapsedSeconds = elapsedSeconds;
            Kills = kills;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>结局。</summary>
        public RaidOutcome Outcome { get; }

        /// <summary>存活时长（秒）。</summary>
        public float ElapsedSeconds { get; }

        /// <summary>击杀数。</summary>
        public int Kills { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.session"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"RaidEndedEvent({Outcome}, 存活 {ElapsedSeconds:F1} 秒, 击杀 {Kills})";
        }
    }

    /// <summary>
    /// 撤离读秒进度变化事件。
    /// </summary>
    /// <remarks>
    /// 只在进度真正变化或状态切换时发布，而不是每帧无条件发布：
    /// 订阅方（界面）本来就要重绘，无变化的重复事件只是浪费。
    /// </remarks>
    public readonly struct ExtractionProgressChangedEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        /// <param name="zoneId">撤离点编号，0 表示已离开全部撤离点。</param>
        /// <param name="progress01">进度（0~1）。</param>
        /// <param name="isActive">是否正在读秒。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public ExtractionProgressChangedEvent(
            int zoneId,
            float progress01,
            bool isActive,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            ZoneId = zoneId;
            Progress01 = progress01;
            IsActive = isActive;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>撤离点编号。</summary>
        public int ZoneId { get; }

        /// <summary>进度（0~1）。</summary>
        public float Progress01 { get; }

        /// <summary>是否正在读秒。</summary>
        public bool IsActive { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.extraction"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"ExtractionProgressChangedEvent(撤离点={ZoneId}, 进度={Progress01:P0}, 进行中={IsActive})";
        }
    }

    /// <summary>
    /// 撤离读秒完成事件。
    /// </summary>
    /// <remarks>
    /// 与进度事件分开，是为了让「撤离成立」这件事只有一个触发点：
    /// 订阅方不必去判断「进度是否到了 1」，那正是最容易写错的地方。
    /// </remarks>
    public readonly struct ExtractionCompletedEvent : IEventEnvelope
    {
        /// <summary>创建事件。</summary>
        /// <param name="zoneId">撤离点编号。</param>
        /// <param name="displayName">撤离点显示名。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public ExtractionCompletedEvent(
            int zoneId,
            string displayName,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            ZoneId = zoneId;
            DisplayName = displayName;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>撤离点编号。</summary>
        public int ZoneId { get; }

        /// <summary>撤离点显示名。</summary>
        public string DisplayName { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "raid.extraction"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"ExtractionCompletedEvent({DisplayName})";
        }
    }
}
