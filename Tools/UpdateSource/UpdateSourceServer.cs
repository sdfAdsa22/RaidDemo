using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RaidDemo.UpdateSource
{
    /// <summary>一条访问日志。</summary>
    public sealed class RequestLogEntry
    {
        /// <summary>发生时间。</summary>
        public string Time { get; set; }

        /// <summary>HTTP 方法。</summary>
        public string Method { get; set; }

        /// <summary>请求路径。</summary>
        public string Path { get; set; }

        /// <summary>响应状态码。</summary>
        public int Status { get; set; }

        /// <summary>耗时（毫秒）。</summary>
        public long Milliseconds { get; set; }

        /// <summary>响应字节数。</summary>
        public long Bytes { get; set; }
    }

    /// <summary>
    /// 更新源 HTTP 服务：静态托管 + 管理 API。
    /// </summary>
    /// <remarks>
    /// <para><b>URL 命名空间是"客户端视角"而不是"磁盘视角"：</b></para>
    /// <code>
    /// GET /manifest.json                → 根清单（当前发布版本）
    /// GET /body/&lt;路径&gt;                  → 解析为 versions/&lt;当前版本&gt;/body/&lt;路径&gt;
    /// GET /content/&lt;路径&gt;  /code/&lt;路径&gt;  同上
    /// GET /versions/&lt;版本&gt;/…             → 磁盘上的原始文件（校验与排障用）
    /// </code>
    /// <para>这样客户端只需要认识"一个入口 + 三个层目录"，而磁盘上仍然按版本分目录
    /// （回滚、保留历史都方便）。两者解耦的好处是：将来换存储形态（对象存储、CDN）时，
    /// 客户端一行都不用改。</para>
    ///
    /// <para><b>缓存策略是刻意的：</b><c>manifest.json</c> 必须 <c>no-store</c>——
    /// 它一旦被缓存，客户端就会永远认为"没有更新"；而具体的文件可以长缓存，
    /// 因为版本目录 + 哈希已经唯一确定内容。</para>
    ///
    /// <para><b>读写分离的权限：</b>下载与只读 API 完全开放（任何人都能复现整条更新链路），
    /// 上传 / 发布 / 删除必须带 <c>X-Auth-Token</c>。演示与作品集场景下，
    /// "别人能验证"比"严格保密"更重要，而写操作只保护到够用即可。</para>
    /// </remarks>
    public sealed class UpdateSourceServer
    {
        /// <summary>写入操作的鉴权请求头。</summary>
        public const string AuthHeaderName = "X-Auth-Token";

        /// <summary>日志保留条数。</summary>
        private const int MaxLogEntries = 200;

        /// <summary>上传体积上限（1 GB）：防止误传大文件把磁盘写满。</summary>
        private const long MaxUploadBytes = 1024L * 1024L * 1024L;

        private readonly HttpListener m_Listener = new HttpListener();
        private readonly UpdateSourceStore m_Store;
        private readonly string m_Token;
        private readonly bool m_ReadOnly;
        private readonly List<RequestLogEntry> m_Log = new List<RequestLogEntry>();
        private readonly object m_LogGate = new object();

        /// <summary>创建服务。</summary>
        /// <param name="store">更新源存储。</param>
        /// <param name="host">监听地址（<c>+</c> / <c>*</c> / 具体 IP）。</param>
        /// <param name="port">监听端口。</param>
        /// <param name="token">写操作口令。</param>
        /// <param name="readOnly">只读模式（关闭全部写操作）。</param>
        public UpdateSourceServer(UpdateSourceStore store, string host, int port, string token, bool readOnly)
        {
            m_Store = store;
            m_Token = token ?? string.Empty;
            m_ReadOnly = readOnly;

            var prefixHost = string.IsNullOrEmpty(host) ? "+" : host;
            m_Listener.Prefixes.Add($"http://{prefixHost}:{port}/");
        }

        /// <summary>取最近日志（新→旧）。</summary>
        public List<RequestLogEntry> GetLog(int take)
        {
            lock (m_LogGate)
            {
                var count = Math.Min(Math.Max(take, 1), m_Log.Count);
                var result = new List<RequestLogEntry>(count);
                for (var index = m_Log.Count - 1; index >= m_Log.Count - count; index--)
                {
                    result.Add(m_Log[index]);
                }

                return result;
            }
        }

        /// <summary>启动监听循环（阻塞）。</summary>
        public void Start()
        {
            m_Listener.Start();

            while (m_Listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = m_Listener.GetContext();
                }
                catch (Exception)
                {
                    break;
                }

                _ = Task.Run(() => HandleSafely(context));
            }
        }

        /// <summary>停止监听。</summary>
        public void Stop()
        {
            try
            {
                m_Listener.Stop();
            }
            catch (Exception)
            {
                // 停止失败不影响进程退出。
            }
        }

        /// <summary>处理请求并统一记录日志（任何异常都转成 500，不让服务被打断）。</summary>
        private void HandleSafely(HttpListenerContext context)
        {
            var started = Environment.TickCount;
            var entry = new RequestLogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Method = context.Request.HttpMethod,
                Path = context.Request.Url?.PathAndQuery ?? "/",
            };

            try
            {
                entry.Bytes = Handle(context);
                entry.Status = context.Response.StatusCode;
            }
            catch (Exception exception)
            {
                entry.Status = 500;
                TryWriteText(context, 500, "服务端错误：" + exception.Message);
            }
            finally
            {
                entry.Milliseconds = Environment.TickCount - started;
                lock (m_LogGate)
                {
                    m_Log.Add(entry);
                    if (m_Log.Count > MaxLogEntries)
                    {
                        m_Log.RemoveAt(0);
                    }
                }

                try
                {
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // 客户端提前断开时也要安静收尾。
                }
            }
        }

        /// <summary>路由分发。</summary>
        /// <returns>响应字节数（用于日志）。</returns>
        private long Handle(HttpListenerContext context)
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;

            if (path == "/" || path == "/index.html")
            {
                return WriteHtml(context, PanelHtml.Content);
            }

            if (path == "/health")
            {
                return WriteText(context, 200, "ok");
            }

            if (path == "/api/status")
            {
                return WriteJson(context, BuildStatus());
            }

            if (path == "/api/log")
            {
                return WriteJson(context, GetLog(50));
            }

            if (path == "/api/verify")
            {
                var version = context.Request.QueryString["version"] ?? m_Store.ReadCurrentVersion();
                return WriteJson(context, m_Store.Verify(version));
            }

            if (path == "/api/upload" && method == "POST")
            {
                return HandleUpload(context);
            }

            if ((path == "/api/publish" || path == "/api/delete") && method == "POST")
            {
                return HandlePublishOrDelete(context, path);
            }

            if (method == "GET" || method == "HEAD")
            {
                return ServeStatic(context, path);
            }

            TryWriteText(context, 405, "不支持的方法。");
            return 0;
        }

        /// <summary>组装状态快照（面板首页用）。</summary>
        private object BuildStatus()
        {
            var current = m_Store.ReadCurrentManifest();
            return new
            {
                root = m_Store.RootDirectory,
                readOnly = m_ReadOnly,
                freeBytes = m_Store.GetFreeSpaceBytes(),
                current = new
                {
                    version = current?.body?.version ?? string.Empty,
                    generatedAt = current?.generatedAt ?? string.Empty,
                    contentVersion = current?.content?.version ?? string.Empty,
                    codeVersion = current?.code?.version ?? string.Empty,
                    schemaVersion = current?.schemaVersion ?? 0,
                },
                versions = m_Store.ListVersions(),
            };
        }

        /// <summary>处理上传（原始 zip 字节流）。</summary>
        private long HandleUpload(HttpListenerContext context)
        {
            if (!IsAuthorized(context))
            {
                return WriteJson(context, new { success = false, message = "口令不正确。" }, 401);
            }

            if (m_ReadOnly)
            {
                return WriteJson(context, new { success = false, message = "服务端运行在只读模式。" }, 403);
            }

            var version = (context.Request.QueryString["version"] ?? string.Empty).Trim();
            var overwrite = string.Equals(context.Request.QueryString["overwrite"], "true", StringComparison.OrdinalIgnoreCase);

            if (context.Request.ContentLength64 > MaxUploadBytes)
            {
                return WriteJson(context, new { success = false, message = "上传体积超过 1 GB 上限。" }, 413);
            }

            var tempZip = Path.Combine(Path.GetTempPath(), "raiddemo-upload-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var output = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    context.Request.InputStream.CopyTo(output);
                }

                var report = m_Store.Import(version, tempZip, overwrite);
                return WriteJson(context, report, report.Success ? 200 : 400);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip))
                    {
                        File.Delete(tempZip);
                    }
                }
                catch (Exception)
                {
                    // 临时文件清理失败不影响导入结果。
                }
            }
        }

        /// <summary>处理发布 / 删除。</summary>
        private long HandlePublishOrDelete(HttpListenerContext context, string path)
        {
            if (!IsAuthorized(context))
            {
                return WriteJson(context, new { success = false, message = "口令不正确。" }, 401);
            }

            if (m_ReadOnly)
            {
                return WriteJson(context, new { success = false, message = "服务端运行在只读模式。" }, 403);
            }

            var version = (context.Request.QueryString["version"] ?? string.Empty).Trim();

            if (path == "/api/publish")
            {
                var published = m_Store.Publish(version);
                return WriteJson(context, new
                {
                    success = published,
                    message = published ? $"已发布版本 {version}。" : $"发布失败：版本 {version} 不存在。",
                }, published ? 200 : 400);
            }

            var deleted = m_Store.DeleteVersion(version, out var message);
            return WriteJson(context, new { success = deleted, message }, deleted ? 200 : 400);
        }

        /// <summary>静态文件：根清单、三层目录（按当前版本解析）与 versions 原始目录。</summary>
        private long ServeStatic(HttpListenerContext context, string path)
        {
            // 路径必须先解码：客户端会把空格等字符转义成 %20（Unity 产物里确实存在
            // "RaidDemo_Data/Resources/unity default resources" 这样的文件名），
            // 而 Uri.AbsolutePath 保留转义形式——不解码就会以"文件不存在"收场（404）。
            path = Uri.UnescapeDataString(path);

            string filePath;

            if (path == "/manifest.json")
            {
                filePath = m_Store.CurrentManifestPath;
            }
            else if (path.StartsWith("/versions/", StringComparison.OrdinalIgnoreCase))
            {
                filePath = Path.Combine(
                    m_Store.RootDirectory,
                    path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            }
            else
            {
                var trimmed = path.TrimStart('/');
                var separator = trimmed.IndexOf('/');
                if (separator <= 0)
                {
                    TryWriteText(context, 404, "未找到。");
                    return 0;
                }

                var layer = trimmed.Substring(0, separator);
                if (layer != "body" && layer != "content" && layer != "code")
                {
                    TryWriteText(context, 404, "未找到。");
                    return 0;
                }

                var currentVersion = m_Store.ReadCurrentVersion();
                if (string.IsNullOrEmpty(currentVersion))
                {
                    TryWriteText(context, 404, "更新源尚未发布任何版本。");
                    return 0;
                }

                filePath = Path.Combine(
                    m_Store.GetVersionDirectory(currentVersion),
                    layer,
                    trimmed.Substring(separator + 1).Replace('/', Path.DirectorySeparatorChar));
            }

            var fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(m_Store.RootDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                TryWriteText(context, 404, "未找到。");
                return 0;
            }

            var isManifest = string.Equals(
                Path.GetFileName(fullPath),
                UpdateSourceStore.ManifestFileName,
                StringComparison.OrdinalIgnoreCase);
            return WriteFile(context, fullPath, isManifest);
        }

        /// <summary>
        /// 写文件响应，支持 <c>Range</c> 断点续传。
        /// </summary>
        /// <param name="context">请求上下文。</param>
        /// <param name="filePath">文件路径。</param>
        /// <param name="noStore">是否禁用缓存（清单必须禁用）。</param>
        /// <returns>响应字节数。</returns>
        private static long WriteFile(HttpListenerContext context, string filePath, bool noStore)
        {
            var response = context.Response;
            var info = new FileInfo(filePath);
            var length = info.Length;

            response.ContentType = GetContentType(filePath);
            response.Headers["Accept-Ranges"] = "bytes";
            response.Headers["Cache-Control"] = noStore ? "no-store" : "public, max-age=31536000";

            long start = 0;
            long end = length - 1;
            var rangeHeader = context.Request.Headers["Range"];
            var isPartial = false;

            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var spec = rangeHeader.Substring("bytes=".Length).Split(',')[0].Trim();
                var dash = spec.IndexOf('-');
                if (dash >= 0)
                {
                    var startText = spec.Substring(0, dash);
                    var endText = spec.Substring(dash + 1);

                    if (long.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStart))
                    {
                        start = parsedStart;
                    }

                    if (!string.IsNullOrEmpty(endText) &&
                        long.TryParse(endText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedEnd))
                    {
                        end = Math.Min(parsedEnd, length - 1);
                    }

                    if (start >= length || start < 0)
                    {
                        response.StatusCode = 416;
                        response.Headers["Content-Range"] = $"bytes */{length}";
                        return 0;
                    }

                    isPartial = true;
                }
            }

            var count = end - start + 1;
            response.StatusCode = isPartial ? 206 : 200;
            response.ContentLength64 = count;
            if (isPartial)
            {
                response.Headers["Content-Range"] = $"bytes {start}-{end}/{length}";
            }

            if (context.Request.HttpMethod == "HEAD")
            {
                return 0;
            }

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                stream.Seek(start, SeekOrigin.Begin);

                var buffer = new byte[128 * 1024];
                long remaining = count;
                while (remaining > 0)
                {
                    var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                    {
                        break;
                    }

                    response.OutputStream.Write(buffer, 0, read);
                    remaining -= read;
                }
            }

            return count;
        }

        /// <summary>写 JSON 响应。</summary>
        private static long WriteJson(HttpListenerContext context, object payload, int statusCode = 200)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            return WriteBody(context, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>写 HTML 响应。</summary>
        private static long WriteHtml(HttpListenerContext context, string html)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            return WriteBody(context, Encoding.UTF8.GetBytes(html));
        }

        /// <summary>写纯文本响应。</summary>
        private static long WriteText(HttpListenerContext context, int statusCode, string text)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/plain; charset=utf-8";
            return WriteBody(context, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>尽力写一个文本响应（异常路径使用，忽略二次异常）。</summary>
        private static void TryWriteText(HttpListenerContext context, int statusCode, string text)
        {
            try
            {
                WriteText(context, statusCode, text);
            }
            catch (Exception)
            {
                // 客户端已断开等情况，忽略。
            }
        }

        /// <summary>写出字节内容。</summary>
        private static long WriteBody(HttpListenerContext context, byte[] bytes)
        {
            context.Response.ContentLength64 = bytes.Length;
            if (context.Request.HttpMethod != "HEAD")
            {
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }

            return bytes.Length;
        }

        /// <summary>校验写操作口令。</summary>
        private bool IsAuthorized(HttpListenerContext context)
        {
            if (string.IsNullOrEmpty(m_Token))
            {
                return false;
            }

            var provided = context.Request.Headers[AuthHeaderName];
            return !string.IsNullOrEmpty(provided) &&
                   string.Equals(provided, m_Token, StringComparison.Ordinal);
        }

        /// <summary>按扩展名给 Content-Type。</summary>
        private static string GetContentType(string filePath)
        {
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".json":
                    return "application/json; charset=utf-8";
                case ".html":
                    return "text/html; charset=utf-8";
                case ".txt":
                case ".log":
                    return "text/plain; charset=utf-8";
                default:
                    return "application/octet-stream";
            }
        }

        /// <summary>JSON 序列化选项。</summary>
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
