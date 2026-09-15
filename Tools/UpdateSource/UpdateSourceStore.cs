using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using RaidDemo.Kernel.Updates;

namespace RaidDemo.UpdateSource
{
    /// <summary>一个版本目录的摘要信息。</summary>
    public sealed class VersionInfo
    {
        /// <summary>版本号（目录名）。</summary>
        public string Version { get; set; }

        /// <summary>该版本的本体层文件数。</summary>
        public int FileCount { get; set; }

        /// <summary>该版本的本体层总字节数。</summary>
        public long TotalBytes { get; set; }

        /// <summary>清单里的生成时间。</summary>
        public string GeneratedAt { get; set; }

        /// <summary>是否当前发布版本。</summary>
        public bool IsCurrent { get; set; }

        /// <summary>是否包含资源层 / 代码层。</summary>
        public bool HasContent { get; set; }

        /// <summary>是否包含代码层。</summary>
        public bool HasCode { get; set; }
    }

    /// <summary>校验结果。</summary>
    public sealed class VerifyReport
    {
        /// <summary>被校验的版本。</summary>
        public string Version { get; set; }

        /// <summary>是否全部通过。</summary>
        public bool Passed { get; set; }

        /// <summary>校验文件数。</summary>
        public int CheckedFiles { get; set; }

        /// <summary>不一致的文件（路径 + 原因）。</summary>
        public List<string> Problems { get; set; } = new List<string>();
    }

    /// <summary>上传导入结果。</summary>
    public sealed class ImportReport
    {
        /// <summary>是否成功。</summary>
        public bool Success { get; set; }

        /// <summary>版本号。</summary>
        public string Version { get; set; }

        /// <summary>文件数。</summary>
        public int FileCount { get; set; }

        /// <summary>总字节数。</summary>
        public long TotalBytes { get; set; }

        /// <summary>失败原因或提示。</summary>
        public string Message { get; set; }

        /// <summary>哈希校验发现的问题（为空表示全部一致）。</summary>
        public List<string> Problems { get; set; } = new List<string>();
    }

    /// <summary>
    /// 更新源目录的读写：版本列表、发布 / 回滚、校验、导入上传包、删除。
    /// </summary>
    /// <remarks>
    /// <para><b>目录约定</b>（与协议一致）：</para>
    /// <code>
    /// &lt;root&gt;/manifest.json              ← 当前发布清单（"发布"就是覆盖它）
    /// &lt;root&gt;/versions/&lt;版本&gt;/manifest.json
    /// &lt;root&gt;/versions/&lt;版本&gt;/body|content|code/…
    /// </code>
    ///
    /// <para><b>发布为什么是"复制清单"而不是"改指针"：</b>客户端只认
    /// <c>&lt;源&gt;/manifest.json</c> 一个入口。把当前版本的清单复制到根上，
    /// 客户端与服务端就都不需要理解"指针文件"这一层概念——协议更小，出错面更小。
    /// 回滚 = 用旧版本的清单覆盖根清单，同样一条路径。</para>
    ///
    /// <para><b>发布原子性：</b>写根清单用"临时文件 + 原子替换"，
    /// 避免客户端恰好读到写了一半的 JSON（那会表现为"清单解析失败"，而不是"版本旧一点"）。</para>
    /// </remarks>
    public sealed class UpdateSourceStore
    {
        /// <summary>清单文件名。</summary>
        public const string ManifestFileName = "manifest.json";

        /// <summary>版本目录名。</summary>
        public const string VersionsDirectoryName = "versions";

        /// <summary>版本号允许的最大长度（防御性限制，避免超长目录名）。</summary>
        private const int MaxVersionLength = 64;

        /// <summary>JSON 选项：清单模型用的是 public 字段，必须开 IncludeFields。</summary>
        private static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>更新源根目录。</summary>
        public string RootDirectory { get; }

