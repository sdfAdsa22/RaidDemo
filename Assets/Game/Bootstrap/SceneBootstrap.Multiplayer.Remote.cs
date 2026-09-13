using System.Collections.Generic;
using RaidDemo.Presentation;
using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的远端玩家部分：插值缓冲、视图生命周期与验收日志。
    /// </summary>
    /// <remarks>
    /// 与"本机预测 + 对账"分开成两个文件，是因为它们服务两条互不相干的数据流：
    /// 一条只关心我自己（预测→上行→对账），另一条只关心别人（快照→插值→表现）。
    /// 混在一起时任何一个出问题都要在读代码时来回跳。
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>远端插值渲染延迟（秒）。约等于两条快照的间隔。</summary>
        private const float InterpolationDelay = 0.1f;

        private readonly Dictionary<int, MovementInterpolationBuffer> m_RemoteBuffers =
            new Dictionary<int, MovementInterpolationBuffer>();
        private readonly Dictionary<int, RemotePlayerView> m_RemoteViews =
            new Dictionary<int, RemotePlayerView>();

        private double m_ServerClockAtSnapshot;
        private double m_LocalClockAtSnapshot;
        private double m_NextRemoteReporterTime;

        /// <summary>已建立的远端玩家视图数量（调试与验收用）。</summary>
        public int RemoteViewCount => m_RemoteViews.Count;

        /// <summary>
        /// 记下最新的服务器时间，供插值估计"现在服务器是几点"。
        /// </summary>
        /// <remarks>
        /// 两端时钟并不一致，因此不能用本地时间直接取样。做法是记录"收到快照那一刻的服务器时间"
        /// 与当时的本地时间，之后用本地流逝量去推——误差只有一次网络抖动那么大，对插值足够。
        /// </remarks>
        private void NoteServerClock(double serverTime)
        {
            m_ServerClockAtSnapshot = serverTime;
            m_LocalClockAtSnapshot = Time.timeAsDouble;
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

            var estimatedServerTime =
                m_ServerClockAtSnapshot + (Time.timeAsDouble - m_LocalClockAtSnapshot);

            foreach (var pair in m_RemoteBuffers)
            {
                if (!pair.Value.TrySample(estimatedServerTime, InterpolationDelay, out var state))
                {
                    continue;
                }

                var view = EnsureRemoteView(pair.Key, state);
                view?.Apply(state, deltaTime);
            }

            ReportRemotePositions();
        }

        /// <summary>销毁全部远端玩家视图（断线时调用）。</summary>
        private void ClearRemoteViews()
        {
            foreach (var view in m_RemoteViews.Values)
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
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

            // P1 的外观策略：远端玩家先复用本机选择的角色模型。
            // 每名玩家各自的外观要等 P4 的大厅把选择结果传上来（那时才谈得上"别人选了什么"）。
            var prefab = m_PresentationCatalog != null
                ? m_PresentationCatalog.FindCharacterPrefab(
                    RaidFlowController.Ensure().Progress != null
                        ? RaidFlowController.Ensure().Progress.SelectedCharacterId
                        : null)
                : null;

            var view = RemotePlayerView.Create(
                prefab,
                playerId,
                new Vector3(state.Position.X, 0f, state.Position.Y));

            m_RemoteViews[playerId] = view;
            Debug.Log($"[联机] 远端玩家 {playerId} 已进入视野。");
            return view;
        }
    }
}
