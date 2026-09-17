using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RaidDemo.Kernel.Updates;

namespace RaidDemo.Launcher.Update
{
    /// <summary>
    /// 把资源层（Addressables 的 catalog 与 bundle）装进**游戏自己的内容缓存**。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么启动器要管这一层：</b>本体层装进安装目录后，游戏仍然要自己去更新源
    /// 下载资源层——那条"游戏内热更"路径在真机上出现过"下载并生效后第一次进图失败"，
    /// 而"启动器装好、双击就能玩"才是玩家最先走的路。启动器与游戏跑在同一台机器、同一个用户下，
    /// 由它先把资源层写进游戏的内容缓存与状态文件，游戏启动时就会认为"资源已是最新"，
    /// 直接复用缓存里的内容。</para>
    ///
    /// <para><b>为什么是这套路径：</b>内容缓存的位置由 Unity 的
    /// <c>Application.persistentDataPath</c> 决定，Windows 上是
    /// <c>%USERPROFILE%\AppData\LocalLow\&lt;公司名&gt;\&lt;产品名&gt;</c>；
    /// 目录布局是 <c>content/&lt;内容版本&gt;/&lt;文件名&gt;</c>，另有 <c>content/state.json</c>
    /// 记录"当前生效的内容版本与 catalog 文件名"。这里与游戏侧
    /// <c>HotUpdatePaths</c> / <c>HotUpdateState</c> 的约定逐字对齐——两边必须同时改，
    /// 所以两边都写了指向对方的注释。</para>
    ///
    /// <para><b>失败不影响本体：</b>资源层装不上时只回报一条失败原因，安装目录里的本体照旧可用，
    /// 游戏下次启动会自己再试一次（那时走的是游戏内的下载逻辑）。</para>
    /// </remarks>
    public static class ContentCacheInstaller
    {
        /// <summary>游戏的公司名（与 ProjectSettings 的 companyName 一致）。</summary>
        private const string GameCompanyName = "RaidDemo";

        /// <summary>游戏的产品名（与 ProjectSettings 的 productName 一致）。</summary>
        private const string GameProductName = "RaidDemo";

        /// <summary>内容缓存根目录名（与游戏侧 HotUpdatePaths.ContentRootName 一致）。</summary>
        private const string ContentRootName = "content";

        /// <summary>状态文件名（与游戏侧 HotUpdatePaths.StateFileName 一致）。</summary>
        private const string StateFileName = "state.json";

        /// <summary>
        /// 确保资源层已经装进游戏的内容缓存。
        /// </summary>
        /// <param name="client">更新源客户端（复用它的下载实现与断点续传）。</param>
        /// <param name="source">更新源地址。</param>
        /// <param name="remote">远端清单。</param>
        /// <param name="log">日志回调。</param>
        /// <param name="failureReason">失败原因。</param>
        /// <returns>成功（或本来就不需要装）返回 true。</returns>
        public static bool TryInstall(
            UpdateSourceClient client,
            string source,
            UpdateManifest remote,
            Action<string> log,
            out string failureReason)
        {
            failureReason = null;

            if (remote?.content == null || string.IsNullOrEmpty(remote.content.version))
            {
                return true;
            }

            try
            {
                var cacheRoot = Path.Combine(ResolveGameDataRoot(), ContentRootName);
                var versionDirectory = Path.Combine(cacheRoot, Sanitize(remote.content.version));
                Directory.CreateDirectory(versionDirectory);

                var entries = CollectEntries(remote.content);
                var downloaded = 0;
                foreach (var entry in entries)
                {
                    var fileName = GetFileName(entry.path);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        continue;
                    }

                    var target = Path.Combine(versionDirectory, fileName);
                    if (IsAlreadyInstalled(target, entry))
                    {
                        continue;
                    }

                    client.DownloadFile(source, UpdateSourceClient.ContentLayerDirectoryName, entry, target, null);
                    downloaded++;
                }

                WriteState(cacheRoot, remote.content);

                log?.Invoke(downloaded == 0
                    ? $"资源层已就绪（{remote.content.version}），无需重新下载。"
                    : $"资源层已安装：{downloaded} 个文件（{remote.content.version}）。");
                return true;
            }
            catch (Exception exception)
            {
                failureReason = exception.Message;
                log?.Invoke("资源层安装失败：" + exception.Message);
                return false;
            }
        }

        /// <summary>资源层要下载的文件：清单里的全部文件，并保证 catalog 在其中。</summary>
        private static List<ManifestFileEntry> CollectEntries(ManifestContentLayer content)
        {
            var entries = new List<ManifestFileEntry>();
            if (content.files != null)
            {
                entries.AddRange(content.files);
            }

            if (content.catalog != null && !string.IsNullOrEmpty(content.catalog.path))
            {
                var catalogPath = ManifestFileEntry.NormalizePath(content.catalog.path);
                if (!entries.Exists(entry => string.Equals(
                        ManifestFileEntry.NormalizePath(entry.path), catalogPath, StringComparison.OrdinalIgnoreCase)))
                {
                    entries.Add(content.catalog);
                }
            }

            return entries;
        }

        /// <summary>文件已在位且大小一致时跳过下载——这让"检查更新"保持秒级。</summary>
        private static bool IsAlreadyInstalled(string path, ManifestFileEntry entry)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return entry.size <= 0 || new FileInfo(path).Length == entry.size;
        }

        /// <summary>写入状态文件，让游戏启动时认为"内容已是最新"。</summary>
        private static void WriteState(string cacheRoot, ManifestContentLayer content)
        {
            var catalogFileName = content.catalog != null ? GetFileName(content.catalog.path) : string.Empty;
            if (string.IsNullOrEmpty(catalogFileName))
            {
                throw new InvalidOperationException("资源层清单缺少 catalog 入口文件。");
            }

            var state = new HotUpdateStateFile
            {
                contentVersion = content.version,
                catalogFileName = catalogFileName,
                updatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                lastFailure = string.Empty,
            };

            Directory.CreateDirectory(cacheRoot);
            File.WriteAllText(
                Path.Combine(cacheRoot, StateFileName),
                JsonSerializer.Serialize(state, StateJsonOptions));
        }

        /// <summary>游戏内容缓存的根目录（不含 <c>content</c> 这一级）。</summary>
        private static string ResolveGameDataRoot()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, "AppData", "LocalLow", GameCompanyName, GameProductName);
        }

        /// <summary>取清单路径的最后一段（与游戏侧 HotUpdateClient.GetFileName 一致）。</summary>
        private static string GetFileName(string manifestPath)
        {
            var normalized = ManifestFileEntry.NormalizePath(manifestPath);
            var index = normalized.LastIndexOf('/');
            return index >= 0 ? normalized.Substring(index + 1) : normalized;
        }

        /// <summary>版本号里的路径分隔符不能带进目录名。</summary>
        private static string Sanitize(string value)
        {
            return value.Replace('/', '_').Replace('\\', '_');
        }

        /// <summary>与游戏侧 JsonUtility 的字段名逐字一致的状态文件模型。</summary>
        private sealed class HotUpdateStateFile
        {
            public string contentVersion = string.Empty;
            public string catalogFileName = string.Empty;
            public string updatedAt = string.Empty;
            public string lastFailure = string.Empty;
        }

        private static readonly JsonSerializerOptions StateJsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
        };
    }
}
