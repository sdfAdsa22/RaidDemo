using System;
using System.Collections;
using System.IO;
using RaidDemo.Kernel.Updates;
using UnityEngine;
using UnityEngine.Networking;

namespace RaidDemo.HotUpdate
{
    /// <summary>
    /// 更新源客户端（游戏内版本）：拉清单、下载文件。
    /// </summary>
    /// <remarks>
    /// <para><b>与启动器那份客户端的关系：</b>协议、路径规则、哈希算法三样完全一致
    /// （都来自 <c>RaidDemo.Kernel.Pure.Updates</c> 的同一份实现），差别只在下载手段——
    /// 启动器跑在游戏进程外用 <c>HttpClient</c>，这里跑在 Unity 里用 <c>UnityWebRequest</c>。</para>
    ///
    /// <para><b>支持两种更新源：</b><c>http(s)://</c> 与本地目录。
    /// 本地目录是为了"本机演示不依赖任何服务"；正式分发用 HTTP。</para>
    ///
    /// <para><b>失败一律不写最终文件：</b>下载先落 <c>.part</c>，校验通过才改名。
    /// 这样中断/损坏的下载永远不会被当成"已就绪的内容"。</para>
    /// </remarks>
    public static class HotUpdateClient
    {
        /// <summary>下载超时（秒）。云主机带宽有限时大文件较慢，给足余量。</summary>
        private const int TimeoutSeconds = 300;

        /// <summary>内容层的 URL 前缀（与更新源目录结构一致）。</summary>
        public const string ContentUrlPrefix = "content";

        /// <summary>判断更新源是否为 HTTP(S) 形态。</summary>
        public static bool IsHttpSource(string source)
        {
            return !string.IsNullOrEmpty(source) &&
                   (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 拉取远端清单。
        /// </summary>
        /// <param name="source">更新源地址。</param>
        /// <param name="onDone">成功回调（参数为清单文本）。</param>
        /// <param name="onError">失败回调。</param>
        /// <returns>协程。</returns>
        public static IEnumerator FetchManifestText(string source, Action<string> onDone, Action<string> onError)
        {
            if (!IsHttpSource(source))
            {
                var localPath = Path.Combine(source, "manifest.json");
                if (!File.Exists(localPath))
                {
                    onError?.Invoke("更新源缺少 manifest.json：" + localPath);
                    yield break;
                }

                onDone?.Invoke(File.ReadAllText(localPath));
                yield break;
            }

            using (var request = UnityWebRequest.Get(source.TrimEnd('/') + "/manifest.json"))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"清单请求失败：{request.responseCode} {request.error}");
                    yield break;
                }

                onDone?.Invoke(request.downloadHandler.text);
            }
        }

        /// <summary>
        /// 下载内容层的全部文件到目标目录（已存在且校验通过的文件会跳过）。
        /// </summary>
        /// <param name="source">更新源地址。</param>
        /// <param name="version">内容版本号（决定目标目录）。</param>
        /// <param name="entries">清单里的文件条目。</param>
        /// <param name="onProgress">进度回调（0~1，当前文件）。</param>
        /// <param name="onError">失败回调。</param>
        /// <returns>协程。</returns>
        public static IEnumerator DownloadContent(
            string source,
            string version,
            System.Collections.Generic.IList<ManifestFileEntry> entries,
            Action<float, string> onProgress,
            Action<string> onError)
        {
            var targetDirectory = HotUpdatePaths.EnsureDirectory(HotUpdatePaths.GetVersionDirectory(version));
            var total = entries.Count;

            for (var index = 0; index < total; index++)
            {
                var entry = entries[index];
                var fileName = GetFileName(entry.path);
                var targetPath = Path.Combine(targetDirectory, fileName);
                var partPath = targetPath + ".part";

                if (UpdateFileHash.Verify(targetPath, entry.sha256))
                {
                    onProgress?.Invoke((index + 1f) / total, fileName);
                    continue;
                }

                if (File.Exists(partPath))
                {
                    File.Delete(partPath);
                }

                if (IsHttpSource(source))
                {
                    var url = source.TrimEnd('/') + "/" + ContentUrlPrefix + "/" + Uri.EscapeDataString(fileName);
                    using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
                    {
                        request.downloadHandler = new DownloadHandlerFile(partPath);
                        request.timeout = TimeoutSeconds;
                        yield return request.SendWebRequest();

                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            onError?.Invoke($"下载 {fileName} 失败：{request.responseCode} {request.error}");
                            yield break;
                        }
                    }
                }
                else
                {
                    var sourcePath = Path.Combine(source, ContentUrlPrefix, fileName);
                    if (!File.Exists(sourcePath))
                    {
                        onError?.Invoke("更新源缺少文件：" + sourcePath);
                        yield break;
                    }

                    File.Copy(sourcePath, partPath, overwrite: true);
                }

                if (!UpdateFileHash.Verify(partPath, entry.sha256))
                {
                    File.Delete(partPath);
                    onError?.Invoke($"下载 {fileName} 后校验失败（文件可能损坏）");
                    yield break;
                }

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(partPath, targetPath);
                onProgress?.Invoke((index + 1f) / total, fileName);
            }
        }

        /// <summary>
        /// 从清单条目里取文件名。
        /// </summary>
        /// <remarks>
        /// 内容层的清单路径按协议是相对路径（可能含目录），但**磁盘上按文件名扁平存放**：
        /// Addressables 的产物名（含哈希）本身已经全局唯一，扁平存放能让"运行时按文件名寻址"
        /// 这条规则只有一处实现（见 <c>HotUpdateRuntime.TransformInternalId</c>）。
        /// </remarks>
        public static string GetFileName(string manifestPath)
        {
            var normalized = ManifestFileEntry.NormalizePath(manifestPath);
            var separator = normalized.LastIndexOf('/');
            return separator >= 0 ? normalized.Substring(separator + 1) : normalized;
        }
    }
}
