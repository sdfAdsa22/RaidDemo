using System;
using NUnit.Framework;
using RaidDemo.Kernel;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>服务定位器的行为测试。</summary>
    [TestFixture]
    public sealed class ServiceLocatorTests
    {
        /// <summary>测试用服务接口。</summary>
        private interface IClock
        {
            int Ticks { get; }
        }

        /// <summary>第二个测试用服务，用于验证按类型隔离与延迟创建。</summary>
        private interface INameSource
        {
            string Name { get; }
        }

        private sealed class FakeClock : IClock
        {
            public FakeClock(int ticks)
            {
                Ticks = ticks;
            }

            public int Ticks { get; }
        }

        private sealed class FixedNameSource : INameSource
        {
            public FixedNameSource(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        /// <summary>用于验证 Clear 会释放一次性资源的服务。</summary>
        private sealed class DisposableService : IDisposable
        {
            public bool WasDisposed { get; private set; }

            public void Dispose()
            {
                WasDisposed = true;
            }
        }

        private ServiceLocator m_Locator;

        [SetUp]
        public void SetUp()
        {
            m_Locator = new ServiceLocator();
        }

        [Test]
        public void Register_ThenGet_ReturnsSameInstance()
        {
            var clock = new FakeClock(7);
            m_Locator.Register<IClock>(clock);

            Assert.AreSame(clock, m_Locator.Get<IClock>());
        }

        [Test]
        public void Get_UnregisteredService_Throws()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => m_Locator.Get<IClock>());
            StringAssert.Contains("尚未注册", exception.Message, "错误信息应明确指出是哪个服务未注册。");
        }

        [Test]
        public void Register_DuplicateWithoutOverwrite_Throws()
        {
            m_Locator.Register<IClock>(new FakeClock(1));

            var exception = Assert.Throws<InvalidOperationException>(() => m_Locator.Register<IClock>(new FakeClock(2)));
            StringAssert.Contains("已注册", exception.Message);
        }

        [Test]
        public void Register_WithOverwrite_ReplacesInstance()
        {
            m_Locator.Register<IClock>(new FakeClock(1));
            var replacement = new FakeClock(2);

            m_Locator.Register<IClock>(replacement, overwrite: true);

            Assert.AreSame(replacement, m_Locator.Get<IClock>());
        }

        [Test]
        public void Register_WithNullInstance_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => m_Locator.Register<IClock>(null));
        }

        [Test]
        public void RegisterLazy_CreatesOnlyOnFirstGet()
        {
            var created = 0;
            m_Locator.RegisterLazy<IClock>(() =>
            {
                created++;
                return new FakeClock(3);
            });

            Assert.AreEqual(0, created, "延迟注册不应在注册时立即创建实例。");

            var first = m_Locator.Get<IClock>();
            var second = m_Locator.Get<IClock>();

            Assert.AreEqual(1, created, "延迟服务只应创建一次，之后复用同一实例。");
            Assert.AreSame(first, second);
        }

        [Test]
        public void TryGet_ReturnsFalseForUnregistered()
        {
            var found = m_Locator.TryGet<IClock>(out var service);

            Assert.IsFalse(found);
            Assert.IsNull(service);
        }

        [Test]
        public void IsRegistered_ReflectsLazyRegistration()
        {
            Assert.IsFalse(m_Locator.IsRegistered<IClock>());

            m_Locator.RegisterLazy<IClock>(() => new FakeClock(1));

            Assert.IsTrue(m_Locator.IsRegistered<IClock>(), "延迟注册在创建之前也应被视为已注册。");
            Assert.AreEqual(1, m_Locator.Get<IClock>().Ticks);
        }

        [Test]
        public void Unregister_RemovesRegistration()
        {
            m_Locator.Register<IClock>(new FakeClock(1));

            Assert.IsTrue(m_Locator.Unregister<IClock>());
            Assert.IsFalse(m_Locator.IsRegistered<IClock>());
        }

        [Test]
        public void Register_TwoServicesOfDifferentTypes_AreIndependent()
        {
            m_Locator.Register<IClock>(new FakeClock(1));
            m_Locator.Register<INameSource>(new FixedNameSource("alpha"));

            Assert.AreEqual(1, m_Locator.Get<IClock>().Ticks);
            Assert.AreEqual("alpha", m_Locator.Get<INameSource>().Name);
        }

        [Test]
        public void Clear_RemovesAllRegistrations()
        {
            m_Locator.Register<IClock>(new FakeClock(1));
            m_Locator.RegisterLazy<INameSource>(() => new FixedNameSource("test"));

            m_Locator.Clear();

            Assert.AreEqual(0, m_Locator.Count);
            Assert.IsFalse(m_Locator.IsRegistered<IClock>());
            Assert.IsFalse(m_Locator.IsRegistered<INameSource>(), "尚未创建的延迟注册也应当被清除。");
        }

        /// <summary>
        /// 服务实现 IDisposable 时，Clear 应当释放它。
        /// </summary>
        /// <remarks>
        /// 这条规则防止对象池之类持有资源（例如原生内存、网络连接）的服务在场景切换或测试结束时泄漏。
        /// </remarks>
        [Test]
        public void Clear_DisposesDisposableServices()
        {
            var disposable = new DisposableService();
            m_Locator.Register<DisposableService>(disposable);

            m_Locator.Clear();

            Assert.IsTrue(disposable.WasDisposed);
        }
    }
}
