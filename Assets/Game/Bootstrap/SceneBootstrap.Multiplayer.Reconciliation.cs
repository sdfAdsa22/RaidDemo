using RaidDemo.Simulation;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的对账部分：把服务器快照与本机预测对齐，并把修正贴到表现上。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一份文件：</b>"上行输入 + 远端插值"与"本机对账回滚"是两条独立链路，
    /// 混在一起时最容易出的错是"改了预测却没有同步表现"（P-45 就是这样被放大的：
    /// 容差比一步还小 → 每次快照都回滚 → 每次都硬瞬移，画面变成一卡一卡）。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>本局因对账超差而回滚重放的次数（诊断与验收用）。</summary>
        /// <remarks>
        /// 做成可读的计数而不是只丢在日志里：判读"移动手感是否有问题"时，
        /// "一局回滚了几次、误差多大"是最直接的量化指标（P4.5 实机定位"一卡一卡"时用过）。
        /// </remarks>
        public int RollbackCount
        {
            get { return m_RollbackCount; }
        }

        private int m_RollbackCount;

        /// <summary>
        /// 收到一批快照：本机玩家对账，其他玩家进插值缓冲。
        /// </summary>
        private void OnSnapshotBatch(ulong senderId, FastBufferReader reader)
        {
            var batch = default(PlayerSnapshotBatchMessage);
            reader.ReadValueSafe(out batch);

            if (batch.Players == null)
            {
                return;
            }

            NoteServerClock(batch.ServerTime);

            foreach (var entry in batch.Players)
            {
                if (entry.PlayerId == m_LocalPlayerId)
                {
                    ApplyAuthoritativeSnapshot(entry);
                    continue;
                }

                PushRemoteSnapshot(entry, batch.ServerTime);
            }
        }

        /// <summary>
        /// 用服务器快照校正本机预测。
        /// </summary>
        /// <remarks>
        /// <para><b>容差按速度给：</b>一步（1/60 秒）的位移随速度变化——走路 0.058 米、
        /// 冲刺 0.108 米。固定容差只要小于冲刺一步，正常的时序抖动就会被当成错误来修，
        /// 于是每次快照都吸附一次（P-45）。</para>
        ///
        /// <para><b>改状态与改表现分开：</b>模拟状态必须立刻采用权威值（否则下一步预测起点是错的），
        /// 但表现层只在大偏差时才瞬移；一步级的修正交给模型自身的跟随平滑吸收。</para>
        /// </remarks>
        private void ApplyAuthoritativeSnapshot(in PlayerStateMessage entry)
        {
            var authoritative = entry.ToMovementSnapshot();
            var tolerance = MovementReconciliationTuning.ToleranceFor(
                Movement.State.CurrentSpeed, NetworkFixedStep);

            var result = m_Prediction.Reconcile(entry.Sequence, authoritative, tolerance);

            if (!result.NeedsRollback)
            {
                return;
            }

            m_Prediction.Replay(Movement, authoritative);

            var state = Movement.State;
            var position = new Vector2(state.Position.X, state.Position.Y);
            var facing = new Vector2(state.Facing.X, state.Facing.Y);

            if (MovementReconciliationTuning.RequiresHardSnap(result.PositionError))
            {
                m_PlayerMotor?.SnapTo(position, facing);
            }
            else
            {
                m_PlayerMotor?.FollowTo(position, facing);
            }

            Debug.Log(
                $"[联机] 回滚重放：误差 {result.PositionError:F3} 米，" +
                $"重放 {m_Prediction.PendingCount} 条输入（seq={entry.Sequence}，" +
                $"容差 {tolerance:F3} 米）。");

            m_RollbackCount++;
        }
    }
}