        /// <summary>创建存储。</summary>
        /// <param name="rootDirectory">更新源根目录（不存在时自动创建）。</param>
        public UpdateSourceStore(string rootDirectory)
        {
            RootDirectory = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(Path.Combine(RootDirectory, VersionsDirectoryName));
        }

        /// <summary>根清单路径。</summary>
        public string CurrentManifestPath
        {
            get { return Path.Combine(RootDirectory, ManifestFileName); }
        }

        /// <summary>读取当前发布清单；不存在或损坏时返回 <c>null</c>。</summary>
        public UpdateManifest ReadCurrentManifest()
        {
            return ReadManifestOrNull(CurrentManifestPath);
        }

        /// <summary>当前发布版本号；未发布过时返回空字符串。</summary>
        public string ReadCurrentVersion()
        {
            var manifest = ReadCurrentManifest();
            return manifest?.body?.version ?? string.Empty;
        }

        /// <summary>列出全部版本（按版本号倒序，便于"最新的在最上面"）。</summary>
        public List<VersionInfo> ListVersions()
        {
            var current = ReadCurrentVersion();
            var result = new List<VersionInfo>();
            var versionsRoot = Path.Combine(RootDirectory, VersionsDirectoryName);

            foreach (var directory in new DirectoryInfo(versionsRoot).GetDirectories())
            {
                var manifest = ReadManifestOrNull(Path.Combine(directory.FullName, ManifestFileName));
                var info = new VersionInfo
                {
                    Version = directory.Name,
                    IsCurrent = string.Equals(directory.Name, current, StringComparison.Ordinal),
                    GeneratedAt = manifest?.generatedAt ?? string.Empty,
                    HasContent = manifest?.HasContentLayer ?? false,
                    HasCode = manifest?.HasCodeLayer ?? false,
                };

                if (manifest?.body?.files != null)
                {
                    info.FileCount = manifest.body.files.Count;
                    foreach (var entry in manifest.body.files)
                    {
                        info.TotalBytes += entry.size;
                    }
                }

                result.Add(info);
            }

            result.Sort((left, right) => string.CompareOrdinal(right.Version, left.Version));
            return result;
        }

        /// <summary>取某个版本的目录（不校验存在性）。</summary>
        public string GetVersionDirectory(string version)
        {
            return Path.Combine(RootDirectory, VersionsDirectoryName, version);
        }

        /// <summary>版本目录是否存在且包含清单。</summary>
        public bool VersionExists(string version)
        {
            return File.Exists(Path.Combine(GetVersionDirectory(version), ManifestFileName));
        }

        /// <summary>
        /// 发布（或回滚到）指定版本：把该版本的清单复制为根清单。
        /// </summary>
        /// <param name="version">版本号。</param>
        /// <returns>成功返回 <c>true</c>，版本不存在返回 <c>false</c>。</returns>
        public bool Publish(string version)
        {
            var source = Path.Combine(GetVersionDirectory(version), ManifestFileName);
            if (!File.Exists(source))
            {
                return false;
            }

            // 先写临时文件再替换：客户端任何时刻读到的都是"完整且旧或新"的清单。
            var tempPath = CurrentManifestPath + ".tmp";
            File.Copy(source, tempPath, overwrite: true);
            File.Move(tempPath, CurrentManifestPath, overwrite: true);
            return true;
        }

        /// <summary>
        /// 校验某个版本：逐文件重算 SHA-256 并与清单比对。
        /// </summary>
        /// <param name="version">版本号。</param>
        /// <returns>校验报告。</returns>
        public VerifyReport Verify(string version)
        {
            var report = new VerifyReport { Version = version, Passed = true };
            var versionDirectory = GetVersionDirectory(version);
            var manifest = ReadManifestOrNull(Path.Combine(versionDirectory, ManifestFileName));

            if (manifest == null)
            {
                report.Passed = false;
                report.Problems.Add("版本不存在或清单无法解析");
                return report;
            }

            if (manifest.body?.files != null)
            {
                foreach (var entry in manifest.body.files)
                {
                    var filePath = Path.Combine(
                        versionDirectory,
                        "body",
                        entry.path.Replace('/', Path.DirectorySeparatorChar));
                    VerifyEntry(filePath, entry, "body", report);
                }
            }

            return report;
        }

