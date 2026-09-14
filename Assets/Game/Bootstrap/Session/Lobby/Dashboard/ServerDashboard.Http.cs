using System;
using System.Collections.Concurrent;
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
                var requestLine = ReadRequestLine(client);
                if (string.IsNullOrEmpty(requestLine))
                {
                    WriteResponse(client, new DashboardResponse(400, DashboardRenderer.TextContentType, "请求为空。"), isHead: false);
                    return;
                }

                var pending = new PendingRequest
                {
                    RequestLine = requestLine,
                    RemoteAddress = remote != null ? remote.Address.ToString() : string.Empty,
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

        /// <summary>读第一行（到 CRLF 为止）。</summary>
        private static string ReadRequestLine(TcpClient client)
        {
            // 注意不要在这里释放 NetworkStream：它由 TcpClient 持有，后面还要用同一条流写响应。
            var stream = client.GetStream();
            var buffer = new byte[MaxRequestBytes];
            var total = 0;
            var headerEnd = -1;
            string requestLine = null;

            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, 1);
                if (read <= 0)
                {
                    break;
                }

                total++;

                if (requestLine == null && total >= 1 && buffer[total - 1] == (byte)'\n')
                {
                    var length = total > 1 && buffer[total - 2] == (byte)'\r' ? total - 2 : total - 1;
                    requestLine = Encoding.UTF8.GetString(buffer, 0, length);
                }

                // 继续读到请求头结束（空行 CRLFCRLF）。
                if (total >= 4
                    && buffer[total - 4] == (byte)'\r' && buffer[total - 3] == (byte)'\n'
                    && buffer[total - 2] == (byte)'\r' && buffer[total - 1] == (byte)'\n')
                {
                    headerEnd = total;
                    break;
                }

                // 容许只发一行就结束的极简客户端（例如 telnet 手工测试）。
                if (requestLine != null && total >= 2
                    && buffer[total - 2] == (byte)'\r' && buffer[total - 1] == (byte)'\n'
                    && headerEnd < 0)
                {
                    headerEnd = total;
                    break;
                }
            }

            if (headerEnd < 0)
            {
                headerEnd = total;
            }

            // 把本次请求剩下的字节读完再交给上层：
            // 只读第一行就关闭连接时，客户端缓冲区里还留着未读的请求头，
            // Windows 会用 RST 而不是 FIN 收尾，客户端（curl / 浏览器 / Invoke-WebRequest）
            // 会报"远程主机强迫关闭了一个现有的连接"，即使响应其实已经发出去了。
            DrainRemainingHeaders(stream);

            return requestLine;
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
