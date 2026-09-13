using RaidDemo.AI;
using RaidDemo.Shared;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 下行快照里的单个敌人状态。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么位置与状态一起发：</b>客户端要画的不只是"敌人在哪"，
    /// 还有"敌人在干什么"（灰盒阶段是状态颜色，正式美术之后是动画）。
    /// 分两条消息发会让两者错帧——表现为"敌人已经转向了，颜色还是上一个状态的"。</para>
    ///
    /// <para><b>坐标用平面坐标：</b>与 <see cref="PlayerStateMessage"/> 一致。
    /// 高度是场景信息，由客户端自己采样（<c>GroundProbe</c>），
    /// 这样网络层不必关心地图有几层地面。</para>
    /// </remarks>
    public struct EnemyStateMessage : INetworkSerializable
    {
        /// <summary>敌人编号（<see cref="ServerRuntime.EnemyIdBase"/> 起，客户端靠它区分玩家与敌人）。</summary>
        public int EnemyId;

        /// <summary>位置（水平面）。</summary>
        public Vector2 Position;

        /// <summary>朝向角度（度），与模拟层同一套约定。</summary>
        public float FacingDegrees;

        /// <summary>AI 状态（<see cref="AiStateId"/> 的字节值）。</summary>
        public byte State;

        /// <summary>是否存活。阵亡后客户端保留尸体但停止更新动画。</summary>
        public bool IsAlive;

        /// <summary>当前生命值。用于调试与（将来的）血条。</summary>
        public float Health;

        /// <summary>由权威状态构造一条快照。</summary>
        /// <param name="enemyId">敌人编号。</param>
        /// <param name="position">平面位置。</param>
        /// <param name="facingDegrees">朝向角度。</param>
        /// <param name="state">AI 状态。</param>
        /// <param name="isAlive">是否存活。</param>
        /// <param name="health">当前生命值。</param>
        public static EnemyStateMessage From(
            int enemyId,
            Vector2F position,
            float facingDegrees,
            AiStateId state,
            bool isAlive,
            float health)
        {
            return new EnemyStateMessage
            {
                EnemyId = enemyId,
                Position = new Vector2(position.X, position.Y),
                FacingDegrees = facingDegrees,
                State = (byte)state,
                IsAlive = isAlive,
                Health = health,
            };
        }

        /// <summary>解析出 AI 状态。未知取值退回巡逻，避免客户端因脏数据抛异常。</summary>
        public AiStateId ResolveState()
        {
            return State <= (byte)AiStateId.Retreat ? (AiStateId)State : AiStateId.Patrol;
        }

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref EnemyId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref FacingDegrees);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref IsAlive);
            serializer.SerializeValue(ref Health);
        }
    }

    /// <summary>
    /// 下行的一批敌人快照。
    /// </summary>
    /// <remarks>
    /// 与玩家快照同一节拍（20 Hz）、同一条时间轴：敌人与玩家必须在同一批里保持一致的时序，
    /// 否则"敌人打中了刚从我身边走过的队友"这类事件在两边的画面里会对不上。
    /// 数组长度需要显式写入（理由同 <see cref="PlayerSnapshotBatchMessage"/>）。
    /// </remarks>
    public struct EnemySnapshotBatchMessage : INetworkSerializable
    {
        /// <summary>这批快照对应的服务器仿真时间（秒）。</summary>
        public double ServerTime;

        /// <summary>各敌人状态。</summary>
        public EnemyStateMessage[] Enemies;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTime);

            var count = Enemies == null ? 0 : Enemies.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                Enemies = new EnemyStateMessage[count];
            }

            for (var i = 0; i < count; i++)
            {
                Enemies[i].NetworkSerialize(serializer);
            }
        }
    }
}
