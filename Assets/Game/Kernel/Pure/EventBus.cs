using System;
using System.Collections.Generic;

namespace RaidDemo.Kernel
{
    /// <summary>
    /// 事件总线：模块之间唯一的广播通道。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>低层模块（例如背包）在完成一次操作后，不应直接调用高层模块
    /// （例如 UI 或任务系统）。否则背包会依赖 UI，破坏程序集依赖方向，也让"新增一个订阅者"
    /// 变成"修改背包代码"。事件总线的价值就是把这种依赖反转过来：数据变更后广播事件，
    /// 谁关心谁自己订阅。</para>
    ///
    /// <para><b>本类刻意不引用 UnityEngine：</b>该文件所在程序集（RaidDemo.Kernel.Pure）禁止引用引擎类型，
    /// 目的是让同一份事件机制既能在客户端使用，也能在服务端（无渲染环境）使用，
    /// 同时保证单元测试不需要启动 Unity。详见 Docs/Decisions/ADR-001。</para>
    ///
    /// <para><b>订阅与退订必须成对：</b>典型用法是在 MonoBehaviour 的 OnEnable 中订阅、
    /// 在 OnDisable 中退订。忘记退订会导致对象已销毁却仍被事件总线持有，形成内存泄漏——
    /// 这是 Unity 项目中最常见的泄漏来源之一。</para>
    ///
    /// <para><b>为什么用事件信封而不是泛型事件：</b>若直接使用 <c>Subscribe&lt;T&gt;() where T : struct</c>
    /// 这样的约束，在运行时用变量取出缓存时无法满足泛型约束，必须反射调用，反而更慢更难调试。
    /// 因此这里统一用 <see cref="IEventEnvelope"/> 作为对外类型，把强类型转换放在泛型包装器内部完成。</para>
    /// </remarks>
    public sealed class EventBus
    {
        /// <summary>默认的订阅者排序值。数值越小越先收到事件。</summary>
        public const int DefaultOrder = 0;

        /// <summary>每个事件类型对应的订阅记录数量上限。</summary>
        private const int InitialSubscriberCapacity = 8;

        /// <summary>
        /// 事件开发模式下保留的历史记录条数上限。
        /// 取值偏小是刻意的：历史记录只用于排查"事件发了但没人响应"这类问题，不需要长期保留。
        /// </summary>
        private const int MaxRecordedHistory = 64;

        /// <summary>事件类型到订阅者的映射。使用 Hashtable 语义的字典，保证相同泛型类型的查找最快。</summary>
        private readonly Dictionary<Type, List<EventSubscription>> m_Subscriptions = new Dictionary<Type, List<EventSubscription>>();

        /// <summary>最近一次事件收发记录，仅在启用记录时写入。</summary>
        private readonly Queue<EventRecord> m_History = new Queue<EventRecord>(MaxRecordedHistory);

        /// <summary>发布过程中若发生订阅变更，先把变更记在此处，避免边遍历边修改集合。</summary>
        private readonly List<PendingSubscriptionChange> m_PendingChanges = new List<PendingSubscriptionChange>(4);

        private int m_PublishDepth;
        private int m_PublishedCount;
        private int m_NextSubscriptionId;

        /// <summary>已发布的事件总数。用于测试断言与性能观测。</summary>
        public int PublishedCount => m_PublishedCount;

        private bool m_RecordHistory;

        /// <summary>
        /// 创建事件总线。
        /// </summary>
        /// <param name="historyCapacity">
        /// 保留的事件历史条数上限。默认 64 条足以覆盖一次排查所需的上下文，
        /// 同时避免长时间运行时的内存增长。
        /// </param>
        /// <param name="recordHistory">
        /// 是否记录事件历史。编辑器与测试环境建议开启，发行构建应关闭以避免额外分配。
        /// </param>
        public EventBus(int historyCapacity = MaxRecordedHistory, bool recordHistory = false)
        {
            HistoryCapacity = Math.Max(8, historyCapacity);
            m_RecordHistory = recordHistory;
        }