        /// <summary>
        /// 导入一个上传的版本包（zip）。
        /// </summary>
        /// <param name="version">版本号（同时作为目录名）。</param>
        /// <param name="zipPath">上传的 zip 路径。</param>
        /// <param name="overwrite">版本已存在时是否覆盖。</param>
        /// <returns>导入报告。</returns>
        /// <remarks>
        /// <para>导入是"先解压到临时目录 → 校验清单与哈希 → 通过后才移入版本目录"的三步：
        /// 半途失败不会在 <c>versions/</c> 里留下一个"看起来存在但其实不完整"的版本——
        /// 那种版本一旦被发布，客户端就会大面积更新失败。</para>
        /// </remarks>
        public ImportReport Import(string version, string zipPath, bool overwrite)
        {
            var report = new ImportReport { Version = version };

            var validationError = ValidateVersionName(version);
            if (validationError != null)
            {
                report.Message = validationError;
                return report;
            }

            var versionDirectory = GetVersionDirectory(version);
            if (Directory.Exists(versionDirectory) && !overwrite)
            {
                report.Message = $"版本 {version} 已存在；如需替换请勾选“覆盖同名版本”。";
                return report;
            }

            var tempDirectory = versionDirectory + ".importing";
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }

            try
            {
                ExtractZipSafely(zipPath, tempDirectory);

                // 上传包可能是"版本目录的内容"，也可能是"包含版本目录的外层"——
                // 两种都被接受：如果根下没有 manifest.json，就向下找唯一一层子目录。
                var effectiveRoot = ResolvePackageRoot(tempDirectory);
                if (effectiveRoot == null)
                {
                    report.Message = "压缩包里找不到 manifest.json。请上传“生成更新清单”产出的版本目录（或其压缩包）。";
                    return report;
                }

                var manifest = ReadManifestOrNull(Path.Combine(effectiveRoot, ManifestFileName));
                if (manifest == null)
                {
                    report.Message = "压缩包里的 manifest.json 无法解析。";
                    return report;
                }

                if (manifest.schemaVersion != UpdateManifest.CurrentSchemaVersion)
                {
                    report.Message = $"清单协议版本不受支持：v{manifest.schemaVersion}（本服务端支持 v{UpdateManifest.CurrentSchemaVersion}）。";
                    return report;
                }

                // 逐文件核对哈希：上传过程中被截断/损坏的包必须在这里被挡住。
                var verify = new VerifyReport { Version = version, Passed = true };
                foreach (var entry in manifest.body?.files ?? new List<ManifestFileEntry>())
                {
                    var filePath = Path.Combine(
                        effectiveRoot,
                        "body",
                        entry.path.Replace('/', Path.DirectorySeparatorChar));
                    VerifyEntry(filePath, entry, "body", verify);
                }

                report.Problems = verify.Problems;
                report.FileCount = manifest.body?.files?.Count ?? 0;
                foreach (var entry in manifest.body?.files ?? new List<ManifestFileEntry>())
                {
                    report.TotalBytes += entry.size;
                }

                if (!verify.Passed)
                {
                    report.Message = "清单与文件不一致，已拒绝导入（详见 problems）。";
                    return report;
                }

                if (Directory.Exists(versionDirectory))
                {
                    Directory.Delete(versionDirectory, true);
                }

                Directory.Move(effectiveRoot, versionDirectory);

                // 外层被剥离的临时目录可能还有残留，直接清掉。
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }

