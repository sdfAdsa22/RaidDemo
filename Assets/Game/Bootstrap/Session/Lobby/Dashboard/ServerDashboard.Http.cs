using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;


namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 状态页的 HTTP 收发部分：接受连接、读请求、写响应。
    /// </summary>
    /// <remarks>与"主线程侧"分开：这一半跑在后台线程上，只做字节与排队，不碰服务器状态。</remarks>
    public sealed partial class ServerDashboard
    {
        /// <summary>后台线程：接受连接、读一行请求、交给主线程、写回响应。</summary>
        private void AcceptLoop()
        {
            while (!m_StopRequested)
            {
                TcpClient client = null;
                try
                {
                    client = m_Listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    // 停止时 Stop() 会让 AcceptTcpClient 抛异常，这是正常的退出路径。
                    if (m_StopRequested)
                    {
                        return;
                    }

                    continue;
                }

                ServeClient(client);
            }
        }

        /// <summary>服务一个连接。</summary>
        private void ServeClient(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = SocketTimeoutMilliseconds;
                client.SendTimeout = SocketTimeoutMilliseconds;

                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                var requestLine = ReadRequestHead(client, out var headers);
                if (string.IsNullOrEmpty(requestLine))
                {
                    WriteResponse(client, new DashboardResponse(400, DashboardRenderer.TextContentType, "请求为空。"), isHead: false);
                    return;
                }

                var pending = new PendingRequest
                {
                    RequestLine = requestLine,
                    RemoteAddress = remote != null ? remote.Address.ToString() : string.Empty,
                    Headers = headers,
                };

                m_Inbox.Enqueue(pending);

                var isHead = requestLine.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase);
                var response = pending.Completed.Wait(HandoffTimeoutMilliseconds) && pending.Response.Body != null
                    ? pending.Response
                    : new DashboardResponse(503, DashboardRenderer.TextContentType, "服务器正忙，请稍后重试。");

                WriteResponse(client, response, isHead);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[服务器] 状态页处理连接时出错：{exception.GetType().Name} — {exception.Message}");
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch (Exception)
                {
                    // 连接已经在异常路径里断了，关闭失败无所谓。
                }
            }
        }

        /// <summary>读请求头：首行加所有头部字段。</summary>
        /// <param name="client">客户端连接。</param>
        /// <param name="headers">解析出的头部字段（大小写不敏感；重名取第一个）。</param>
        /// <remarks>
        /// 头部字段是为管理口令（<c>X-Admin-Token</c>）读的：口令走头部而不是查询串，
        /// 就不会出现在浏览器历史、代理日志与地址栏截图里。解析仍然只做到"读得懂"为止——
        /// 不支持同名多次、不做百分号解码，因为这台服务器上只有我们自己发的请求。
        /// </remarks>
        private static string ReadRequestHead(TcpClient client, out Dictionary<string, string> headers)
        {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 注意不要在这里释放 NetworkStream：它由 TcpClient 持有，后面还要用同一条流写响应。
            var stream = client.GetStream();
            var buffer = new byte[MaxRequestBytes];
            var total = 0;
            var headerEnd = -1;
            var firstLineEnd = -1;
            string requestLine = null;

            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, 1);
                if (read <= 0)
                {
                    break;
                }

                total++;

                if (requestLine == null && buffer[total - 1] == (byte)'\n')
                {
                    var length = total > 1 && buffer[total - 2] == (byte)'\r' ? total - 2 : total - 1;
                    requestLine = Encoding.UTF8.GetString(buffer, 0, length);
                    firstLineEnd = total;

                    // 单行容忍：首行之后暂时没有数据时，等一小会儿再决定这是"只发一行的极简客户端"
                    // 还是"头字段还在路上"的正常请求（两者在首行读完后无法立刻区分）。
                    if (!stream.DataAvailable && !WaitForMoreBytes(stream))
                    {
                        headerEnd = total;
                        break;
                    }

                    continue;
                }

                if (DashboardHttpRules.IsRequestHeadTerminated(buffer, total))
                {
                    headerEnd = total;
                    break;
                }
            }

            if (headerEnd < 0)
            {
                headerEnd = total;
            }

            DashboardHttpRules.ParseHeaderFields(buffer, firstLineEnd, headerEnd, headers);

            // 把本次请求剩下的字节读完再交给上层：
            // 只读第一行就关闭连接时，客户端缓冲区里还留着未读的请求头，
            // Windows 会用 RST 而不是 FIN 收尾，客户端（curl / 浏览器 / Invoke-WebRequest）
            // 会报"远程主机强迫关闭了一个现有的连接"，即使响应其实已经发出去了。
            DrainRemainingHeaders(stream);

            return requestLine;
        }

        /// <summary>短暂等待后续字节；等到返回 true，超时返回 false。</summary>
        /// <param name="stream">客户端流。</param>
        /// <remarks>
        /// 100 毫秒是本机与公网实测都远够用的量级：正常请求的头字段与首行几乎同时到达，
        /// 而 telnet 手工敲的一行不会等这么久。等待发生在后台收包线程上，不阻塞主线程。
        /// </remarks>
        private static bool WaitForMoreBytes(NetworkStream stream)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(100);
            while (DateTime.UtcNow < deadline)
            {
                if (stream.DataAvailable)
                {
                    return true;
                }

                Thread.Sleep(2);
            }

            return stream.DataAvailable;
        }

        /// <summary>把请求头剩余部分读干净（最多等到短暂的空闲），避免关闭连接时发出 RST。</summary>
        /// <param name="stream">客户端流。</param>
        private static void DrainRemainingHeaders(NetworkStream stream)
        {
            var scratch = new byte[256];

            try
            {
                var idleDeadline = DateTime.UtcNow.AddMilliseconds(200);
                while (stream.DataAvailable && DateTime.UtcNow < idleDeadline)
                {
                    if (stream.Read(scratch, 0, scratch.Length) <= 0)
                    {
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // 读干净只是"让关闭更友好"，失败不影响已经拿到的请求行。
            }
        }

        /// <summary>写回一条响应。</summary>
        private static void WriteResponse(TcpClient client, in DashboardResponse response, bool isHead)
        {
            var body = response.Body ?? string.Empty;
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            var header = new StringBuilder(160);
            header.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(ReasonPhrase(response.StatusCode)).Append("\r\n");
            header.Append("Content-Type: ").Append(response.ContentType ?? DashboardRenderer.TextContentType).Append("\r\n");
            header.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            header.Append("Cache-Control: no-store\r\n");
            header.Append("Connection: close\r\n\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            using var stream = client.GetStream();
            stream.Write(headerBytes, 0, headerBytes.Length);

            if (!isHead && bodyBytes.Length > 0)
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            stream.Flush();
        }

        /// <summary>状态码对应的原因短语。</summary>
        private static string ReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "OK";
            }
        }
    }
}
