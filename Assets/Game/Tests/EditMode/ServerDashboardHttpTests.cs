using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 状态页收包规则测试：请求头何时读完、头字段怎么解析。
    /// </summary>
    /// <remarks>
    /// 这一小段逻辑曾经把"某一行以 CRLF 收尾"当成头结束信号，导致
    /// <c>X-Admin-Token</c> 永远读不到（页面输对口令也 403）。因此这里把
    /// 每种字节形态都钉死，尤其是"读完 Host 行但还没读完"的中间态。
    /// </remarks>
    [TestFixture]
    public sealed class ServerDashboardHttpTests
    {
        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>完整请求头（CRLFCRLF 收尾）判定为结束。</summary>
        [Test]
        public void 完整请求头判定为结束()
        {
            var text = "GET / HTTP/1.1\r\nHost: x\r\nX-Admin-Token: t\r\n\r\n";
            var buffer = Bytes(text);

            Assert.IsTrue(DashboardHttpRules.IsRequestHeadTerminated(buffer, buffer.Length));
        }

        /// <summary>只有首行、还没收到空行时：不算结束（头字段可能还在路上）。</summary>
        [Test]
        public void 只有首行时不算结束()
        {
            var buffer = Bytes("GET / HTTP/1.1\r\n");
            Assert.IsFalse(
                DashboardHttpRules.IsRequestHeadTerminated(buffer, buffer.Length),
                "首行读完不等于头区结束——提前结束会丢掉 X-Admin-Token（U-101 的 403 根因）。");
        }

        /// <summary>
        /// 回归：读完一个头字段（还有其它头没读）不算结束。
        /// </summary>
        [Test]
        public void 读完一个头字段不算结束()
        {
            var buffer = Bytes("GET / HTTP/1.1\r\nHost: x\r\n");

            Assert.IsFalse(
                DashboardHttpRules.IsRequestHeadTerminated(buffer, buffer.Length),
                "头区还没遇到空行，不能提前结束——否则 X-Admin-Token 会被丢掉。");
        }

        /// <summary>读到一半（还没有换行）不算读完。</summary>
        [Test]
        public void 未读完不算结束()
        {
            var buffer = Bytes("GET / HTTP/1.1\r\nHost: x");
            Assert.IsFalse(DashboardHttpRules.IsRequestHeadTerminated(buffer, buffer.Length));
        }

        /// <summary>头字段解析：名字大小写不敏感、值去空白。</summary>
        [Test]
        public void 头字段解析出管理口令()
        {
            var text = "GET / HTTP/1.1\r\nHost: 127.0.0.1:8080\r\nX-Admin-Token:  s3cret  \r\n\r\n";
            var buffer = Bytes(text);
            var firstLineEnd = "GET / HTTP/1.1\r\n".Length;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            DashboardHttpRules.ParseHeaderFields(buffer, firstLineEnd, buffer.Length, headers);

            Assert.AreEqual("127.0.0.1:8080", headers["host"]);
            Assert.AreEqual("s3cret", headers["x-admin-token"]);
        }

        /// <summary>非法区间不崩溃、不写入。</summary>
        [Test]
        public void 头字段解析容忍非法区间()
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buffer = Bytes("GET / HTTP/1.1\r\n");

            Assert.DoesNotThrow(() => DashboardHttpRules.ParseHeaderFields(buffer, -1, buffer.Length, headers));
            Assert.DoesNotThrow(() => DashboardHttpRules.ParseHeaderFields(buffer, buffer.Length, buffer.Length, headers));
            Assert.DoesNotThrow(() => DashboardHttpRules.ParseHeaderFields(null, 0, 4, headers));
            Assert.AreEqual(0, headers.Count);
        }
    }
}
