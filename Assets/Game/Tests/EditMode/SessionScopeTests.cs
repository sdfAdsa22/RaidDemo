using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 会话作用域的生命周期测试。
    /// </summary>
    /// <remarks>
    /// 会话是 M9 引入的新边界：它把「事件总线、服务定位器、命令路由」的生命周期从场景里搬了出来。
    /// 这里验证三条最容易出错的规则——服务能被解析、释放后静态入口必须清空、
    /// 以及新旧会话共存时不能互相误删。
    /// </remarks>
    [TestFixture]
    public sealed class SessionScopeTests
    {
        private SessionScope m_Session;

        [TearDown]
        public void TearDown()
        {
            m_Session?.Dispose();
            m_Session = null;
            ServiceLocatorHolder.Clear();
        }

        /// <summary>创建后，四个会话级服务都应可解析。</summary>
        [Test]
        public void 创建后会话服务可解析()
        {
            m_Session = new SessionScope("测试会话");

            Assert.IsNotNull(m_Session.Events);
            Assert.IsNotNull(m_Session.Commands);
            Assert.IsNotNull(m_Session.Log);
            Assert.AreSame(m_Session.Events, m_Session.Services.Get<EventBus>());
            Assert.AreSame(m_Session.Commands, m_Session.Services.Get<CommandRouter>());
            Assert.AreSame(m_Session.Log, m_Session.Services.Get<LogService>());
            Assert.IsFalse(m_Session.IsDisposed);
        }

        /// <summary>会话创建后，表现层通过静态入口就能拿到服务。</summary>
        [Test]
        public void 创建后静态入口指向本会话()
        {
            m_Session = new SessionScope("测试会话");

            Assert.AreSame(m_Session.Services, ServiceLocatorHolder.Current);
            Assert.IsTrue(ServiceLocatorHolder.TryGet(out EventBus bus));
            Assert.AreSame(m_Session.Events, bus);
        }

        /// <summary>释放后静态入口必须清空，否则下次场景启动会拿到已死的服务。</summary>
        [Test]
        public void 释放后静态入口被清空()
        {
            m_Session = new SessionScope("测试会话");
            m_Session.Dispose();

            Assert.IsTrue(m_Session.IsDisposed);
            Assert.IsNull(ServiceLocatorHolder.Current);
            Assert.AreEqual(0, m_Session.Services.Count);
        }

        /// <summary>重复释放必须安全——卸载路径上多个对象都会调用它。</summary>
        [Test]
        public void 重复释放不抛异常()
        {
            m_Session = new SessionScope("测试会话");

            Assert.DoesNotThrow(() => m_Session.Dispose());
            Assert.DoesNotThrow(() => m_Session.Dispose());
        }

        /// <summary>释放后的会话不应再被注册系统使用。</summary>
        [Test]
        public void 释放后继续使用会抛异常()
        {
            m_Session = new SessionScope("测试会话");
            m_Session.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() => m_Session.ThrowIfDisposed());
        }

        /// <summary>
        /// 旧会话释放时不能清掉新会话的静态入口。
        /// </summary>
        /// <remarks>
        /// 场景重载期间新旧会话会短暂共存：先建新、再释放旧。
        /// 若释放逻辑无条件清空静态入口，表现层就会在新场景里收不到任何事件——
        /// 症状是「第二局开始后角色不动、音效消失」，非常难查。
        /// </remarks>
        [Test]
        public void 释放旧会话不会影响新会话的静态入口()
        {
            var oldSession = new SessionScope("旧会话");
            m_Session = new SessionScope("新会话");

            oldSession.Dispose();

            Assert.AreSame(m_Session.Services, ServiceLocatorHolder.Current);
            Assert.IsTrue(ServiceLocatorHolder.TryGet(out EventBus bus));
            Assert.AreSame(m_Session.Events, bus);
        }

        /// <summary>单机与服务器的会话标签不同，日志里才能区分是谁起的会话。</summary>
        [Test]
        public void 单机与服务器会话标签可区分()
        {
            var local = SessionScope.CreateLocal();
            var server = SessionScope.CreateDedicatedServer(LogLevel.Warning);

            try
            {
                Assert.AreNotEqual(local.Owner, server.Owner);
                Assert.AreEqual(LogLevel.Warning, server.Log.MinimumLevel);
            }
            finally
            {
                server.Dispose();
                local.Dispose();
            }
        }
    }
}
