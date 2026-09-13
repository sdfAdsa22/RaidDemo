namespace RaidDemo.Simulation
{
    /// <summary>
    /// 服务器下发给客户端的单个玩家快照。
    /// </summary>
    /// <remarks>
    /// <para>它比 <see cref="PlayerMoveState"/> 多两样东西，两样都是客户端必需的：</para>
    ///
    /// <list type="bullet">
    /// <item><description><b>玩家标识</b>：一个包里可能含多名玩家，接收方要能对上号。</description></item>
    /// <item><description><b>已处理到的输入序号</b>：客户端据此丢弃预测历史、判断是否需要回滚。</description></item>
    /// </list>
    ///
    /// <para><b>服务器时间</b>同样随身携带：远端角色的插值缓冲需要按时间排序，
    /// 而客户端的本地时钟与服务器并不一致，只能用服务器给的时间戳排序。</para>
    ///
    /// <para><b>为什么带的是全量快照而不是可见状态：</b>客户端回滚重放时，
    /// 起点必须是服务器那一时刻的**完整**状态，包括体力恢复计时器。
    /// 若只带位置朝向、计时器在客户端靠猜，重放出来的体力曲线就会与服务器不同——
    /// 症状是「被拉回一次之后跑得更久」，极难定位。多传 4 个字节换掉这类问题很划算。</para>
    /// </remarks>
    public readonly struct PlayerSnapshot
    {
        /// <summary>创建快照。</summary>
        public PlayerSnapshot(
            int playerId,
            uint lastProcessedSequence,
            double serverTime,
            in PlayerMovementSnapshot movement)
        {
            PlayerId = playerId;
            LastProcessedSequence = lastProcessedSequence;
            ServerTime = serverTime;
            Movement = movement;
        }

        /// <summary>玩家标识。</summary>
        public int PlayerId { get; }

        /// <summary>服务器已经处理到的该玩家输入序号。</summary>
        public uint LastProcessedSequence { get; }

        /// <summary>快照对应的服务器仿真时间（秒）。</summary>
        public double ServerTime { get; }

        /// <summary>该时刻的完整移动状态（含体力恢复计时器）。</summary>
        public PlayerMovementSnapshot Movement { get; }

        /// <summary>该时刻对外可见的移动状态，供远端插值与表现层使用。</summary>
        public PlayerMoveState State => Movement.State;

        /// <summary>转换为全量快照，供「回滚重放」作为起点使用。</summary>
        public PlayerMovementSnapshot ToMovementSnapshot()
        {
            return Movement;
        }

        public override string ToString()
        {
            return $"快照(P{PlayerId}, seq={LastProcessedSequence}, t={ServerTime:F3}, {State})";
        }
    }
}
