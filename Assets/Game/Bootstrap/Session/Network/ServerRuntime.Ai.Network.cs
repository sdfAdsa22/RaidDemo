using RaidDemo.AI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的 AI 网络部分：把敌人的权威状态广播给客户端。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须下行：</b>P2-2 起客户端不再自己跑 AI——
    /// 它不知道敌人在哪、在干什么。没有这条通道，联机模式里敌人对客户端就是"不存在"；
    /// 而客户端若自己补一份，就又回到了"两份真相"。</para>
    ///
    /// <para><b>发什么：</b>位置、朝向、状态、生死、生命。客户端用前四项做插值与表现，
    /// 生命留给调试与将来的血条。弹药不发——客户端不需要知道敌人还剩几发子弹。</para>
    ///
    /// <para><b>节拍与玩家快照一致（20 Hz）：</b>两者在客户端是同一帧被插值的，
    /// 节拍不同会让"敌人打中了队友"这种事件在两边的画面上错开半拍。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>单条敌人快照的大致字节数（估算，用于预分配发送缓冲）。</summary>
        private const int EnemySnapshotBytesEstimate = 48;

        /// <summary>
        /// 把当前全部敌人的状态广播给所有客户端。
        /// </summary>
        /// <remarks>
        /// <para>阵亡的敌人也会继续出现在快照里（<c>IsAlive = false</c>）：客户端据此保留尸体并停住它。
        /// 少了这一步，敌人被打死后会从客户端画面上"直接消失"，而玩家期待的是留下一具尸体。</para>
        ///
        /// <para>没有客户端连接时直接返回：NGO 在无接收者时发送命名消息会抛异常（与玩家快照同一条约束）。</para>
        /// </remarks>
        private void BroadcastEnemySnapshots()
        {
            if (m_AiDirector == null)
            {
                return;
            }

            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null
                || manager.ConnectedClientsIds.Count == 0)
            {
                return;
            }

            var agents = m_AiDirector.Agents;
            var entries = new EnemyStateMessage[agents.Count];

            for (var i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                var enemyId = ResolveEntityId(agent.CombatantId);
                if (enemyId == 0)
                {
                    // 编号翻译不出来（理论上不会发生）：跳过这一条而不是发一个 0 号敌人，
                    // 客户端拿到 0 会把它当成玩家编号去建视图。
                    continue;
                }

                entries[i] = EnemyStateMessage.From(
                    enemyId,
                    agent.Position,
                    agent.FacingDegrees,
                    agent.CurrentState,
                    agent.IsAlive,
                    agent.Health);
            }

            var batch = new EnemySnapshotBatchMessage
            {
                ServerTime = m_World != null ? m_World.SimulationTime : 0d,
                Enemies = entries,
            };

            var capacity = 64 + (entries.Length * EnemySnapshotBytesEstimate);
            using (var writer = new FastBufferWriter(capacity, Allocator.Temp))
            {
                writer.WriteValueSafe(batch);
                manager.CustomMessagingManager.SendNamedMessageToAll(
                    EnemyNetworkChannel.SnapshotMessageName,
                    writer);
            }
        }
    }
}