        /// <summary>事件历史记录的容量上限。</summary>
        public int HistoryCapacity { get; }

        /// <summary>是否记录事件历史。编辑器与测试环境建议开启，发行构建应关闭以避免额外分配。</summary>
        public bool RecordHistory
        {
            get => m_RecordHistory;
            set => m_RecordHistory = value;
        }

        /// <summary>
        /// 订阅指定类型的事件。
        /// </summary>
        /// <typeparam name="T">事件类型，必须是结构体（避免装箱与意外共享）。</typeparam>
        /// <param name="handler">事件处理回调，不允许为 null。</param>
        /// <param name="order">
        /// 执行顺序，数值越小越先执行。同一顺序值时按订阅先后顺序执行。
        /// 需要控制顺序的场景示例：表现层音效应先于 UI 刷新，以便两者在同一帧内保持同步。
        /// </param>
        /// <returns>可用 using 释放的订阅句柄，释放即退订。</returns>
        public IDisposable Subscribe<T>(Action<T> handler, int order = DefaultOrder)
            where T : struct
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler), "事件处理回调不能为 null。");
            }

            var type = typeof(T);
            var subscription = new EventSubscription<T>(++m_NextSubscriptionId, order, handler);

            // 正在发布时不能直接改集合，先挂起，等发布结束后统一提交。
            if (m_PublishDepth > 0)
            {
                if (!m_Subscriptions.TryGetValue(type, out var pending))
                {
                    pending = new List<EventSubscription>(InitialSubscriberCapacity);
                    m_Subscriptions.Add(type, pending);
                }

                m_PendingChanges.Add(PendingSubscriptionChange.Add(type, pending, subscription));
            }
            else
            {
                AddSubscriptionInternal(type, subscription);
            }

            return new SubscriptionHandle(this, type, subscription);
        }

        /// <summary>
        /// 发布事件，按顺序通知所有订阅者。
        /// </summary>
        /// <param name="evt">事件负载，按值传递。</param>
        /// <remarks>
        /// 若某个订阅者抛出异常，异常会向上传播并中断后续订阅者的通知。
        /// 这是刻意选择：静默吞掉异常会让"事件没人响应"的问题极难排查。
        /// 需要容错的订阅者应自行在回调内捕获异常。
        /// </remarks>
        public void Publish<T>(in T evt)
            where T : struct
        {
            var type = typeof(T);
            m_PublishedCount++;

            if (RecordHistory)
            {
                Record(type.Name, m_Subscriptions.TryGetValue(type, out var counted) ? counted.Count : 0);
            }

            if (!m_Subscriptions.TryGetValue(type, out var list) || list.Count == 0)
            {
                return;
            }

            m_PublishDepth++;
            try
            {
                // 按索引遍历而非 foreach：发布过程中新增的订阅者会在本次发布结束时才生效，
                // 因此这里读取的 Count 是稳定的。
                var count = list.Count;
                for (var i = 0; i < count; i++)
                {
                    if (list[i] is EventSubscription<T> typed)
                    {
                        typed.Invoke(in evt);
                    }
                }
            }
            finally
            {
                m_PublishDepth--;
                if (m_PublishDepth == 0)
                {
                    ApplyPendingChanges();
                }
            }
        }

        /// <summary>退订指定类型事件的全部处理回调。</summary>
        /// <returns>被移除的订阅数量。</returns>
        public int UnsubscribeAll<T>()
            where T : struct
        {
            var removed = UnsubscribeAll(typeof(T));
            if (removed > 0 && RecordHistory)
            {
                RecordSubscribeChange(typeof(T).Name, -removed);
            }

            return removed;
        }

        /// <summary>
        /// 清空所有订阅与历史记录。
        /// </summary>
        /// <remarks>
        /// 单元测试必须在每个用例开始前调用本方法，否则上一个用例留下的订阅会污染下一个用例，
        /// 产生"单独跑通过、一起跑失败"的随机故障。
        /// </remarks>
        public void Clear()
        {
            m_Subscriptions.Clear();
            m_History.Clear();
            m_PendingChanges.Clear();
            m_PublishDepth = 0;
            m_PublishedCount = 0;
            m_NextSubscriptionId = 0;
        }

        /// <summary>读取最近的事件记录（时间顺序）。仅在 <see cref="RecordHistory"/> 为 true 时有内容。</summary>
        public IReadOnlyList<EventRecord> GetHistory()
        {
            return new List<EventRecord>(m_History);
        }

        /// <summary>查询指定事件类型当前的订阅者数量，用于断言与调试。</summary>
        public int GetSubscriberCount<T>()
            where T : struct
        {
            return m_Subscriptions.TryGetValue(typeof(T), out var list) ? list.Count : 0;
        }

        /// <summary>查询所有事件类型的订阅者总数。</summary>
        public int GetTotalSubscriberCount()
        {
            var total = 0;
            foreach (var pair in m_Subscriptions)
            {
                total += pair.Value.Count;
            }

            return total;
        }

        /// <summary>按 order 升序插入；相同 order 保持订阅先后顺序，保证执行顺序可预测。</summary>
        private static void InsertSorted(List<EventSubscription> list, EventSubscription subscription)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Order > subscription.Order)
                {
                    list.Insert(i, subscription);
                    return;
                }
            }

            list.Add(subscription);
        }

        private void ApplyPendingChanges()
        {
            for (var i = 0; i < m_PendingChanges.Count; i++)
            {
                var change = m_PendingChanges[i];
                if (change.IsAdd)
                {
                    InsertSorted(change.Target, change.Subscription);
                }
                else
                {
                    change.Target.Remove(change.Subscription);
                }
            }

            m_PendingChanges.Clear();
        }

        /// <summary>执行实际退订。返回是否确实移除了订阅（重复退订返回 false）。</summary>
        private bool Unsubscribe(Type eventType, EventSubscription subscription)
        {
            if (!m_Subscriptions.TryGetValue(eventType, out var list))
            {
                return false;
            }

            if (m_PublishDepth > 0)
            {
                m_PendingChanges.Add(PendingSubscriptionChange.Remove(eventType, list, subscription));
                return true;
            }

            var removed = list.Remove(subscription);
            if (removed && list.Count == 0)
            {
                // 空列表立即回收，避免事件类型数量随运行时间无限增长。
                m_Subscriptions.Remove(eventType);
            }

            return removed;
        }

        private int UnsubscribeAll(Type eventType)
        {
            if (!m_Subscriptions.TryGetValue(eventType, out var list))
            {
                return 0;
            }

            var count = list.Count;
            m_Subscriptions.Remove(eventType);
            return count;
        }

        private void Record(string eventName, int subscriberCount)
        {
            Push(new EventRecord(eventName, subscriberCount, true, 0));
        }

        private void RecordSubscribeChange(string eventName, int delta)
        {
            Push(new EventRecord(eventName, 0, false, delta));
        }

        private void Push(EventRecord record)
        {
            if (m_History.Count >= HistoryCapacity)
            {
                m_History.Dequeue();
            }

            m_History.Enqueue(record);
        }

        /// <summary>
        /// 执行实际退订，供订阅句柄调用。返回是否确实移除了订阅（重复退订返回 false）。
        /// </summary>
        internal bool UnsubscribeInternal(Type eventType, EventSubscription subscription)
        {
            return Unsubscribe(eventType, subscription);
        }

        /// <summary>新增订阅，供订阅句柄在发布期间挂起变更时调用。</summary>
        internal void AddSubscriptionInternal(Type eventType, EventSubscription subscription)
        {
            if (!m_Subscriptions.TryGetValue(eventType, out var list))
            {
                list = new List<EventSubscription>(InitialSubscriberCapacity);
                m_Subscriptions.Add(eventType, list);
            }

            InsertSorted(list, subscription);
        }
    }
}
