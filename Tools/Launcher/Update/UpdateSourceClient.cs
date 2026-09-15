using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using RaidDemo.Kernel.Updates;

namespace RaidDemo.Launcher.Update
{
    /// <summary>
    /// 更新源客户端：从"更新源"读取清单与文件。
    /// </summary>
    /// <remarks>
    /// <para><b>支持两种更新源形态：</b></para>
    /// <list type="bullet">
    /// <item><c>http(s)://</c>：云主机 / 局域网静态托管（批次 3 的更新源服务端就提供这种形态）；</item>
    /// <item>本地目录：本机演示用，不需要起任何服务——少一个环节就少一个会坏的地方。</item>
    /// </list>
    ///
    /// <para><b>断点续传：</b>下载以"目标路径 + 已有字节数"为输入，
    /// 有 <c>.part</c> 残留时先发 <c>Range: bytes=N-</c>。
    /// 服务器返回 200（不支持 Range）时**必须**从头写，不能把完整内容追加到半截文件后面——
    /// 那会得到一个大小正确、内容错位的文件，而它只有在校验失败时才暴露。
    /// 这个判断是续传实现里最容易漏掉的一步，因此单独写在这里。</para>
    /// </remarks>
    public sealed class UpdateSourceClient : IDisposable
    {
        /// <summary>清单文件名（与协议一致）。</summary>
        public const string ManifestFileName = "manifest.json";

        /// <summary>本体层子目录名。</summary>
        public const string BodyLayerDirectoryName = "body";

        /// <summary>资源层子目录名。</summary>
        public const string ContentLayerDirectoryName = "content";

        /// <summary>代码层子目录名。</summary>
        public const string CodeLayerDirectoryName = "code";

        /// <summary>下载缓冲：128 KB。</summary>
        private const int BufferSize = 128 * 1024;

        /// <summary>HTTP 客户端（超时给足：云主机带宽有限时大文件下载可能很慢）。</summary>
        private readonly HttpClient m_Http;

        /// <summary>创建客户端。</summary>
        public UpdateSourceClient()
        {
            m_Http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        }

        /// <summary>判断更新源地址是不是 HTTP(S) 形态。</summary>
        public static bool IsHttpSource(string source)
        {
            return !string.IsNullOrEmpty(source) &&
                   (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 拉取远端清单。
        /// </summary>
        /// <param name="source">更新源地址（HTTP 或本地目录）。</param>
        /// <returns>解析后的清单。</returns>
        /// <exception cref="InvalidOperationException">清单缺失或无法解析。</exception>
        public UpdateManifest FetchManifest(string source)
        {
            var json = ReadAllText(source, ManifestFileName);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException($"更新源没有返回清单内容：{source}");
            }

            UpdateManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<UpdateManifest>(json, ManifestJsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"清单格式无法解析：{source}（{exception.Message}）");
            }

            if (manifest == null || manifest.schemaVersion != UpdateManifest.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"清单协议版本不受支持（本启动器支持 v{UpdateManifest.CurrentSchemaVersion}）：" +
                    $"{manifest?.schemaVersion.ToString() ?? "null"}");
            }

            return manifest;
        }

        /// <summary>
        /// 读取更新源上的一个文本文件（清单）。
        /// </summary>
        private string ReadAllText(string source, string relativePath)
        {
            if (IsHttpSource(source))
            {
                var url = CombineUrl(source, relativePath);
                using (var response = m_Http.GetAsync(url).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
            }

            var localPath = Path.Combine(source, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(localPath) ? File.ReadAllText(localPath) : null;
        }

        /// <summary>
        /// 下载（或复制）一个文件到目标路径，支持断点续传。
        /// </summary>
        /// <param name="source">更新源地址。</param>
        /// <param name="layerDirectoryName">层目录名（<c>body</c> / <c>content</c> / <c>code</c>）。</param>
        /// <param name="entry">清单条目（提供相对路径与期望大小）。</param>
        /// <param name="destinationPath">目标路径（暂存区内的路径）。</param>
        /// <param name="onBytes">每写入一批数据回调一次，参数为本次新增的字节数。</param>
        public void DownloadFile(
            string source,
            string layerDirectoryName,
            ManifestFileEntry entry,
            string destinationPath,
            Action<long> onBytes)
        {
            var relativePath = ManifestFileEntry.NormalizePath(entry.path);
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (IsHttpSource(source))
            {
                DownloadOverHttp(source, layerDirectoryName, relativePath, destinationPath, onBytes);
                return;
            }

            CopyFromLocalSource(source, layerDirectoryName, relativePath, destinationPath, onBytes);
        }

        /// <summary>从本地更新源复制文件（无断点续传——本地复制本身是原子的）。</summary>
        private static void CopyFromLocalSource(
            string source,
            string layerDirectoryName,
            string relativePath,
            string destinationPath,
            Action<long> onBytes)
        {
            var sourcePath = Path.Combine(
                source,
                layerDirectoryName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"更新源缺少文件：{relativePath}", sourcePath);
            }

            using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                CopyWithProgress(input, output, onBytes);
            }
        }

        /// <summary>通过 HTTP 下载文件，带 <c>Range</c> 续传。</summary>
        private void DownloadOverHttp(
            string source,
            string layerDirectoryName,
            string relativePath,
            string destinationPath,
            Action<long> onBytes)
        {
            var url = CombineUrl(source, layerDirectoryName + "/" + relativePath);
            long existingBytes = 0;
            if (File.Exists(destinationPath))
            {
                existingBytes = new FileInfo(destinationPath).Length;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            using (var response = m_Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                       .GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();

                // 206 = 服务器接受了续传；200 = 服务器不支持 Range（或没有断点），必须从头写。
                var appending = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                var mode = appending ? FileMode.Append : FileMode.Create;

                using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var output = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None, BufferSize))
                {
                    CopyWithProgress(input, output, onBytes);
                }
            }
        }

        /// <summary>带进度回调的流复制。</summary>
        private static void CopyWithProgress(Stream input, Stream output, Action<long> onBytes)
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                onBytes?.Invoke(read);
            }
        }

        /// <summary>拼接更新源 URL 与相对路径（逐段转义，避免中文与空格导致请求失败）。</summary>
        private static string CombineUrl(string source, string relativePath)
        {
            var baseUrl = source.TrimEnd('/');
            var segments = relativePath.Replace('\\', '/').Split('/');
            var escaped = new string[segments.Length];
            for (var index = 0; index < segments.Length; index++)
            {
                escaped[index] = Uri.EscapeDataString(segments[index]);
            }

            return baseUrl + "/" + string.Join("/", escaped);
        }

        /// <summary>释放 HTTP 客户端。</summary>
        public void Dispose()
        {
            m_Http.Dispose();
        }

        /// <summary>
        /// 清单解析选项。
        /// </summary>
        /// <remarks>
        /// 必须设置 <c>PropertyNameCaseInsensitive</c>：Unity 的 <c>JsonUtility</c> 写出的字段名
        /// 与 C# 字段名一致（小驼峰），但一旦有人手工改过清单大小写，这里也不该直接失败。
        /// 必须设置 <c>IncludeFields</c>：清单模型为了兼容 Unity 的 <c>JsonUtility</c> 用的是
        /// **public 字段**而不是属性，而 <c>System.Text.Json</c> 默认只处理属性——
        /// 少了这一项，反序列化会"成功"但得到一份各层皆为空的清单（这是个很难一眼看出的静默错误）。
        /// </remarks>
        private static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
    }
}
