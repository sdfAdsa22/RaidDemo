using System;
using System.Collections.Generic;
using System.IO;
using RaidDemo.Kernel.Updates;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 构建期清单生成器：把一次出包的产物整理成"可直接上传的更新版本目录"。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决的问题：</b>启动器判断"要不要更新"靠的是一份逐文件清单（路径 + 大小 + SHA-256）。
    /// 这份清单必须由构建流程生成——手工维护必然漏项，而漏项的表现是玩家更新后游戏起不来，
    /// 排查成本极高。把生成动作绑在出包之后，才能保证"清单与产物永远同源"。</para>
    ///
    /// <para><b>产物形态：</b><c>Builds/Update/&lt;本体版本&gt;/</c> 下是
    /// <c>manifest.json</c> + <c>body/</c>（+ 后续批次的 <c>content/</c>、<c>code/</c>）。
    /// 目录结构与更新源的 <c>versions/&lt;版本&gt;/</c> 一一对应，上传时整目录复制即可。</para>
    ///
    /// <para><b>为什么把文件复制一份而不是原地引用：</b>更新源目录要求"一个版本一个自包含目录"
    /// （回滚 = 换一个目录），而构建输出目录会被下一次构建覆盖（Unity 会清空重建）。
    /// 复制一份带来约 160 MB 的额外磁盘占用，换来的是"这个目录任何时候都能直接上传/回滚"。</para>
    ///
    /// <para><b>哈希算在副本上：</b>清单里的哈希是对**上传出去的那份文件**计算的，
    /// 而不是对构建输出计算的——两者若因复制过程中的问题不一致，先算副本就能立刻暴露，
    /// 而不是等玩家下载后校验失败。</para>
    /// </remarks>
    public static class UpdateManifestBuilder
    {
        /// <summary>更新产物的根目录（相对工程根；<c>Builds/</c> 已被 .gitignore 忽略）。</summary>
        public const string UpdateRootDirectory = "Builds/Update";

        /// <summary>清单文件名。协议中固定，消费方按此名拉取。</summary>
        public const string ManifestFileName = "manifest.json";

        /// <summary>本体层子目录名。</summary>
        public const string BodyLayerDirectoryName = "body";

        /// <summary>资源层子目录名（批次 4 起使用）。</summary>
        public const string ContentLayerDirectoryName = "content";

        /// <summary>代码层子目录名（批次 5 起使用）。</summary>
        public const string CodeLayerDirectoryName = "code";

        /// <summary>
        /// Unity 生成的崩溃符号备份目录后缀。
        /// </summary>
        /// <remarks>
        /// 该目录（<c>*_BackUpThisFolder_ButDontShipItWithYourGame</c>）体积大、只用于崩溃符号化，
        /// 不属于运行时文件。把它排除可以避免"每次更新都在下一份几十 MB 的无用数据"。
        /// </remarks>
        private const string BackupFolderSuffix = "_BackUpThisFolder_ButDontShipItWithYourGame";

        /// <summary>取某个版本的更新产物目录（相对工程根）。</summary>
        /// <param name="bodyVersion">本体版本号（如 <c>0.10.0</c>）。</param>
        /// <returns>形如 <c>Builds/Update/0.10.0</c> 的相对路径。</returns>
        public static string GetVersionDirectory(string bodyVersion)
        {
            return Path.Combine(UpdateRootDirectory, bodyVersion).Replace('\\', '/');
        }

        /// <summary>
        /// 生成某个版本的更新产物目录与清单。
        /// </summary>
        /// <param name="bodyDirectory">本体构建输出目录（如 <c>Builds/Client/0.10.0</c>）。</param>
        /// <param name="contentDirectory">资源层目录；没有则传 <c>null</c>（清单里该层为空）。</param>
        /// <param name="codeDirectory">代码层目录；没有则传 <c>null</c>。</param>
        /// <param name="bodyVersion">本体版本号（与 PlayerSettings 的 bundleVersion 一致）。</param>
        /// <param name="contentVersion">资源层版本号；无资源层时忽略。</param>
        /// <param name="codeVersion">代码层版本号；无代码层时忽略。</param>
        /// <returns>生成的清单对象；失败返回 <c>null</c>。</returns>
        public static UpdateManifest Generate(
            string bodyDirectory,
            string contentDirectory,
            string codeDirectory,
            string bodyVersion,
            string contentVersion,
            string codeVersion)
        {
            if (string.IsNullOrWhiteSpace(bodyVersion))
            {
                Debug.LogError("[更新清单] 本体版本号为空，无法生成清单。请先设置 PlayerSettings 的 bundleVersion。");
                return null;
            }

            if (string.IsNullOrEmpty(bodyDirectory) || !Directory.Exists(bodyDirectory))
            {
                Debug.LogError($"[更新清单] 本体产物目录不存在：{bodyDirectory}");
                return null;
            }

            var versionDirectory = GetVersionDirectory(bodyVersion);

            // 每次生成都从干净目录开始：产物目录必须与清单严格一致，
            // 残留的旧文件会让"清单里没有、目录里有"这种不一致无法被发现。
            if (Directory.Exists(versionDirectory))
            {
                Directory.Delete(versionDirectory, true);
            }

            var manifest = new UpdateManifest
            {
                schemaVersion = UpdateManifest.CurrentSchemaVersion,
                generatedAt = UpdateManifest.CreateTimestamp(),
            };

            long bodyBytes;
            var bodyEntries = CopyLayer(
                bodyDirectory,
                Path.Combine(versionDirectory, BodyLayerDirectoryName),
                out bodyBytes);
            manifest.body = new ManifestLayer { version = bodyVersion, files = bodyEntries };

            if (!string.IsNullOrEmpty(contentDirectory) && Directory.Exists(contentDirectory))
            {
                long contentBytes;
                var contentEntries = CopyLayer(
                    contentDirectory,
                    Path.Combine(versionDirectory, ContentLayerDirectoryName),
                    out contentBytes);
                manifest.content = new ManifestContentLayer
                {
                    version = string.IsNullOrEmpty(contentVersion) ? bodyVersion : contentVersion,
                    catalog = PickCatalogEntry(contentEntries),
                    files = contentEntries,
                };
            }

            if (!string.IsNullOrEmpty(codeDirectory) && Directory.Exists(codeDirectory))
            {
                long codeBytes;
                var codeEntries = CopyLayer(
                    codeDirectory,
                    Path.Combine(versionDirectory, CodeLayerDirectoryName),
                    out codeBytes);
                manifest.code = new ManifestCodeLayer
                {
                    version = string.IsNullOrEmpty(codeVersion) ? bodyVersion : codeVersion,
                    assemblies = codeEntries.FindAll(IsAssemblyEntry),
                    metadata = codeEntries.FindAll(entry => !IsAssemblyEntry(entry)),
                };
            }

            var manifestPath = Path.Combine(versionDirectory, ManifestFileName);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));

            Debug.Log(
                $"[更新清单] 已生成 {manifestPath} ｜ 本体 {bodyEntries.Count} 个文件 / " +
                $"{bodyBytes / (1024f * 1024f):F1} MB ｜ 资源 {CountOf(manifest.content)} ｜ 代码 {CountOf(manifest.code)}");

            return manifest;
        }

        /// <summary>
        /// 复制一层文件并同时算出清单条目。
        /// </summary>
        /// <param name="sourceDirectory">源目录。</param>
        /// <param name="destinationDirectory">目标目录（会按需创建）。</param>
        /// <param name="totalBytes">输出：该层复制后的总字节数。</param>
        /// <returns>按路径升序排列的清单条目。</returns>
        private static List<ManifestFileEntry> CopyLayer(
            string sourceDirectory,
            string destinationDirectory,
            out long totalBytes)
        {
            var entries = new List<ManifestFileEntry>();
            totalBytes = 0;

            var sourceRoot = new DirectoryInfo(sourceDirectory);
            foreach (var file in sourceRoot.GetFiles("*", SearchOption.AllDirectories))
            {
                if (IsExcluded(file))
                {
                    continue;
                }

                var relativePath = ToRelativePosixPath(sourceRoot.FullName, file.FullName);
                var targetPath = Path.Combine(
                    destinationDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));

                var targetParent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetParent))
                {
                    Directory.CreateDirectory(targetParent);
                }

                file.CopyTo(targetPath, true);

                entries.Add(new ManifestFileEntry
                {
                    path = relativePath,
                    size = file.Length,
                    sha256 = UpdateFileHash.ComputeFileHash(targetPath),
                });

                totalBytes += file.Length;
            }

            entries.Sort((left, right) =>
                string.Compare(left.path, right.path, StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        /// <summary>判断文件是否应排除在更新包之外。</summary>
        private static bool IsExcluded(FileInfo file)
        {
            var directory = file.Directory;
            while (directory != null)
            {
                if (directory.Name.EndsWith(BackupFolderSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                directory = directory.Parent;
            }

            return false;
        }

        /// <summary>把绝对路径转成相对根的、正斜杠分隔的路径。</summary>
        /// <remarks>
        /// 不用 <c>Path.GetRelativePath</c>：不同运行时的可用性不一致，
        /// 而这里只需要"去掉前缀"这一种最简单的情形，自己实现更可控。
        /// </remarks>
        private static string ToRelativePosixPath(string rootFullPath, string fileFullPath)
        {
            var root = rootFullPath.Replace('\\', '/').TrimEnd('/') + "/";
            var full = fileFullPath.Replace('\\', '/');

            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(root.Length);
            }

            return full;
        }

        /// <summary>在资源层条目里挑出 catalog 入口（Addressables 的目录文件）。</summary>
        /// <remarks>
        /// <para>识别规则：文件名以 <c>catalog</c> 开头，扩展名是 <c>.bin</c>（Addressables 2.x 的默认
        /// 二进制目录）或 <c>.json</c>（文本目录）。**必须排除 <c>.hash</c>**——
        /// 那是给运行时做增量判断用的伴随文件，不是入口。</para>
        ///
        /// <para>产物名由 Addressables 自己生成（如 <c>catalog_0.10.0.bin</c>），
        /// 因此这里只做"形状匹配"，不做唯一性强制；命名规则变化时只改这一处。</para>
        /// </remarks>
        private static ManifestFileEntry PickCatalogEntry(List<ManifestFileEntry> entries)
        {
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry.path);
                if (!name.StartsWith("catalog", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>判断代码层条目是否属于"热更程序集"（而不是补充元数据）。</summary>
        private static bool IsAssemblyEntry(ManifestFileEntry entry)
        {
            var name = Path.GetFileName(entry.path);
            return name.StartsWith("RaidDemo.", StringComparison.OrdinalIgnoreCase) &&
                   name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>统计资源层文件数（无该层时为 0）。</summary>
        private static int CountOf(ManifestContentLayer layer)
        {
            return layer == null ? 0 : layer.files.Count + (layer.catalog == null ? 0 : 1);
        }

        /// <summary>统计代码层文件数（无该层时为 0）。</summary>
        private static int CountOf(ManifestCodeLayer layer)
        {
            return layer == null ? 0 : layer.assemblies.Count + layer.metadata.Count;
        }
    }
}
