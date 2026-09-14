using System.Collections.Generic;
using RaidDemo.Presentation;
using RaidDemo.Simulation;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机移动链路的快照部分：本机对账 + 远端玩家插值。
    /// </summary>
    /// <remarks>
    /// <para>两类数据流刻意分开：一条只关心我自己（快照 → 与预测对账 → 修正表现），
    /// 另一条只关心别人（快照 → 插值缓冲 → 驱动远端视图）。混在一起时任何一处出问题
    /// 都要在读代码时来回跳，而它们的失效表现也很像（都是"位置不对"）。</para>
    /// </remarks>
    internal sealed partial class MultiplayerMovementLink
    {
        /// <summary>
        /// 远端插值渲染延迟（秒）。约等于两条快照的间隔。
        /// </summary>
        /// <remarks>公开给战局侧的敌人插值：玩家与敌人同一节拍到达，延迟必须一致，否则两批角色会错开半帧。</remarks>
        public const float InterpolationDelaySeconds = 0.1f;

        private readonly Dictionary<int, MovementInterpolationBuffer> m_RemoteBuffers =
            new Dictionary<int, MovementInterpolationBuffer>();

        private readonly Dictionary<int, RemotePlayerView> m_RemoteViews =
            new Dictionary<int, RemotePlayerView>();

        private double m_ServerClockAtSnapshot;
        private double m_LocalClockAtSnapshot;
        private double m_NextRemoteReporterTime;

        /// <summary>
        /// 远端玩家视图表（只读）。
        /// </summary>
        /// <remarks>战局侧的自瞄与救援验收脚本要按玩家编号查位置，因此这里对外开放只读视图。</remarks>
        public IReadOnlyDictionary<int, RemotePlayerView> RemoteViews
        {
            get { return m_RemoteViews; }
        }

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
                if (entry.PlayerId == LocalPlayerId)
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
        /// <para><b>容差按速度给：</b>一步（1/60 秒）的位移随速度变化——走路 0.058 米、冲刺 0.108 米。
        /// 固定容差只要小于冲刺一步，正常的时序抖动就会被当成错误来修，于是每次快照都吸附一次
        /// （P-45 的实机现象就是"房主冲刺一卡一卡"）。</para>
        ///
        /// <para><b>改状态与改表现分开：</b>模拟状态必须立刻采用权威值（否则下一步预测起点是错的），
        /// 但表现层只在大偏差时才瞬移；一步级的修正交给模型自身的跟随平滑吸收。</para>
        /// </remarks>
        private void ApplyAuthoritativeSnapshot(in PlayerStateMessage entry)
        {
            if (m_Prediction == null || m_Simulator == null)
            {
                return;
            }

            var authoritative = entry.ToMovementSnapshot();
            var tolerance = MovementReconciliationTuning.ToleranceFor(
                m_Simulator.State.CurrentSpeed, FixedStep);

            var result = m_Prediction.Reconcile(entry.Sequence, authoritative, tolerance);

            if (!result.NeedsRollback)
            {
                return;
            }

            m_Prediction.Replay(m_Simulator, authoritative);

            var state = m_Simulator.State;
            var position = new Vector2(state.Position.X, state.Position.Y);
            var facing = new Vector2(state.Facing.X, state.Facing.Y);

            if (MovementReconciliationTuning.RequiresHardSnap(result.PositionError))
            {
                m_Motor?.SnapTo(position, facing);
            }
            else
            {
                m_Motor?.FollowTo(position, facing);
            }

            Debug.Log(
                $"[联机] 回滚重放：误差 {result.PositionError:F3} 米，"
                + $"重放 {m_Prediction.PendingCount} 条输入（seq={entry.Sequence}，"
                + $"容差 {tolerance:F3} 米）。");

            RollbackCount++;
        }

        /// <summary>
        /// 记下最新的服务器时间，供插值估计"现在服务器是几点"。
        /// </summary>
        /// <remarks>
        /// 两端时钟并不一致，因此不能用本地时间直接取样。做法是记录"收到快照那一刻的服务器时间"
        /// 与当时的本地时间，之后用本地流逝量去推——误差只有一次网络抖动那么大，对插值足够。
        /// </remarks>
        public void NoteServerClock(double serverTime)
        {
            m_ServerClockAtSnapshot = serverTime;
            m_LocalClockAtSnapshot = Time.timeAsDouble;
        }

        /// <summary>
        /// 按本地流逝量推算"现在的服务器时间"。
        /// </summary>
        /// <remarks>敌人快照与玩家快照共用这一份估计（见 <see cref="NoteServerClock"/>），两者因此不会错开。</remarks>
        public double EstimatedServerTime
        {
            get { return m_ServerClockAtSnapshot + (Time.timeAsDouble - m_LocalClockAtSnapshot); }
        }

        /// <summary>把一条远端玩家快照放进它的插值缓冲。</summary>
        private void PushRemoteSnapshot(in PlayerStateMessage entry, double serverTime)
        {
            if (!m_RemoteBuffers.TryGetValue(entry.PlayerId, out var buffer))
            {
                buffer = new MovementInterpolationBuffer();
                m_RemoteBuffers[entry.PlayerId] = buffer;
            }

            var state = entry.ToMoveState();
            buffer.Push(serverTime, state);
        }

        /// <summary>按插值缓冲驱动每个远端玩家视图。</summary>
        private void UpdateRemoteViews(float deltaTime)
        {
            if (m_RemoteBuffers.Count == 0)
            {
                return;
            }

            foreach (var pair in m_RemoteBuffers)
            {
                if (!pair.Value.TrySample(EstimatedServerTime, InterpolationDelaySeconds, out var state))
                {
                    continue;
                }

                var view = EnsureRemoteView(pair.Key, state);
                view?.Apply(state, deltaTime);
            }

            ReportRemotePositions();
        }

        /// <summary>销毁全部远端玩家视图（断线、切场景时调用）。</summary>
        public void ClearRemoteViews()
        {
            foreach (var view in m_RemoteViews.Values)
            {
                if (view != null)
                {
                    Object.Destroy(view.gameObject);
                }
            }

            m_RemoteViews.Clear();
            m_RemoteBuffers.Clear();
        }

        /// <summary>
        /// 验收模式下周期性打印远端玩家位置。
        /// </summary>
        /// <remarks>
        /// 只在 <c>-autowalk</c> 时输出：无头进程没有画面，日志是唯一能证明
        /// "远端角色确实在动"的证据。正常游玩时保持静默。
        /// </remarks>
        private void ReportRemotePositions()
        {
            if (!m_AutoWalk || Time.timeAsDouble < m_NextRemoteReporterTime || m_RemoteViews.Count == 0)
            {
                return;
            }

            m_NextRemoteReporterTime = Time.timeAsDouble + 2d;

            foreach (var pair in m_RemoteViews)
            {
                var view = pair.Value;
                if (view == null)
                {
                    continue;
                }

                var position = view.transform.position;
                Debug.Log($"[联机] 远端玩家 {pair.Key} 位置 ({position.x:F2}, {position.z:F2})");
            }
        }

        /// <summary>按需创建远端玩家视图。</summary>
        private RemotePlayerView EnsureRemoteView(int playerId, in PlayerMoveState state)
        {
            if (m_RemoteViews.TryGetValue(playerId, out var existing) && existing != null)
            {
                return existing;
            }

            // 外观策略：远端玩家先复用本机选择的角色模型。
            // 每名玩家各自的外观要等大厅把选择结果传上来（那时才谈得上"别人选了什么"）。
            var prefab = m_Catalog != null
                ? m_Catalog.FindCharacterPrefab(m_CharacterIdProvider != null ? m_CharacterIdProvider() : null)
                : null;

            var view = RemotePlayerView.Create(
                prefab,
                playerId,
                new Vector3(state.Position.X, 0f, state.Position.Y));

            m_RemoteViews[playerId] = view;
            Debug.Log($"[联机] 远端玩家 {playerId} 已进入视野。");
            return view;
        }

        /// <summary>与服务器断开时由装配层调用：清理远端视图并通知订阅者。</summary>
        public void NotifyDisconnected()
        {
            ClearRemoteViews();
            Disconnected?.Invoke();
        }
    }
}