                report.Success = true;
                report.Message = $"导入成功：{report.FileCount} 个文件 / {report.TotalBytes / (1024f * 1024f):F1} MB";
                return report;
            }
            catch (Exception exception)
            {
                report.Message = "导入失败：" + exception.Message;
                return report;
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    try
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch (Exception)
                    {
                        // 清理失败不影响导入结论。
                    }
                }
            }
        }

        /// <summary>删除一个版本目录（当前发布版本不允许删除）。</summary>
        /// <param name="version">版本号。</param>
        /// <param name="message">结果说明。</param>
        /// <returns>成功返回 <c>true</c>。</returns>
        public bool DeleteVersion(string version, out string message)
        {
            if (string.Equals(version, ReadCurrentVersion(), StringComparison.Ordinal))
            {
                message = "当前发布版本不能删除；请先发布另一个版本。";
                return false;
            }

            var directory = GetVersionDirectory(version);
            if (!Directory.Exists(directory))
            {
                message = "版本不存在。";
                return false;
            }

            Directory.Delete(directory, true);
            message = $"已删除版本 {version}。";
            return true;
        }

        /// <summary>取根目录所在卷的剩余空间（放不下新版本时面板能提前提示）。</summary>
        public long GetFreeSpaceBytes()
        {
            try
            {
                return new DriveInfo(Path.GetPathRoot(RootDirectory)).AvailableFreeSpace;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>校验单个文件条目。</summary>
        private static void VerifyEntry(string filePath, ManifestFileEntry entry, string layer, VerifyReport report)
        {
            report.CheckedFiles++;

            if (!File.Exists(filePath))
            {
                report.Passed = false;
                report.Problems.Add($"{layer}/{entry.path}：文件缺失");
                return;
            }

            var info = new FileInfo(filePath);
            if (entry.size > 0 && info.Length != entry.size)
            {
                report.Passed = false;
                report.Problems.Add($"{layer}/{entry.path}：大小不符（清单 {entry.size}，实际 {info.Length}）");
                return;
            }

            var actual = UpdateFileHash.ComputeFileHash(filePath);
            if (!string.Equals(actual, entry.sha256, StringComparison.OrdinalIgnoreCase))
            {
                report.Passed = false;
                report.Problems.Add($"{layer}/{entry.path}：SHA-256 不符");
            }
        }

        /// <summary>读取清单（不存在或损坏返回 null）。</summary>
        private static UpdateManifest ReadManifestOrNull(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(path), ManifestJsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>版本号合法性校验：禁止路径分隔符与相对路径片段。</summary>
        private static string ValidateVersionName(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return "版本号不能为空。";
            }

            if (version.Length > MaxVersionLength)
            {
                return $"版本号过长（上限 {MaxVersionLength} 个字符）。";
            }

            if (version.Contains("/") || version.Contains("\\") || version.Contains(".."))
            {
                return "版本号不能包含路径分隔符或 ..";
            }

            foreach (var character in Path.GetInvalidFileNameChars())
            {
                if (version.IndexOf(character) >= 0)
                {
                    return "版本号包含非法字符：" + character;
                }
            }

            return null;
        }

        /// <summary>安全解压（阻止 zip slip：条目路径不得逃出目标目录）。</summary>
        private static void ExtractZipSafely(string zipPath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // 目录条目
                        continue;
                    }

                    var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
                    if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("压缩包包含越界路径，已拒绝：" + entry.FullName);
                    }

                    var parent = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    entry.ExtractToFile(targetPath, overwrite: true);
                }
            }
        }

        /// <summary>
        /// 找到包内"真正的版本根"：优先根目录，其次是唯一一层子目录里带清单的那个。
        /// </summary>
        private static string ResolvePackageRoot(string extractedDirectory)
        {
            if (File.Exists(Path.Combine(extractedDirectory, ManifestFileName)))
            {
                return extractedDirectory;
            }

            foreach (var directory in Directory.GetDirectories(extractedDirectory))
            {
                if (File.Exists(Path.Combine(directory, ManifestFileName)))
                {
                    return directory;
                }
            }

            return null;
        }
    }
}
