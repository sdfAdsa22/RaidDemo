using System.Collections.Generic;
using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 撤离读秒器：负责「玩家站在撤离区里累计时间」这条规则。
    /// </summary>
    /// <remarks>
    /// <para><b>离开即重置，不保留进度。</b>这是已确认的设计。
    /// 撤离的成本必须是持续暴露 10 秒；若进度可以分段累计，
    /// 玩家就能打一下退回来、再打一下再退回来，风险被摊薄到接近于零。</para>
    ///
    /// <para>纯 C# 实现，位置由外部每帧喂进来，不依赖场景里的触发器。</para>
    /// </remarks>
    public sealed class ExtractionTracker
    {
        private readonly List<ExtractionZone> m_Zones;
        private readonly float m_RequiredSeconds;
        private readonly EventBus m_EventBus;

        /// <summary>上一次广播出去的进度，用于过滤无变化的事件。</summary>
        private float m_LastPublishedProgress = -1f;

        /// <summary>创建撤离读秒器。</summary>
        /// <param name="zones">全部撤离点。</param>
        /// <param name="requiredSeconds">需要的读秒时长（秒）。</param>
        /// <param name="eventBus">事件总线，可为 null（测试时可以不要事件）。</param>
        public ExtractionTracker(
            IReadOnlyList<ExtractionZone> zones,
            float requiredSeconds,
            EventBus eventBus)
        {
            m_Zones = new List<ExtractionZone>(zones ?? new ExtractionZone[0]);
            m_RequiredSeconds = requiredSeconds > 0f ? requiredSeconds : 1f;
            m_EventBus = eventBus;
        }

        /// <summary>当前正在读秒的撤离点，没有则为 null。</summary>
        public ExtractionZone ActiveZone { get; private set; }

        /// <summary>当前已累计的读秒时长（秒）。</summary>
        public float ProgressSeconds { get; private set; }

        /// <summary>当前进度（0~1）。</summary>
        public float Progress01
        {
            get
            {
                var ratio = ProgressSeconds / m_RequiredSeconds;
                return ratio > 1f ? 1f : ratio;
            }
        }

        /// <summary>读秒是否已经完成过。</summary>
        public bool HasCompleted { get; private set; }

        /// <summary>撤离点数量。供调试与测试断言使用。</summary>
        public int ZoneCount
        {
            get { return m_Zones.Count; }
        }

        /// <summary>推进读秒。</summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="playerPosition">玩家当前位置。</param>
        /// <param name="playerAlive">玩家是否存活。</param>
        public void Tick(float deltaTime, Vector2F playerPosition, bool playerAlive)
        {
            if (HasCompleted)
            {
                return;
            }

            if (!playerAlive)
            {
                // 阵亡即清空进度并广播一次「不活跃」。
                // 尸体站在撤离区里读秒显然不合理。
                CancelRun(zoneId: 0);
                return;
            }

            var zone = FindZone(playerPosition);
            if (zone == null)
            {
                CancelRun(ActiveZone != null ? ActiveZone.Id : 0);
                return;
            }

            if (ActiveZone != null && ActiveZone.Id != zone.Id)
            {
                // 换了一个撤离点：进度从头开始。
                // 允许换点继承进度，等于允许玩家在两点之间横跳，把 10 秒拆成任意多小段。
                CancelRun(ActiveZone.Id);
            }

            ActiveZone = zone;
            ProgressSeconds += deltaTime > 0f ? deltaTime : 0f;

            if (ProgressSeconds >= m_RequiredSeconds)
            {
                ProgressSeconds = m_RequiredSeconds;
                PublishProgress(zone.Id, Progress01, isActive: true);
                HasCompleted = true;
                m_EventBus?.Publish(new ExtractionCompletedEvent(zone.Id, zone.DisplayName));
                return;
            }

            PublishProgress(zone.Id, Progress01, isActive: true);
        }

        /// <summary>清空进度与完成标记，供再开一局或测试隔离使用。</summary>
        public void Reset()
        {
            ActiveZone = null;
            ProgressSeconds = 0f;
            HasCompleted = false;
            m_LastPublishedProgress = -1f;
        }

        /// <summary>找出玩家所在的撤离点。</summary>
        private ExtractionZone FindZone(Vector2F position)
        {
            for (var i = 0; i < m_Zones.Count; i++)
            {
                if (m_Zones[i].Contains(position))
                {
                    return m_Zones[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 结束当前这次读秒并广播清零。
        /// </summary>
        /// <param name="zoneId">刚离开的撤离点编号，0 表示本来就不在任何点内。</param>
        /// <remarks>
        /// 当前本来就没有进度时不广播：否则每帧站在撤离区外都会产生一条事件，
        /// 开发者面板会被无意义的记录淹没。
        /// </remarks>
        private void CancelRun(int zoneId)
        {
            if (ActiveZone == null && ProgressSeconds <= 0f)
            {
                return;
            }

            ActiveZone = null;
            ProgressSeconds = 0f;
            PublishProgress(zoneId, 0f, isActive: false);
        }

        /// <summary>广播进度，过滤掉与上次完全相同的值。</summary>
        private void PublishProgress(int zoneId, float progress01, bool isActive)
        {
            if (isActive
                && m_LastPublishedProgress >= 0f
                && progress01 - m_LastPublishedProgress < 0.001f)
            {
                return;
            }

            m_LastPublishedProgress = isActive ? progress01 : -1f;
            m_EventBus?.Publish(new ExtractionProgressChangedEvent(zoneId, progress01, isActive));
        }
    }
}
