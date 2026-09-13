using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器地址报告的测试。
    /// </summary>
    /// <remarks>
    /// 本类只覆盖纯逻辑部分（文本拼装与网卡枚举），不涉及引擎生命周期，
    /// 因此可以放在 EditMode。真正「起监听」的验证在 PlayMode 测试里（见 Tests/PlayMode）。
    /// </remarks>
    [TestFixture]
    public sealed class ServerAddressReporterTests
    {
        private const int ReportPort = 45888;

        /// <summary>报告必须同时给出本机回环地址、端口与云主机提示。</summary>
        [Test]
        public void 报告包含本机地址端口与云主机提示()
        {
            var report = ServerAddressReporter.BuildReport(ReportPort);

            StringAssert.Contains(ServerAddressReporter.LoopbackAddress, report);
            StringAssert.Contains(ReportPort.ToString(), report);
            StringAssert.Contains("公网 IP", report);
        }

        /// <summary>枚举局域网地址不应抛异常，且结果里不能出现回环地址。</summary>
        [Test]
        public void 局域网地址枚举不含回环地址()
        {
            var addresses = ServerAddressReporter.EnumerateLanIPv4();

            Assert.IsNotNull(addresses);
            foreach (var address in addresses)
            {
                Assert.AreNotEqual(ServerAddressReporter.LoopbackAddress, address);
            }
        }
    }
}
