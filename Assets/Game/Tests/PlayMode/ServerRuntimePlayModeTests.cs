using System.Collections;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using UnityEngine;
using UnityEngine.TestTools;

namespace RaidDemo.Tests.PlayMode
{
    /// <summary>
    /// 专用服务器运行时的启动测试（PlayMode）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须放在 PlayMode：</b>Netcode for GameObjects 依赖 MonoBehaviour 的
    /// <c>Awake</c> 完成内部装配，而编辑模式不会触发生命周期回调——在 EditMode 里调用
    /// <c>StartServer()</c> 会直接抛「没有为实例分配 NetworkManager」。
    /// 这不是被测代码的问题，而是「依赖引擎生命周期」这类测试只能在播放模式下做。</para>
    ///
    /// <para><b>它验证什么：</b>P0 的验收点「服务器能起来、能监听、能报出别人该连的地址」。
    /// 用真实端口做真实绑定——端口占用、传输层没配置、监听地址写错，
    /// 这些只有真绑定才会暴露。</para>
    ///
    /// <para>端口取高位端口，避免与本机正在运行的服务器（默认 7777）撞车。</para>
    /// </remarks>
    [TestFixture]
    public sealed class ServerRuntimePlayModeTests
    {
        /// <summary>测试端口。</summary>
        private const int TestPort = 45777;

        private ServerRuntime m_Runtime;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (m_Runtime != null)
            {
                m_Runtime.Shutdown();
                Object.Destroy(m_Runtime.gameObject);
                m_Runtime = null;
            }

            // 等一帧让销毁生效，避免下一个用例拿到上一个用例的残留对象。
            yield return null;
        }

        /// <summary>启动后应进入监听状态，并持有可用的会话作用域。</summary>
        [UnityTest]
        public IEnumerator 启动后进入监听状态()
        {
            ServerLaunchOptions.TryParse(
                new[] { "-server", "-port", TestPort.ToString(), "-room", "测试房间" },
                out var options,
                out var parseError);
            Assert.IsNotNull(options, parseError);

            m_Runtime = ServerRuntime.Create(options);

            // 传输层在 StartServer 之后还需要一帧完成内部状态同步。
            yield return null;

            Assert.IsTrue(m_Runtime.IsListening, "服务器应当处于监听状态。");
            Assert.IsNotNull(m_Runtime.Session, "服务器会话应当已建立。");
            Assert.IsFalse(m_Runtime.Session.IsDisposed);
            Assert.IsNotNull(m_Runtime.Network);
            Assert.IsTrue(m_Runtime.Network.IsServer, "本进程应当是权威服务器。");
            Assert.AreEqual(TestPort, m_Runtime.Options.Port);
        }

        /// <summary>关闭后应停止监听并释放会话，避免端口与会话服务泄漏到下一次启动。</summary>
        [UnityTest]
        public IEnumerator 关闭后停止监听并释放会话()
        {
            ServerLaunchOptions.TryParse(new[] { "-server", "-port", TestPort.ToString() }, out var options, out _);
            m_Runtime = ServerRuntime.Create(options);
            yield return null;

            var session = m_Runtime.Session;
            m_Runtime.Shutdown();
            yield return null;

            Assert.IsFalse(m_Runtime.IsListening);
            Assert.IsTrue(session.IsDisposed);
            Assert.IsNull(m_Runtime.Session);
        }

        /// <summary>重复关闭必须安全：退出流程会同时走 OnApplicationQuit 与 OnDestroy。</summary>
        [UnityTest]
        public IEnumerator 重复关闭不抛异常()
        {
            ServerLaunchOptions.TryParse(new[] { "-server", "-port", TestPort.ToString() }, out var options, out _);
            m_Runtime = ServerRuntime.Create(options);
            yield return null;

            Assert.DoesNotThrow(() => m_Runtime.Shutdown());
            Assert.DoesNotThrow(() => m_Runtime.Shutdown());
        }
    }
}
