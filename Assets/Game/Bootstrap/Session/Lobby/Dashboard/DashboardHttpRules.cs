using System;
using System.Collections.Generic;
using System.Text;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 状态页 HTTP 收包的两条纯规则：请求头何时算读完、头字段怎么解析。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么抽成 public 纯函数（U-101 的教训）：</b>最初的实现把"某一行的 CRLF"
    /// 当成请求头结束信号，于是任何请求读到第一个头字段（<c>Host</c>）就停了——
    /// <c>X-Admin-Token</c> 永远读不到，表现是"在页面上怎么输口令都是 403"。
    /// 网络收包本身不好单测，但"读完了没有""这段字节里有哪些头"是纯粹的数据判断，
    /// 抽出来之后可以在编辑模式里把每种字节形态都钉住。</para>
    /// </remarks>
    public static class DashboardHttpRules
    {
        /// <summary>判断请求头是否以空行（<c>CRLFCRLF</c>）收尾。</summary>
        /// <param name="buffer">读缓冲。</param>
        /// <param name="total">已经读到的字节数。</param>
        /// <returns>头区结束时为 true。</returns>
        /// <remarks>
        /// <para><b>这是唯一的结束条件。</b>曾经还额外把"刚读完首行"当成结束信号（为 telnet 手工测试保留的
        /// 单行容忍），但 HTTP 请求的首行与头字段可能分两个 TCP 段到达——首行读完的那一刻
        /// 头字段还在路上，提前结束就把 <c>X-Admin-Token</c> 这类头全部丢掉了。
        /// 单行容忍因此搬到读循环里，用"短暂等待后仍无数据"作为判据（见 <c>ServerDashboard.Http</c>）。</para>
        /// </remarks>
        public static bool IsRequestHeadTerminated(byte[] buffer, int total)
        {
            if (buffer == null || total < 4)
            {
                return false;
            }

            return buffer[total - 4] == (byte)'\r' && buffer[total - 3] == (byte)'\n'
                   && buffer[total - 2] == (byte)'\r' && buffer[total - 1] == (byte)'\n';
        }

        /// <summary>把请求头区间的字节解析成字段字典。</summary>
        /// <param name="buffer">读缓冲。</param>
        /// <param name="start">头部区间起点（首行之后；首行未读到时为 -1）。</param>
        /// <param name="end">头部区间终点（不含）。</param>
        /// <param name="headers">写入目标（字典大小写不敏感；重名只保留第一个）。</param>
        public static void ParseHeaderFields(byte[] buffer, int start, int end, Dictionary<string, string> headers)
        {
            if (buffer == null || headers == null || start < 0 || end <= start)
            {
                return;
            }

            var text = Encoding.UTF8.GetString(buffer, start, end - start);
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var name = line.Substring(0, colon).Trim();
                if (name.Length == 0 || headers.ContainsKey(name))
                {
                    continue;
                }

                headers[name] = line.Substring(colon + 1).Trim();
            }
        }
    }
}
