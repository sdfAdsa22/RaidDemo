namespace RaidDemo.Kernel
{
    /// <summary>
    /// 事件信封：为需要跨模块统一处理的事件提供契约。
    /// </summary>
    /// <remarks>
    /// <para>普通事件不必实现本接口，直接以结构体发布即可。它存在的意义有两个：</para>
    /// <list type="number">
    /// <item><description>需要携带来源与时间戳的事件（例如网络同步事件）可以统一被记录与回放。</description></item>
    /// <item><description>编辑器调试面板可以对所有实现本接口的事件做统一展示，而不必逐个类型适配。</description></item>
    /// </list>
    /// </remarks>
    public interface IEventEnvelope
    {
        /// <summary>事件发生的时间戳（秒），用于排序与延迟分析。</summary>
        double Timestamp { get; }

        /// <summary>事件来源标识（例如玩家 ID 或模块名），用于调试与网络归属判断。</summary>
        string Source { get; }

        /// <summary>若本事件由网络同步产生，此处为对应命令的序号；本地事件填 0。</summary>
        uint Sequence { get; }
    }

    /// <summary>
    /// 事件收发的历史记录。
    /// </summary>
    /// <remarks>
    /// 用于编辑器调试面板排查"事件发了但没人响应"这类问题：
    /// <see cref="SubscriberCount"/> 为 0 就是最直接的证据——事件确实发出去了，只是没有订阅者。
    /// </remarks>
    public readonly struct EventRecord
    {
        public EventRecord(string eventName, int subscriberCount, bool isPublish, int subscriberDelta)
        {
            EventName = eventName;
            SubscriberCount = subscriberCount;
            IsPublish = isPublish;
            SubscriberDelta = subscriberDelta;
        }

        /// <summary>事件类型名。</summary>
        public string EventName { get; }

        /// <summary>发布时该事件的订阅者数量。</summary>
        public int SubscriberCount { get; }

        /// <summary>true 表示这是一次发布记录，false 表示这是一次订阅数量变化记录。</summary>
        public bool IsPublish { get; }

        /// <summary>订阅数量变化量（订阅为正，退订为负）。仅在订阅变化记录中有意义。</summary>
        public int SubscriberDelta { get; }

        public override string ToString()
        {
            return IsPublish
                ? $"publish {EventName} → {SubscriberCount} 个订阅者"
                : $"subscribers {EventName} 变化 {SubscriberDelta}";
        }
    }
}
