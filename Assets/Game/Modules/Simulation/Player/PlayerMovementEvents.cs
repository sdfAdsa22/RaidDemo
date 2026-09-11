using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 玩家移动状态发生变化时广播的事件。
    /// </summary>
    /// <remarks>
    /// 这是模拟层与表现层之间的唯一通道。移动模拟不接触任何 Unity 对象，
    /// 只把新状态通过事件发布出去；表现层订阅该事件并把结果应用到 Transform 上。
    /// 这样模拟逻辑可以完全脱离场景测试，而表现层可以随时替换。
    ///
    /// 该事件每帧发布，因此字段刻意保持精简，只传递表现层真正需要的数据。
    /// </remarks>
    public readonly struct PlayerMovementChanged : IEventEnvelope
    {
        public PlayerMovementChanged(
            int playerId,
            Vector2F position,
            Vector2F facing,
            float speed,
            float stamina,
            float maxStamina,
            bool isSprinting,
            bool isExhausted,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            PlayerId = playerId;
            Position = position;
            Facing = facing;
            Speed = speed;
            Stamina = stamina;
            MaxStamina = maxStamina;
            IsSprinting = isSprinting;
            IsExhausted = isExhausted;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>所属玩家。</summary>
        public int PlayerId { get; }

        /// <summary>新的位置。</summary>
        public Vector2F Position { get; }

        /// <summary>新的朝向（单位向量）。</summary>
        public Vector2F Facing { get; }

        /// <summary>当前移动速度。</summary>
        public float Speed { get; }

        /// <summary>当前体力值。</summary>
        public float Stamina { get; }

        /// <summary>
        /// 体力上限。
        /// </summary>
        /// <remarks>
        /// 事件里带上上限，订阅方就不需要再去查配置。
        /// 体力条这类表现层元素本来就只关心"占满值的百分之多少"，
        /// 让它自己去别处找上限只会多一条依赖。
        /// </remarks>
        public float MaxStamina { get; }

        /// <summary>是否正在奔跑。</summary>
        public bool IsSprinting { get; }

        /// <summary>是否处于力竭状态。</summary>
        public bool IsExhausted { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source => "simulation.movement";

        /// <inheritdoc />
        public uint Sequence { get; }

        public override string ToString()
        {
            return $"PlayerMovementChanged(P{PlayerId}, {Position}, speed={Speed:F2}, stamina={Stamina:F1})";
        }
    }
}
