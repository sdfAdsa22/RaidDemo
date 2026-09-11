namespace RaidDemo.Shared
{
    /// <summary>
    /// 玩家换弹意图：请求给当前武器装填弹药。
    /// </summary>
    /// <remarks>
    /// <para>命令**不携带任何参数**——不指明用哪把枪、装多少发、从哪里取弹药。
    /// 这三件事全部由权威侧根据当前状态决定：主武器槽里是什么枪、背包里有哪些弹药、
    /// 弹匣还缺多少。客户端只需要表达"我想换弹"。</para>
    ///
    /// <para>这是本项目命令设计的一贯原则：**命令只表达意图，不描述结果**。
    /// 一旦命令里带上"装 30 发"这样的参数，客户端就获得了凭空生成弹药的能力。</para>
    /// </remarks>
    public readonly struct PlayerReloadIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "player.reload";

        /// <summary>创建换弹意图。</summary>
        /// <param name="playerId">发起玩家。</param>
        /// <param name="sequence">命令序号。</param>
        /// <param name="timestamp">发起时刻（秒）。</param>
        public PlayerReloadIntent(int playerId, uint sequence = 0u, double timestamp = 0d)
        {
            PlayerId = playerId;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PlayerReloadIntent(P{PlayerId}, seq={Sequence})";
        }
    }
}
