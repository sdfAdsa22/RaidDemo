using System;
using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Kernel;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>事件总线的行为测试。</summary>
    [TestFixture]
    public sealed class EventBusTests
    {
        /// <summary>测试用事件：携带一个计数值。</summary>
        private readonly struct CounterEvent
        {
            public CounterEvent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        /// <summary>第二个测试用事件，用于验证类型隔离。</summary>
        private readonly struct OtherEvent
        {
        }

        private EventBus m_Bus;

        [SetUp]
        public void SetUp()
        {
            m_Bus = new EventBus();
        }

        [Test]
        public void Publish_NotifiesSubscriber()
        {
            var received = 0;
            using (m_Bus.Subscribe<CounterEvent>(e => received = e.Value))
            {
                m_Bus.Publish(new CounterEvent(42));
            }

            Assert.AreEqual(42, received, "订阅者应当收到发布的事件负载。");
        }

        [Test]
        public void Publish_NotifiesAllSubscribers()
        {
            var calls = new List<string>();
            using (m_Bus.Subscribe<CounterEvent>(_ => calls.Add("first")))
            using (m_Bus.Subscribe<CounterEvent>(_ => calls.Add("second")))
            {
                m_Bus.Publish(new CounterEvent(1));
            }

            CollectionAssert.AreEqual(new[] { "first", "second" }, calls, "多个订阅者都应收事件，且按订阅顺序执行。");
        }

        [Test]
        public void Publish_OrdersSubscribersByOrderValue()
        {
            var calls = new List<string>();

            // 后订阅但 order 更小，应当先执行。
            using (m_Bus.Subscribe<CounterEvent>(_ => calls.Add("late-subscribed-earlier-executed"), order: -10))
            using (m_Bus.Subscribe<CounterEvent>(_ => calls.Add("default-order")))
            {
                m_Bus.Publish(new CounterEvent(1));
            }

            CollectionAssert.AreEqual(
                new[] { "late-subscribed-earlier-executed", "default-order" },
                calls,
                "order 值更小的订阅者应先收到事件。");
        }

        [Test]
        public void DisposeSubscription_StopsReceivingEvents()
        {
            var count = 0;
            var subscription = m_Bus.Subscribe<CounterEvent>(_ => count++);

            m_Bus.Publish(new CounterEvent(1));
            subscription.Dispose();
            m_Bus.Publish(new CounterEvent(2));

            Assert.AreEqual(1, count, "退订后不应再收到事件。");
        }

        [Test]
        public void DisposeSubscription_TwiceIsSafe()
        {
            var count = 0;
            var subscription = m_Bus.Subscribe<CounterEvent>(_ => count++);

            subscription.Dispose();
            subscription.Dispose();
            m_Bus.Publish(new CounterEvent(1));

            Assert.AreEqual(0, count, "重复释放订阅句柄不应抛异常，也不应误删其他订阅。");
        }

        [Test]
        public void Publish_DoesNotNotifyOtherEventTypes()
        {
            var counterCalls = 0;
            var otherCalls = 0;

            using (m_Bus.Subscribe<CounterEvent>(_ => counterCalls++))
            using (m_Bus.Subscribe<OtherEvent>(_ => otherCalls++))
            {
                m_Bus.Publish(new CounterEvent(1));
            }

            Assert.AreEqual(1, counterCalls);
            Assert.AreEqual(0, otherCalls, "订阅者只应收到自己订阅的事件类型。");
        }

        [Test]
        public void Publish_WithNoSubscriber_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => m_Bus.Publish(new CounterEvent(1)));
            Assert.AreEqual(1, m_Bus.PublishedCount);
        }

        [Test]
        public void SubscribeDuringPublish_TakesEffectOnNextPublish()
        {
            var nested = 0;

            // 第一个订阅者在回调中新增一个订阅者。本次发布不应通知新订阅者，
            // 否则会出现边遍历边扩展集合的未定义行为。
            using (m_Bus.Subscribe<CounterEvent>(_ =>
                   {
                       if (nested == 0)
                       {
                           m_Bus.Subscribe<CounterEvent>(__ => nested++);
                       }
                   }))
            {
                m_Bus.Publish(new CounterEvent(1));
                Assert.AreEqual(0, nested, "发布过程中新增的订阅者不应收到本次事件。");

                m_Bus.Publish(new CounterEvent(2));
                Assert.Greater(nested, 0, "下一次发布时新订阅者应当收到事件。");
            }
        }

        [Test]
        public void UnsubscribeAll_RemovesSubscribersOfThatType()
        {
            m_Bus.Subscribe<CounterEvent>(_ => { });
            m_Bus.Subscribe<CounterEvent>(_ => { });
            m_Bus.Subscribe<OtherEvent>(_ => { });

            var removed = m_Bus.UnsubscribeAll<CounterEvent>();

            Assert.AreEqual(2, removed);
            Assert.AreEqual(0, m_Bus.GetSubscriberCount<CounterEvent>());
            Assert.AreEqual(1, m_Bus.GetSubscriberCount<OtherEvent>(), "不应影响其他事件类型的订阅者。");
        }

        [Test]
        public void GetSubscriberCount_TracksAddAndRemove()
        {
            Assert.AreEqual(0, m_Bus.GetSubscriberCount<CounterEvent>());

            var a = m_Bus.Subscribe<CounterEvent>(_ => { });
            var b = m_Bus.Subscribe<CounterEvent>(_ => { });
            Assert.AreEqual(2, m_Bus.GetSubscriberCount<CounterEvent>());

            a.Dispose();
            Assert.AreEqual(1, m_Bus.GetSubscriberCount<CounterEvent>());

            b.Dispose();
            Assert.AreEqual(0, m_Bus.GetSubscriberCount<CounterEvent>());
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var calls = 0;
            m_Bus.Subscribe<CounterEvent>(_ => calls++);

            m_Bus.Clear();
            m_Bus.Publish(new CounterEvent(1));

            Assert.AreEqual(0, m_Bus.GetTotalSubscriberCount());
            Assert.AreEqual(0, calls, "Clear 之后不应再有任何订阅者被通知。");
            Assert.AreEqual(1, m_Bus.PublishedCount, "Clear 应当重置发布计数：清零后本次发布应计为 1 次。");
        }

        [Test]
        public void Subscribe_WithNullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => m_Bus.Subscribe<CounterEvent>(null));
        }

        [Test]
        public void RecordHistory_CapturesPublishedEvents()
        {
            var bus = new EventBus(recordHistory: true);
            using (bus.Subscribe<CounterEvent>(_ => { }))
            {
                bus.Publish(new CounterEvent(1));
            }

            var history = bus.GetHistory();

            Assert.AreEqual(1, history.Count);
            Assert.IsTrue(history[0].IsPublish);
            Assert.AreEqual(nameof(CounterEvent), history[0].EventName);
            Assert.AreEqual(1, history[0].SubscriberCount, "历史记录应当保留当时的订阅者数量，便于排查事件无人响应的问题。");
        }

        [Test]
        public void RecordHistory_IsBounded()
        {
            var bus = new EventBus(recordHistory: true);
            for (var i = 0; i < 200; i++)
            {
                bus.Publish(new CounterEvent(i));
            }

            Assert.LessOrEqual(bus.GetHistory().Count, bus.HistoryCapacity, "历史记录必须有上限，否则长时间运行会持续占用内存。");
        }

        /// <summary>
        /// 历史记录默认关闭。
        /// </summary>
        /// <remarks>
        /// 记录历史会产生额外分配，发行构建不应承担这份开销，因此默认值必须是关闭。
        /// </remarks>
        [Test]
        public void RecordHistory_IsDisabledByDefault()
        {
            m_Bus.Publish(new CounterEvent(1));

            Assert.IsFalse(m_Bus.RecordHistory);
            Assert.AreEqual(0, m_Bus.GetHistory().Count);
        }
    }
}
