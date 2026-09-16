using System;
using System.IO;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Kernel.Server;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器配置文件（<c>server.config.json</c>）的读取与合并测试。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这些用例必须存在：</b>"双击一个 exe 就开服"之后，配置文件成了
    /// 最常用的输入通道，而它的失败方式全都是**静默**的——端口没读到就还是 7777、
    /// 房间名没读到就还是默认名字，服务器照样起得来，只有玩家发现"连不上我改的那个端口"。
    /// 因此这里把每一种合并情形与每一种错误情形都钉死。</para>
    ///
    /// <para>测试全部在临时目录里自造文件，不依赖仓库里的任何配置；
    /// 用例名用中文，与工程内其他测试保持一致。</para>
    /// </remarks>
    [TestFixture]
    public sealed class ServerConfigFileTests
    {
        /// <summary>每个用例独立的临时目录。</summary>
        private string m_Directory;

        [SetUp]
        public void SetUp()
        {
            m_Directory = Path.Combine(
                Path.GetTempPath(),
                "rd-serverconfig-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(m_Directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Directory))
            {
                Directory.Delete(m_Directory, recursive: true);
            }
        }

        /// <summary>没有配置文件时，服务器按内置默认值启动。</summary>
        [Test]
        public void 没有配置文件时使用内置默认值()
        {
            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server" }, m_Directory, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsTrue(options.IsServerRequested);
            Assert.AreEqual(LaunchOptions.DefaultPort, options.Port);
            Assert.AreEqual(LaunchOptions.DefaultRoomName, options.RoomName);
            Assert.AreEqual(LaunchOptions.DefaultSaveDirectory, options.SaveDirectory);
            Assert.AreEqual(LaunchOptions.DefaultDashboardPort, options.DashboardPort);
            Assert.AreEqual(LaunchOptions.DefaultDiscoveryPort, options.DiscoveryPort);
            Assert.AreEqual(LaunchOptions.DefaultRaidTimeLimitSeconds, options.RaidTimeLimitSeconds);
            Assert.AreEqual(LaunchOptions.DefaultReconnectGraceSeconds, options.ReconnectGraceSeconds);
            Assert.AreEqual(LaunchOptions.DefaultTransportWatchdogSeconds, options.TransportWatchdogSeconds);
        }

        /// <summary>配置文件里的每一项都能生效（这是面板与 Linux 脚本共用的那套字段）。</summary>
        [Test]
        public void 配置文件全部字段生效()
        {
            WriteConfig(
                "{\"port\": 8123, \"room\": \"工业区\", \"saveDir\": \"saves_demo\", \"logLevel\": \"warning\", " +
                "\"map\": \"GreyboxRaid\", \"raidDuration\": 240, \"autoStart\": 15, \"dashboardPort\": 8181, " +
                "\"discoveryPort\": 47778, \"grace\": 30, \"watchdog\": 5}");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server" }, m_Directory, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(8123, options.Port);
            Assert.AreEqual("工业区", options.RoomName);
            Assert.AreEqual("saves_demo", options.SaveDirectory);
            Assert.AreEqual(RaidDemo.Kernel.LogLevel.Warning, options.MinimumLogLevel);
            Assert.AreEqual("GreyboxRaid", options.MapSceneName);
            Assert.AreEqual(240f, options.RaidTimeLimitSeconds);
            Assert.AreEqual(15f, options.AutoStartSeconds);
            Assert.AreEqual(8181, options.DashboardPort);
            Assert.AreEqual(47778, options.DiscoveryPort);
            Assert.AreEqual(30f, options.ReconnectGraceSeconds);
            Assert.AreEqual(5f, options.TransportWatchdogSeconds);
        }

        /// <summary>命令行必须覆盖配置文件——这是"既有验收脚本一行都不用改"的前提。</summary>
        [Test]
        public void 命令行覆盖配置文件()
        {
            WriteConfig("{\"port\": 7999, \"room\": \"配置里的房间\", \"raidDuration\": 300}");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server", "-port", "8000", "-room", "命令行的房间" },
                m_Directory,
                out var options,
                out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(8000, options.Port, "命令行给的端口必须赢。");
            Assert.AreEqual("命令行的房间", options.RoomName);
            Assert.AreEqual(300f, options.RaidTimeLimitSeconds, "命令行没给的项仍取配置文件的值。");
        }

        /// <summary>配置文件里没写的字段回退到内置默认（哨兵值不产生参数）。</summary>
        [Test]
        public void 缺失字段回退内置默认()
        {
            WriteConfig("{\"port\": 8123}");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server" }, m_Directory, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(8123, options.Port);
            Assert.AreEqual(LaunchOptions.DefaultRoomName, options.RoomName);
            Assert.AreEqual(LaunchOptions.DefaultRaidTimeLimitSeconds, options.RaidTimeLimitSeconds);
        }

        /// <summary>显式写了非法值时必须报错，且报错要指向配置文件。</summary>
        [Test]
        public void 非法取值报错并指向配置文件()
        {
            WriteConfig("{\"port\": 0}");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server" }, m_Directory, out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains(ServerConfigDocument.FileName, error);
            StringAssert.Contains("端口", error);
        }

        /// <summary>不是合法 JSON 时启动失败并给出可读原因（不静默降级成默认值）。</summary>
        [Test]
        public void 损坏的JSON报错()
        {
            WriteConfig("{ 这不是 JSON");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server" }, m_Directory, out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("JSON", error);
        }

        /// <summary>显式传了 -config 却找不到文件：必须报错，不能按默认值静默启动。</summary>
        [Test]
        public void 显式指定的配置文件不存在时报错()
        {
            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server", "-config", "不存在的配置.json" },
                m_Directory,
                out _,
                out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("找不到配置文件", error);
        }

        /// <summary>-config 的相对路径以"可执行文件所在目录"为基准（含子目录）。</summary>
        [Test]
        public void 相对配置路径按可执行文件目录解析()
        {
            WriteConfig("{\"port\": 8333}", Path.Combine("configs", "server.config.json"));

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server", "-config", Path.Combine("configs", "server.config.json") },
                m_Directory,
                out var options,
                out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(8333, options.Port);
        }

        /// <summary>客户端 / 单机进程不受服务器配置文件影响（避免"改服务器顺手改坏客户端"）。</summary>
        [Test]
        public void 客户端模式忽略配置文件()
        {
            WriteConfig("{\"port\": 7999, \"logLevel\": \"error\"}");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-connect", "127.0.0.1:7777" },
                m_Directory,
                out var options,
                out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(LaunchOptions.DefaultPort, options.Port);
            Assert.AreEqual(RaidDemo.Kernel.LogLevel.Info, options.MinimumLogLevel);
        }

        /// <summary>显式给了 -config 时即使不是服务器模式也读它（调用方的明确意图）。</summary>
        [Test]
        public void 显式指定配置时非服务器模式也读取()
        {
            WriteConfig("{\"port\": 8333}");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-config", ServerConfigDocument.FileName },
                m_Directory,
                out var options,
                out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(8333, options.Port);
            Assert.IsFalse(options.IsServerRequested);
        }

        /// <summary>全哨兵值的文件等价于"什么都没配"。</summary>
        [Test]
        public void 全哨兵值等价于未配置()
        {
            WriteConfig(
                "{\"port\": -1, \"room\": \"\", \"saveDir\": \"\", \"logLevel\": \"\", \"map\": \"\", " +
                "\"raidDuration\": -1, \"autoStart\": -1, \"dashboardPort\": -1, \"discoveryPort\": -1, " +
                "\"grace\": -1, \"watchdog\": -1}");

            var ok = LaunchOptions.TryParseWithServerConfig(
                new[] { "-server" }, m_Directory, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(LaunchOptions.DefaultPort, options.Port);
            Assert.AreEqual(LaunchOptions.DefaultRoomName, options.RoomName);
            Assert.AreEqual(LaunchOptions.DefaultDashboardPort, options.DashboardPort);
            Assert.IsNull(options.MapSceneName);
        }

        /// <summary>
        /// 共享模型里的默认值必须与 <see cref="LaunchOptions"/> 的默认值一致。
        /// </summary>
        /// <remarks>
        /// 面板用它自己的常量生成默认配置文件，游戏用 <c>LaunchOptions</c> 的常量兜底。
        /// 两处一旦漂移，就会出现"面板显示 7777、服务器实际监听 8080"这种看不见的错位。
        /// </remarks>
        [Test]
        public void 共享默认值与启动参数默认值一致()
        {
            Assert.AreEqual(LaunchOptions.DefaultPort, ServerConfigDocument.DefaultPort);
            Assert.AreEqual(LaunchOptions.DefaultRoomName, ServerConfigDocument.DefaultRoomName);
            Assert.AreEqual(LaunchOptions.DefaultSaveDirectory, ServerConfigDocument.DefaultSaveDirectory);
            Assert.AreEqual(LaunchOptions.DefaultDashboardPort, ServerConfigDocument.DefaultDashboardPort);
            Assert.AreEqual(LaunchOptions.DefaultDiscoveryPort, ServerConfigDocument.DefaultDiscoveryPort);
            Assert.AreEqual(
                LaunchOptions.DefaultRaidTimeLimitSeconds,
                ServerConfigDocument.DefaultRaidDurationSeconds);
            Assert.AreEqual(
                LaunchOptions.DefaultReconnectGraceSeconds,
                ServerConfigDocument.DefaultReconnectGraceSeconds);
            Assert.AreEqual(
                LaunchOptions.DefaultTransportWatchdogSeconds,
                ServerConfigDocument.DefaultTransportWatchdogSeconds);
        }

        /// <summary>把一份 JSON 写进临时目录（可指定子路径）。</summary>
        private void WriteConfig(string json, string relativePath = null)
        {
            var path = Path.Combine(m_Directory, relativePath ?? ServerConfigDocument.FileName);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
        }
    }
}
