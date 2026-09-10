using System;
using System.Collections.Generic;

namespace RaidDemo.Kernel
{
    /// <summary>订阅记录的非泛型基类，使不同事件类型的订阅可以存放在同一列表中。</summary>
    internal abstract class EventSubscription
    {
        protected EventSubscription(int id, int order)
        {
            Id = id;
            Order = order;
        }

        /// <summary>单调递增的订阅编号，用于区分同一委托的多次订阅。</summary>
        public int Id { get; }

        /// <summary>执行顺序，数值越小越先执行。</summary>
        public int Order { get; }
    }

    /// <summary>
    /// 强类型订阅记录。
    /// </summary>
    /// <remarks>
    /// 泛型转换在构造时完成，因此发布循环里只需一次类型检查，无需反射或装箱调用。
    /// 这是事件总线在不牺牲类型安全的前提下保持性能的关键。
    /// </remarks>
    internal sealed class EventSubscription<T> : EventSubscription
        where T : struct
    {
        private readonly Action<T> m_Handler;

        public EventSubscription(int id, int order, Action<T> handler)
            : base(id, order)
        {
            m_Handler = handler;
        }

        /// <summary>以只读引用传递事件负载，避免结构体拷贝。</summary>
        public void Invoke(in T evt)
        {
            m_Handler(evt);
        }
    }

    /// <summary>
    /// 发布过程中挂起的订阅变更。
    /// </summary>
    /// <remarks>
    /// 在遍历订阅列表的过程中新增或移除订阅会导致集合被修改，
    /// 因此这些变更先记录下来，等本次发布结束后统一提交。
    /// </remarks>
    internal readonly struct PendingSubscriptionChange
    {
        private PendingSubscriptionChange(bool isAdd, Type eventType, List<EventSubscription> target, EventSubscription subscription)
        {
            IsAdd = isAdd;
            EventType = eventType;
            Target = target;
            Subscription = subscription;
        }

        /// <summary>true 表示新增订阅，false 表示退订。</summary>
        public bool IsAdd { get; }

        /// <summary>事件类型，用于报告与调试。</summary>
        public Type EventType { get; }

        /// <summary>受影响的订阅列表。</summary>
        public List<EventSubscription> Target { get; }

        /// <summary>涉及的订阅记录。</summary>
        public EventSubscription Subscription { get; }

        public static PendingSubscriptionChange Add(Type eventType, List<EventSubscription> target, EventSubscription subscription)
        {
            return new PendingSubscriptionChange(true, eventType, target, subscription);
        }

        public static PendingSubscriptionChange Remove(Type eventType, List<EventSubscription> target, EventSubscription subscription)
        {
            return new PendingSubscriptionChange(false, eventType, target, subscription);
        }
    }

    /// <summary>
    /// 订阅句柄：释放即退订，支持 using 语法。
    /// </summary>
    /// <remarks>
    /// 重复释放是安全的空操作——这一点很重要，因为在 OnDisable 与对象销毁两个时机
    /// 都可能触发释放，若重复释放会抛异常，反而引入新的崩溃点。
    /// </remarks>
    internal sealed class SubscriptionHandle : IDisposable
    {
        private readonly EventBus m_Owner;
        private readonly Type m_EventType;
        private EventSubscription m_Subscription;

        public SubscriptionHandle(EventBus owner, Type eventType, EventSubscription subscription)
        {
            m_Owner = owner;
            m_EventType = eventType;
            m_Subscription = subscription;
        }

        public void Dispose()
        {
            var subscription = m_Subscription;
            if (subscription == null)
            {
                return;
            }

            // 先清空引用再退订：即使退订过程中出现异常，重复释放也不会造成二次退订。
            m_Subscription = null;
            m_Owner.UnsubscribeInternal(m_EventType, subscription);
        }
    }
}
