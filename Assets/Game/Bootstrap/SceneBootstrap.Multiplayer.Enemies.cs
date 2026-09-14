using System.Collections.Generic;
using RaidDemo.AI;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.Simulation;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的远端敌人部分：接收快照、插值、维护敌人视图。
    /// </summary>
    /// <remarks>
    /// <para><b>客户端在这里只做一件事：把服务器给的结果画出来。</b>它不跑 AI、
    /// 不判断生死、不算命中——敌人打没打中由服务器的战斗结算决定，
    /// 客户端收到的只是"现在长这样"。</para>
    ///
    /// <para><b>为什么复用玩家那套插值缓冲：</b>两边的数据形状一样（服务器时间 + 平面状态），
    /// 复用意味着"插值怎么做"只有一份实现，改动（比如调整渲染延迟）不会只改到一半。</para>
    ///
    /// <para><b>状态与生死不插值：</b>它们是离散量——插值只会让"敌人已经进入交战"晚半拍显示，
    /// 或者在阵亡的瞬间画出一个半灰半彩的敌人。离散量一律取最新值。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>远端一件敌人的"离散状态"：不插值的那部分。</summary>
        private struct RemoteEnemyState
        {
            /// <summary>AI 状态。</summary>
            public AiStateId State;

            /// <summary>是否存活。</summary>
            public bool IsAlive;
        }

        private readonly Dictionary<int, MovementInterpolationBuffer> m_RemoteEnemyBuffers =
            new Dictionary<int, MovementInterpolationBuffer>();
        private readonly Dictionary<int, RemoteEnemyState> m_RemoteEnemyStates =
            new Dictionary<int, RemoteEnemyState>();
        private readonly Dictionary<int, RemoteEnemyView> m_RemoteEnemyViews =
            new Dictionary<int, RemoteEnemyView>();

        private bool m_EnemyChannelRegistered;
        private double m_NextEnemyReporterTime;

        /// <summary>已建立的远端敌人视图数量（调试与验收用）。</summary>
        public int RemoteEnemyViewCount
        {
            get { return m_RemoteEnemyViews.Count; }
        }

        /// <summary>订阅敌人快照通道；连接成功后调用一次。</summary>
        private void RegisterEnemyChannel()
        {
            if (m_EnemyChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.RegisterNamedMessageHandler(
                EnemyNetworkChannel.SnapshotMessageName,
                OnEnemySnapshotReceived);
            m_EnemyChannelRegistered = true;
        }

        /// <summary>退订敌人快照通道（战局场景销毁时调用）。</summary>
        private void UnregisterEnemyChannel()
        {
            if (!m_EnemyChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.UnregisterNamedMessageHandler(
                EnemyNetworkChannel.SnapshotMessageName);
            m_EnemyChannelRegistered = false;
        }

        /// <summary>
        /// 处理服务器下发的敌人快照：位置进插值缓冲，状态与生死直接覆盖。
        /// </summary>
        private void OnEnemySnapshotReceived(ulong senderId, FastBufferReader reader)
        {
            var batch = default(EnemySnapshotBatchMessage);
            reader.ReadValueSafe(out batch);

            // 服务器时间与玩家快照共用同一条估计：两者同一节拍到达，
            // 各自维护一份时钟只会让敌人与玩家错开半帧。
            m_MovementLink?.NoteServerClock(batch.ServerTime);

            var enemies = batch.Enemies;
            if (enemies == null)
            {
                return;
            }

            for (var i = 0; i < enemies.Length; i++)
            {
                var entry = enemies[i];
                if (entry.EnemyId == 0)
                {
                    continue;
                }

                if (!m_RemoteEnemyBuffers.TryGetValue(entry.EnemyId, out var buffer))
                {
                    buffer = new MovementInterpolationBuffer();
                    m_RemoteEnemyBuffers[entry.EnemyId] = buffer;
                }

                buffer.Push(batch.ServerTime, ToEnemyMoveState(entry));
                m_RemoteEnemyStates[entry.EnemyId] = new RemoteEnemyState
                {
                    State = entry.ResolveState(),
                    IsAlive = entry.IsAlive,
                };
            }
        }

        /// <summary>
        /// 把一条敌人快照转成插值缓冲认识的移动状态。
        /// </summary>
        /// <remarks>
        /// 速度在快照里没有：敌人的动画速度由视图按位置差分算（与本地敌人同一做法），
        /// 因此这里填 0 只影响插值缓冲内部对"是否奔跑"的记录，不影响画面。
        /// </remarks>
        private static PlayerMoveState ToEnemyMoveState(in EnemyStateMessage entry)
        {
            var facing = Vector2F.FromDegrees(entry.FacingDegrees);
            return new PlayerMoveState
            {
                Position = new Vector2F(entry.Position.x, entry.Position.y),
                Facing = facing,
                CurrentSpeed = 0f,
                IsSprinting = false,
                IsExhausted = false,
            };
        }

        /// <summary>按插值缓冲驱动每个远端敌人视图。</summary>
        private void UpdateRemoteEnemyViews(float deltaTime)
        {
            if (m_RemoteEnemyBuffers.Count == 0)
            {
                return;
            }

            var estimatedServerTime = m_MovementLink != null ? m_MovementLink.EstimatedServerTime : 0d;

            foreach (var pair in m_RemoteEnemyBuffers)
            {
                if (!pair.Value.TrySample(
                        estimatedServerTime, MultiplayerMovementLink.InterpolationDelaySeconds, out var state))
                {
                    continue;
                }

                var enemyId = pair.Key;
                var discrete = m_RemoteEnemyStates.TryGetValue(enemyId, out var stored)
                    ? stored
                    : new RemoteEnemyState { State = AiStateId.Patrol, IsAlive = true };

                var view = EnsureRemoteEnemyView(enemyId, state);

                // 朝向从插值后的方向向量反算角度：与远端玩家同一套换算，
                // 因此"敌人面朝哪"与"子弹往哪飞"用的是同一条约定。
                var facingDegrees = Mathf.Atan2(state.Facing.Y, state.Facing.X) * Mathf.Rad2Deg;
                view?.Apply(state.Position, facingDegrees, discrete.State, discrete.IsAlive, deltaTime);
            }

            ReportRemoteEnemies();
        }

        /// <summary>按需创建远端敌人视图。</summary>
        private RemoteEnemyView EnsureRemoteEnemyView(int enemyId, in PlayerMoveState state)
        {
            if (m_RemoteEnemyViews.TryGetValue(enemyId, out var existing) && existing != null)
            {
                return existing;
            }

            // 外观按编号取模轮换，与服务器生成敌人时的顺序一致：
            // 编号 1000 起依次对应布局表里的第 1、2、3…名敌人。
            var index = Mathf.Max(0, enemyId - ServerRuntime.EnemyIdBase);
            var prefab = m_EnemyCharacterPrefabs != null && m_EnemyCharacterPrefabs.Length > 0
                ? m_EnemyCharacterPrefabs[index % m_EnemyCharacterPrefabs.Length]
                : null;

            var view = RemoteEnemyView.Create(
                prefab,
                enemyId,
                new Vector3(state.Position.X, 0f, state.Position.Y));
            view.Bind(m_EventBus);

            m_RemoteEnemyViews[enemyId] = view;
            Debug.Log($"[联机] 远端敌人 {enemyId} 已进入视野。");
            return view;
        }

        /// <summary>销毁全部远端敌人视图（断线时调用）。</summary>
        private void ClearRemoteEnemyViews()
        {
            foreach (var view in m_RemoteEnemyViews.Values)
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }

            m_RemoteEnemyViews.Clear();
            m_RemoteEnemyBuffers.Clear();
            m_RemoteEnemyStates.Clear();
        }

        /// <summary>
        /// 验收模式下周期性打印远端敌人位置与状态。
        /// </summary>
        /// <remarks>
        /// 只在 <c>-autowalk</c> 时输出。无头进程没有画面，而这行日志是
        /// "客户端确实看到了服务器上的敌人、并且它在动"的唯一证据（M9-P-14 的同一原则）。
        /// </remarks>
        private void ReportRemoteEnemies()
        {
            if (!m_AutoWalk || Time.timeAsDouble < m_NextEnemyReporterTime
                || m_RemoteEnemyViews.Count == 0)
            {
                return;
            }

            m_NextEnemyReporterTime = Time.timeAsDouble + 2d;

            var reported = 0;
            foreach (var pair in m_RemoteEnemyViews)
            {
                var view = pair.Value;
                if (view == null)
                {
                    continue;
                }

                var position = view.transform.position;
                var state = m_RemoteEnemyStates.TryGetValue(pair.Key, out var stored)
                    ? stored.State
                    : AiStateId.Patrol;

                Debug.Log(
                    $"[联机] 远端敌人 {pair.Key} 位置 ({position.x:F2}, {position.z:F2})，" +
                    $"状态 {state}，存活 {!view.IsDestroyed}");

                // 只打印前两个：五名敌人每 2 秒刷五行会把日志淹掉，
                // 而"位置在变"只需要看一两个就够。
                if (++reported >= 2)
                {
                    break;
                }
            }
        }
    }
}
