using RaidDemo.Simulation;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 客户端上行的一条移动输入。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么只发方向而不是位置：</b>位置由服务器算。客户端若直接上报位置，
    /// 就等于拥有了瞬移能力，服务端无从校验。方向 + 序号 + 时间戳是"我想做什么"的最小描述。</para>
    ///
    /// <para>序号用于服务器去重与客户端对账；时间戳只作为诊断信息记录，
    /// 不参与任何判定——客户端的时钟不可信。</para>
    /// </remarks>
    public struct PlayerInputMessage : INetworkSerializable
    {
        /// <summary>移动方向，长度不超过 1。</summary>
        public Vector2 Move;

        /// <summary>朝向（单位向量）。零向量表示保持当前朝向。</summary>
        public Vector2 Look;

        /// <summary>是否请求奔跑（能否跑起来仍由服务器按体力判定）。</summary>
        public bool Sprint;

        /// <summary>
        /// 是否扣着扳机。
        /// </summary>
        /// <remarks>
        /// 搭在移动输入上一起上行：扣扳机是**持续状态**，与移动同频（每固定步一次），
        /// 再单开一条消息只会多一份时序问题。服务器拿它驱动权威武器控制器。
        /// </remarks>
        public bool TriggerHeld;

        /// <summary>
        /// 本步是否请求换弹（边沿触发：客户端按下 R 的那一步置 true）。
        /// </summary>
        /// <remarks>
        /// 换弹是**一次性事件**，与"扣着扳机"这种持续状态不同，因此用边沿而不是电平。
        /// 服务器收到即调用一次换弹请求，重复包不会导致重复换弹。
        /// </remarks>
        public bool ReloadRequested;

        /// <summary>
        /// 本步是否按住"扶起队友"（电平：按住期间每步都为 true）。
        /// </summary>
        /// <remarks>
        /// 与扣扳机同为持续状态，因此搭在同一条输入上：服务器按"每一帧都按住且距离够近"
        /// 累计施救进度，松手即清零（见 <see cref="RaidDemo.Combat.PlayerLifeStateTracker"/>）。
        /// </remarks>
        public bool ReviveHeld;

        /// <summary>单调递增的输入序号。</summary>
        public uint Sequence;

        /// <summary>客户端本地时间戳，仅用于日志与延迟观测。</summary>
        public double Timestamp;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Move);
            serializer.SerializeValue(ref Look);
            serializer.SerializeValue(ref Sprint);
            serializer.SerializeValue(ref TriggerHeld);
            serializer.SerializeValue(ref ReloadRequested);
            serializer.SerializeValue(ref ReviveHeld);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Timestamp);
        }

        public override string ToString()
        {
            return $"输入(seq={Sequence}, move={Move}, look={Look}, sprint={Sprint})";
        }
    }

    /// <summary>
    /// 下行快照中的单个玩家状态。
    /// </summary>
    /// <remarks>
    /// 字段与 <see cref="RaidDemo.Simulation.PlayerMovementSnapshot"/> 一一对应，
    /// 包括体力恢复计时器：客户端回滚重放的起点必须是完整状态，差一个计时器就会让体力曲线对不上。
    /// </remarks>
    public struct PlayerStateMessage : INetworkSerializable
    {
        /// <summary>玩家标识。</summary>
        public int PlayerId;

        /// <summary>服务器已处理到的该玩家输入序号。</summary>
        public uint Sequence;

        /// <summary>位置（水平面）。</summary>
        public Vector2 Position;

        /// <summary>朝向（单位向量）。</summary>
        public Vector2 Facing;

        /// <summary>当前移动速度（米/秒），用于动画与噪音显示。</summary>
        public float Speed;

        /// <summary>当前体力值。</summary>
        public float Stamina;

        /// <summary>距上次奔跑结束的时间，负值表示尚未开始计时。</summary>
        public float SprintEndTimer;

        /// <summary>是否正在奔跑。</summary>
        public bool IsSprinting;

        /// <summary>是否处于力竭状态。</summary>
        public bool IsExhausted;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Facing);
            serializer.SerializeValue(ref Speed);
            serializer.SerializeValue(ref Stamina);
            serializer.SerializeValue(ref SprintEndTimer);
            serializer.SerializeValue(ref IsSprinting);
            serializer.SerializeValue(ref IsExhausted);
        }

        /// <summary>把模拟层的快照转换成可传输的形式。</summary>
        public static PlayerStateMessage From(in PlayerSnapshot snapshot)
        {
            var movement = snapshot.Movement;
            var state = movement.State;

            return new PlayerStateMessage
            {
                PlayerId = snapshot.PlayerId,
                Sequence = snapshot.LastProcessedSequence,
                Position = new Vector2(state.Position.X, state.Position.Y),
                Facing = new Vector2(state.Facing.X, state.Facing.Y),
                Speed = state.CurrentSpeed,
                Stamina = state.Stamina,
                SprintEndTimer = movement.TimeSinceSprintEnd,
                IsSprinting = state.IsSprinting,
                IsExhausted = state.IsExhausted,
            };
        }

        /// <summary>还原成模拟层快照，供回滚重放作为起点。</summary>
        public PlayerMovementSnapshot ToMovementSnapshot()
        {
            return new PlayerMovementSnapshot
            {
                State = new PlayerMoveState
                {
                    Position = new RaidDemo.Shared.Vector2F(Position.x, Position.y),
                    Facing = Facing.sqrMagnitude <= 1e-8f
                        ? RaidDemo.Shared.Vector2F.Right
                        : new RaidDemo.Shared.Vector2F(Facing.x, Facing.y).Normalized,
                    CurrentSpeed = Speed,
                    Stamina = Stamina,
                    IsSprinting = IsSprinting,
                    IsExhausted = IsExhausted,
                },
                TimeSinceSprintEnd = SprintEndTimer,
            };
        }

        /// <summary>还原成可见状态，供远端插值使用。</summary>
        public PlayerMoveState ToMoveState()
        {
            return ToMovementSnapshot().State;
        }
    }

    /// <summary>
    /// 下行的一批快照：一帧里把所有玩家的状态打成一个包。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么按批而不是按玩家：</b>2~4 人同局时，每帧只有寥寥几条状态，
    /// 分开发送等于把固定开销乘以人数。打成一批既省带宽，也让客户端在同一时刻对所有人做插值，
    /// 不会出现"队友 A 已经是新位置、队友 B 还是旧位置"的撕裂。</para>
    ///
    /// <para>数组需要自己序列化长度：NGO 只支持定长类型与它认识的集合，托管数组要显式写入条数。</para>
    /// </remarks>
    public struct PlayerSnapshotBatchMessage : INetworkSerializable
    {
        /// <summary>这批快照对应的服务器仿真时间（秒）。</summary>
        public double ServerTime;

        /// <summary>各玩家状态。</summary>
        public PlayerStateMessage[] Players;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTime);

            var count = Players == null ? 0 : Players.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                Players = new PlayerStateMessage[count];
            }

            for (var i = 0; i < count; i++)
            {
                Players[i].NetworkSerialize(serializer);
            }
        }
    }
}
