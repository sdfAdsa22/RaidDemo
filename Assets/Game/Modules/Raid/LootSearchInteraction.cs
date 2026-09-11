using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 搜刮读条：开箱需要持续按键并站定一段时间。
    /// </summary>
    /// <remarks>
    /// <para><b>这条读条是整个「贪婪循环」的成本来源。</b>没有它，搜刮就是瞬时的，
    /// 「要不要再多搜一个箱子」这个问题也就不存在了——
    /// 因为多搜一个没有任何代价。</para>
    ///
    /// <para>打断条件有三条：玩家移动、玩家阵亡、显式取消。
    /// 被打断后进度清零，必须重新开始：允许分段累计等于把 2 秒拆成任意多小段，
    /// 风险会被摊薄到接近于零。</para>
    ///
    /// <para>纯 C# 实现，时间与位置都由外部喂进来，可在 EditMode 测试里直接断言。</para>
    /// </remarks>
    public sealed class LootSearchInteraction
    {
        private readonly float m_DurationSeconds;
        private readonly float m_MovementToleranceMeters;
        private readonly EventBus m_EventBus;

        private Vector2F m_StartPosition;

        /// <summary>创建搜刮读条。</summary>
        /// <param name="durationSeconds">读条时长（秒），非正值会被提升为 0.1 秒。</param>
        /// <param name="movementToleranceMeters">允许多大的位移而不算「移动」（米）。</param>
        /// <param name="eventBus">事件总线，可为 null。</param>
        public LootSearchInteraction(
            float durationSeconds,
            float movementToleranceMeters,
            EventBus eventBus)
        {
            m_DurationSeconds = durationSeconds > 0f ? durationSeconds : 0.1f;
            m_MovementToleranceMeters = movementToleranceMeters > 0f ? movementToleranceMeters : 0.01f;
            m_EventBus = eventBus;
        }

        /// <summary>当前正在搜刮的容器 ID，0 表示没有。</summary>
        public int TargetContainerId { get; private set; }

        /// <summary>是否正在搜刮。</summary>
        public bool IsSearching
        {
            get { return TargetContainerId > 0; }
        }

        /// <summary>当前进度（0~1）。</summary>
        public float Progress01 { get; private set; }

        /// <summary>已累计的读条时长（秒）。</summary>
        public float ProgressSeconds { get; private set; }

        /// <summary>
        /// 开始搜刮一个容器。
        /// </summary>
        /// <param name="containerId">容器 ID。</param>
        /// <param name="playerPosition">玩家当前位置，用作「有没有移动」的基准点。</param>
        /// <returns>成功开始返回 true；已经在搜刮或 ID 非法时返回 false。</returns>
        public bool TryBegin(int containerId, Vector2F playerPosition)
        {
            if (containerId <= 0 || IsSearching)
            {
                return false;
            }

            TargetContainerId = containerId;
            m_StartPosition = playerPosition;
            ProgressSeconds = 0f;
            Progress01 = 0f;
            return true;
        }

        /// <summary>
        /// 取消当前搜刮。
        /// </summary>
        /// <param name="reason">取消原因（中文）。</param>
        public void Cancel(string reason)
        {
            if (!IsSearching)
            {
                return;
            }

            var containerId = TargetContainerId;
            Clear();
            m_EventBus?.Publish(new LootSearchCanceledEvent(containerId, reason));
        }

        /// <summary>
        /// 推进读条。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="playerPosition">玩家当前位置。</param>
        /// <param name="playerAlive">玩家是否存活。</param>
        /// <param name="wantsToMove">本帧是否有移动输入。</param>
        public void Tick(float deltaTime, Vector2F playerPosition, bool playerAlive, bool wantsToMove)
        {
            if (!IsSearching)
            {
                return;
            }

            if (!playerAlive)
            {
                Cancel("阵亡");
                return;
            }

            if (wantsToMove || HasMovedTooFar(playerPosition))
            {
                Cancel("移动");
                return;
            }

            ProgressSeconds += deltaTime > 0f ? deltaTime : 0f;
            Progress01 = ProgressSeconds / m_DurationSeconds;
            if (Progress01 > 1f)
            {
                Progress01 = 1f;
            }

            var containerId = TargetContainerId;
            m_EventBus?.Publish(new LootSearchProgressEvent(containerId, Progress01));

            if (ProgressSeconds < m_DurationSeconds)
            {
                return;
            }

            // 完成时先清空状态再广播：订阅方收到事件后可能会立刻打开容器面板，
            // 若此时状态还停在「正在搜刮」，面板打开的瞬间界面会以为读条仍在进行。
            Clear();
            m_EventBus?.Publish(new LootSearchCompletedEvent(containerId));
        }

        /// <summary>玩家是否已经离开起始点太远。</summary>
        /// <remarks>
        /// 除了「有没有移动输入」之外再加一道位移判定，是因为被击退、
        /// 或站在移动平台上这类情况不会产生移动输入，但人确实被挪动了。
        /// </remarks>
        private bool HasMovedTooFar(Vector2F playerPosition)
        {
            var dx = playerPosition.X - m_StartPosition.X;
            var dy = playerPosition.Y - m_StartPosition.Y;
            var squared = (dx * dx) + (dy * dy);
            return squared > m_MovementToleranceMeters * m_MovementToleranceMeters;
        }

        /// <summary>清空搜刮状态。</summary>
        private void Clear()
        {
            TargetContainerId = 0;
            ProgressSeconds = 0f;
            Progress01 = 0f;
        }
    }
}
