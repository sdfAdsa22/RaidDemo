using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// P-51：传输层自愈看门狗的规则测试。
    /// </summary>
    /// <remarks>
    /// <para>真正的自愈要在多进程里才能验证（验收脚本负责那一段）；但"什么时候算全员静默、
    /// 什么时候不该触发、冷却怎么算"是纯逻辑，全部能在编辑模式里钉死。
    /// 这几条恰好也是最容易写错、错了最难查的部分——阈值写反会让正常对局被反复重建，
    /// 冷却漏掉会让一次故障变成一串重启。</para>
    ///
    /// <para>时间参数都显式传入（<c>now</c>），因此测试不依赖真实时钟，跑得既快又稳。</para>
    /// </remarks>
    [TestFixture]
    public sealed class P51TransportWatchdogTests
    {
        /// <summary>全员静默满阈值：必须触发。</summary>
        [Test]
        public void 全员静默达到阈值时触发重建()
        {
            var watchdog = new TransportWatchdog(allSilentSeconds: 2.5f, rebuildCooldownSeconds: 10f);

            // 第一帧先进入观察窗口（返回 false：静默还没数满）。
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 100f));

            // 不到阈值：继续观察。
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 102f));

            // 满 2.5 秒：触发。
            Assert.IsTrue(watchdog.ShouldRebuildTransport(true, true, false, 102.5f));
        }

        /// <summary>只要还有人说话，就不是"全员静默"，并且观察窗口要重新计时。</summary>
        [Test]
        public void 有人说话时不触发且重新计时()
        {
            var watchdog = new TransportWatchdog(allSilentSeconds: 2.5f, rebuildCooldownSeconds: 10f);

            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 100f));
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 102f));

            // 102.2 有人说话：窗口清空。
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, true, 102.2f));

            // 重新从 103 开始数：到 105.4 还差一点，不触发。
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 103f));
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 105.4f));

            // 105.5 正好 2.5 秒：触发。
            Assert.IsTrue(watchdog.ShouldRebuildTransport(true, true, false, 105.5f));
        }

        /// <summary>权威世界里没人：不进观察窗口（大厅挂机、看界面不算故障）。</summary>
        [Test]
        public void 权威世界没人时不触发()
        {
            var watchdog = new TransportWatchdog(allSilentSeconds: 2.5f, rebuildCooldownSeconds: 10f);

            Assert.IsFalse(watchdog.ShouldRebuildTransport(false, true, false, 100f));
            Assert.IsFalse(watchdog.ShouldRebuildTransport(false, true, false, 200f));
        }

        /// <summary>没有在线客户端（都在宽限里）："全员静默"判定不适用。</summary>
        [Test]
        public void 没有在线客户端时不触发()
        {
            var watchdog = new TransportWatchdog(allSilentSeconds: 2.5f, rebuildCooldownSeconds: 10f);

            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, false, false, 100f));
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, false, false, 200f));
        }

        /// <summary>刚连上、还没说过话的客户端由调用方折算成"有人说话"：抑制判定。</summary>
        [Test]
        public void 新连接的加载窗口不触发()
        {
            var watchdog = new TransportWatchdog(allSilentSeconds: 2.5f, rebuildCooldownSeconds: 10f);

            // 调用约定：anyLiveClientHeardFrom = true（"从没说过话"视作还在说话）。
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, true, 100f));
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, true, 300f));
        }

        /// <summary>触发一次之后的冷却期内不再触发；冷却过后重新数满阈值才触发。</summary>
        [Test]
        public void 重建后的冷却期内不重复触发()
        {
            var watchdog = new TransportWatchdog(allSilentSeconds: 2.5f, rebuildCooldownSeconds: 10f);

            watchdog.NoteRebuildStarted(200f);

            // 冷却期内：即使静默成立也不触发。
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 205f));

            // 冷却刚过：重新进入观察窗口。
            Assert.IsFalse(watchdog.ShouldRebuildTransport(true, true, false, 210.1f));

            // 数满阈值：再次触发。
            Assert.IsTrue(watchdog.ShouldRebuildTransport(true, true, false, 212.7f));
        }

        /// <summary>判定时长会被夹到上下界之间，防止验收参数写出荒唐值。</summary>
        [Test]
        public void 判定时长被夹到上下界()
        {
            Assert.AreEqual(
                TransportWatchdog.MinAllSilentSeconds,
                new TransportWatchdog(allSilentSeconds: 0.1f).AllSilentSeconds);

            Assert.AreEqual(
                TransportWatchdog.MaxAllSilentSeconds,
                new TransportWatchdog(allSilentSeconds: 999f).AllSilentSeconds);
        }

        /// <summary>空房场景：有人在宽限里等待重连时应当重建。</summary>
        [Test]
        public void 空房等待重连时触发()
        {
            var watchdog = new TransportWatchdog();

            Assert.IsFalse(watchdog.ShouldRebuildWhileEmpty(false, 300f), "没有等待者不该触发。");
            Assert.IsTrue(watchdog.ShouldRebuildWhileEmpty(true, 300f), "有人在宽限里等待时应当重建套接字。");
        }

        /// <summary>空房场景同样受冷却约束：不能每帧都重建。</summary>
        [Test]
        public void 空房触发后进入冷却()
        {
            var watchdog = new TransportWatchdog(allSilentSeconds: 2.5f, rebuildCooldownSeconds: 10f);

            watchdog.NoteRebuildStarted(300f);

            Assert.IsFalse(watchdog.ShouldRebuildWhileEmpty(true, 305f), "冷却期内不重复触发。");
            Assert.IsTrue(watchdog.ShouldRebuildWhileEmpty(true, 310.1f), "冷却过后允许再次触发。");
        }

        /// <summary>启动参数：<c>-watchdog</c> 的时长生效。</summary>
        [Test]
        public void 看门狗时长参数生效()
        {
            var ok = LaunchOptions.TryParse(
                new[] { "-server", "-watchdog", "5" }, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(5f, options.TransportWatchdogSeconds);
        }

        /// <summary>启动参数：<c>-watchdog 0</c> 表示关闭自愈（出问题时用于对照排查）。</summary>
        [Test]
        public void 看门狗可以关闭()
        {
            var ok = LaunchOptions.TryParse(
                new[] { "-server", "-watchdog", "0" }, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(0f, options.TransportWatchdogSeconds);
        }

        /// <summary>启动参数：越界值要报错，而不是悄悄取默认。</summary>
        [Test]
        public void 看门狗越界参数被拒绝()
        {
            Assert.IsFalse(
                LaunchOptions.TryParse(new[] { "-server", "-watchdog", "0.5" }, out _, out var lowError),
                "低于下界（且非 0）的值应当被拒绝。");
            Assert.IsNotNull(lowError);

            Assert.IsFalse(
                LaunchOptions.TryParse(new[] { "-server", "-watchdog", "40" }, out _, out var highError),
                "高于上界的值应当被拒绝。");
            Assert.IsNotNull(highError);
        }
    }
}
