using RaidDemo.Data;
using RaidDemo.Kernel;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 使用物品的读条：启动、推进、受伤打断、完成。
    /// </summary>
    /// <remarks>
    /// <para>结构上与搜刮读条同源（进度 + 打断 + 完成事件），但打断条件不同：
    /// **搜刮要求站定，使用物品允许移动**——被打得只剩一丝血时还要站住包扎，
    /// 等于把这条功能从「救命」变成「自杀」。它唯一的打断条件是受伤。</para>
    ///
    /// <para>纯 C# 实现，不依赖 UnityEngine：时间由外部喂入，可在测试里直接断言。</para>
    /// </remarks>
    public sealed class ItemUseInteraction
    {
        private readonly EventBus m_EventBus;
        private float m_DurationSeconds;

        /// <summary>创建使用读条。</summary>
        /// <param name="eventBus">事件总线，可为 null。</param>
        public ItemUseInteraction(EventBus eventBus)
        {
            m_EventBus = eventBus;
        }

        /// <summary>当前正在使用的物品，没有则为 null。</summary>
        public ItemInstance TargetItem { get; private set; }

        /// <summary>正在使用的物品显示名。</summary>
        public string DisplayName { get; private set; } = string.Empty;

        /// <summary>是否正在使用。</summary>
        public bool IsUsing
        {
            get { return TargetItem != null; }
        }

        /// <summary>当前进度（0~1）。</summary>
        public float Progress01 { get; private set; }

        /// <summary>已累计的读条时长（秒）。</summary>
        public float ProgressSeconds { get; private set; }

        /// <summary>
        /// 开始使用一件物品。
        /// </summary>
        /// <param name="item">物品实例，不能为 null。</param>
        /// <param name="displayName">显示名，用于界面提示。</param>
        /// <param name="durationSeconds">读条时长（秒）。</param>
        /// <returns>成功开始返回 true；正在使用或参数非法时返回 false。</returns>
        public bool TryBegin(ItemInstance item, string displayName, float durationSeconds)
        {
            if (item == null || durationSeconds <= 0f || IsUsing)
            {
                return false;
            }

            TargetItem = item;
            DisplayName = string.IsNullOrEmpty(displayName) ? "物品" : displayName;
            m_DurationSeconds = durationSeconds;
            ProgressSeconds = 0f;
            Progress01 = 0f;
            m_EventBus?.Publish(new ItemUseStartedEvent(item, DisplayName));
            return true;
        }

        /// <summary>主动取消（例如玩家又按了一次快捷键）。</summary>
        /// <param name="reason">取消原因（中文）。</param>
        public void Cancel(string reason)
        {
            if (!IsUsing)
            {
                return;
            }

            var item = TargetItem;
            Clear();
            m_EventBus?.Publish(new ItemUseCanceledEvent(item, reason));
        }

        /// <summary>
        /// 推进读条。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="playerAlive">玩家是否存活。</param>
        /// <param name="tookDamage">本帧是否受到伤害。</param>
        public void Tick(float deltaTime, bool playerAlive, bool tookDamage)
        {
            if (!IsUsing)
            {
                return;
            }

            if (!playerAlive)
            {
                Cancel("阵亡");
                return;
            }

            if (tookDamage)
            {
                Cancel("受伤");
                return;
            }

            ProgressSeconds += deltaTime > 0f ? deltaTime : 0f;
            Progress01 = ProgressSeconds / m_DurationSeconds;
            if (Progress01 > 1f)
            {
                Progress01 = 1f;
            }

            m_EventBus?.Publish(new ItemUseProgressEvent(Progress01));

            if (ProgressSeconds < m_DurationSeconds)
            {
                return;
            }

            // 与搜刮读条一致：先清状态再广播完成，
            // 否则订阅方在处理「用完了」时读到的是「还在用」。
            var item = TargetItem;
            Clear();
            m_EventBus?.Publish(new ItemUseCompletedEvent(item));
        }

        /// <summary>清空状态。</summary>
        private void Clear()
        {
            TargetItem = null;
            DisplayName = string.Empty;
            ProgressSeconds = 0f;
            Progress01 = 0f;
        }
    }
}
