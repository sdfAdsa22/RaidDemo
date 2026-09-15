using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using RaidDemo.Kernel.Updates;

namespace RaidDemo.Launcher.Update
{
    /// <summary>更新流程所处的阶段。</summary>
    public enum UpdatePhase
    {
        /// <summary>空闲。</summary>
        Idle = 0,

        /// <summary>拉取远端清单。</summary>
        FetchingManifest = 1,

        /// <summary>比对本地与远端（计算下载计划）。</summary>
        Comparing = 2,

        /// <summary>下载中。</summary>
        Downloading = 3,

        /// <summary>校验中。</summary>
        Verifying = 4,

        /// <summary>替换文件。</summary>
        Applying = 5,

        /// <summary>已完成（含"无需更新"）。</summary>
        Completed = 6,

        /// <summary>失败（已回滚）。</summary>
        Failed = 7,
    }

    /// <summary>更新进度快照（供界面或控制台消费）。</summary>
    public sealed class UpdateProgress
    {
        /// <summary>当前阶段。</summary>
        public UpdatePhase Phase { get; set; }

        /// <summary>当前处理的文件（相对路径）。</summary>
        public string CurrentFile { get; set; } = string.Empty;

        /// <summary>已处理文件数。</summary>
        public int FilesDone { get; set; }

        /// <summary>需要处理的文件总数。</summary>
        public int FilesTotal { get; set; }

        /// <summary>需要下载的总字节数。</summary>
        public long TotalBytes { get; set; }

        /// <summary>已下载字节数。</summary>
        public long BytesDone { get; set; }

        /// <summary>人类可读的说明文字。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>整体完成比例（0~1）。</summary>
        public double Ratio
        {
            get { return TotalBytes <= 0 ? 0d : Math.Min(1d, (double)BytesDone / TotalBytes); }
        }
    }

    /// <summary>一次更新执行的结果。</summary>
    public sealed class UpdateResult
    {
        /// <summary>是否成功（含"无需更新"）。</summary>
        public bool Success { get; set; }

        /// <summary>是否需要下载。</summary>
        public bool HasChanges { get; set; }

        /// <summary>计划处理的文件数。</summary>
        public int ChangeCount { get; set; }

        /// <summary>计划下载的字节数。</summary>
        public long DownloadBytes { get; set; }

        /// <summary>失败原因（成功时为 null）。</summary>
        public string FailureReason { get; set; }

        /// <summary>远端清单（成功后即"当前版本"）。</summary>
        public UpdateManifest RemoteManifest { get; set; }

        /// <summary>人类可读的摘要。</summary>
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新流程编排：拉清单 → 比对 → 下载 → 校验 → 替换 → 写快照。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把它与界面分开：</b>同一套流程要服务三种入口——图形界面（玩家）、
    /// 命令行（验收脚本、CI）与将来的自动更新。把流程写成"可被任意入口驱动"的形式，
    /// 才不至于出现"界面上能更新、脚本里更新不了"这种只能靠人复现的差异。</para>
    ///
    /// <para><b>本批次只处理本体层</b>：资源层与代码层在游戏内更新（批次 4/5），
    /// 启动器只负责把"跑不起来的那些文件"换掉。清单里若有资源/代码层，本流程会跳过并记录。</para>
    ///
    /// <para><b>失败必须回滚且保留旧版本可启动</b>：任何一步失败都走
    /// <see cref="ApplyTransaction.RollbackAll"/>，并且**不写**本地清单快照——
    /// 这样下一次启动看到的仍然是"旧版本 + 有更新可用"。</para>
    /// </remarks>
    public sealed class UpdateSession
    {
        /// <summary>安装根目录。</summary>
        private readonly string m_InstallRoot;

        /// <summary>更新源地址（HTTP 或本地目录）。</summary>
        private readonly string m_Source;

        /// <summary>进度回调（可为 null）。</summary>
        private readonly Action<UpdateProgress> m_OnProgress;

        /// <summary>日志写入器（可为 null）。</summary>
        private readonly Action<string> m_OnLog;

        /// <summary>创建会话。</summary>
        /// <param name="installRoot">安装根目录。</param>
        /// <param name="source">更新源地址。</param>
        /// <param name="onProgress">进度回调。</param>
        /// <param name="onLog">日志回调（同时写文件由调用方决定）。</param>
        public UpdateSession(
            string installRoot,
            string source,
            Action<UpdateProgress> onProgress = null,
            Action<string> onLog = null)
        {
            m_InstallRoot = installRoot;
            m_Source = source;
            m_OnProgress = onProgress;
            m_OnLog = onLog;
        }

        /// <summary>
        /// 执行一次完整的"检查并更新"。
        /// </summary>
        /// <param name="applyChanges">为 <c>false</c> 时只计算计划（检查更新，不下载）。</param>
        /// <returns>执行结果。</returns>
        public UpdateResult Run(bool applyChanges)
        {
            try
            {
                Directory.CreateDirectory(m_InstallRoot);

                Report(UpdatePhase.FetchingManifest, "正在获取更新清单…");
                using (var client = new UpdateSourceClient())
                {
                    var remote = client.FetchManifest(m_Source);
                    var local = UpdateApplier.LoadLocalManifest(m_InstallRoot);

                    Report(UpdatePhase.Comparing, "正在比对版本…");
                    var changes = UpdateManifestDiff.CompareLayer(
                        local?.body?.files ?? new List<ManifestFileEntry>(),
                        remote.body?.files ?? new List<ManifestFileEntry>());
                    var downloadBytes = UpdateManifestDiff.SumDownloadBytes(changes);

                    var result = new UpdateResult
                    {
                        RemoteManifest = remote,
                        HasChanges = changes.Count > 0,
                        ChangeCount = changes.Count,
                        DownloadBytes = downloadBytes,
                        Success = true,
                    };

                    if (changes.Count == 0)
                    {
                        result.Summary = $"已是最新版本（{DescribeVersion(remote)}）。";
                        Report(UpdatePhase.Completed, result.Summary);
                        return result;
                    }

                    result.Summary =
                        $"发现更新：{changes.Count} 个文件 / {FormatBytes(downloadBytes)}（远端 {DescribeVersion(remote)}）。";
                    Log(result.Summary);

                    if (!applyChanges)
                    {
                        Report(UpdatePhase.Completed, result.Summary + "（仅检查，未下载）");
                        return result;
                    }

                    if (!DownloadLayer(client, changes, out var failureReason))
                    {
                        result.Success = false;
                        result.FailureReason = failureReason;
                        result.Summary = "更新失败：" + failureReason;
                        Report(UpdatePhase.Failed, result.Summary);
                        return result;
                    }

                    ApplyLayer(changes, remote);
                    result.Summary = $"更新完成：{changes.Count} 个文件（{DescribeVersion(remote)}）。";
                    Report(UpdatePhase.Completed, result.Summary);
                    return result;
                }
            }
            catch (Exception exception)
            {
                var message = exception.Message;
                Report(UpdatePhase.Failed, "更新失败：" + message);
                return new UpdateResult { Success = false, FailureReason = message, Summary = "更新失败：" + message };
            }
        }

        /// <summary>下载全部变更文件到暂存区并逐个校验。</summary>
        /// <param name="client">更新源客户端。</param>
        /// <param name="changes">变更列表。</param>
        /// <param name="failureReason">失败原因。</param>
        /// <returns>全部通过返回 <c>true</c>。</returns>
        private bool DownloadLayer(UpdateSourceClient client, List<UpdateFileChange> changes, out string failureReason)
        {
            failureReason = null;

            long totalBytes = UpdateManifestDiff.SumDownloadBytes(changes);
            long doneBytes = 0;
            var doneFiles = 0;

            // 暂存区每次从干净状态开始：残留的 .part 只对"同一版本的重复下载"有意义，
            // 而版本已经变化时旧残留只会带来"大小对不上"的困惑。
            UpdateApplier.ClearStagingLayer(m_InstallRoot, UpdateSourceClient.BodyLayerDirectoryName);

            foreach (var change in changes)
            {
                if (!change.NeedsDownload)
                {
                    doneFiles++;
                    continue;
                }

                var entry = new ManifestFileEntry
                {
                    path = change.Path,
                    size = change.Size,
                    sha256 = change.Sha256,
                };

                var stagedPath = UpdateApplier.GetStagingPath(
                    m_InstallRoot,
                    UpdateSourceClient.BodyLayerDirectoryName,
                    change.Path);

                Report(
                    UpdatePhase.Downloading,
                    $"下载 {change.Path}",
                    change.Path,
                    doneFiles,
                    changes.Count,
                    totalBytes,
                    doneBytes);

                var progressed = 0L;
                client.DownloadFile(
                    m_Source,
                    UpdateSourceClient.BodyLayerDirectoryName,
                    entry,
                    stagedPath,
                    bytes =>
                    {
                        progressed += bytes;
                        doneBytes += bytes;
                        Report(
                            UpdatePhase.Downloading,
                            $"下载 {change.Path}",
                            change.Path,
                            doneFiles,
                            changes.Count,
                            totalBytes,
                            doneBytes);
                    });

                Report(
                    UpdatePhase.Verifying,
                    $"校验 {change.Path}",
                    change.Path,
                    doneFiles,
                    changes.Count,
                    totalBytes,
                    doneBytes);

                if (!UpdateApplier.VerifyStagedFile(stagedPath, entry, out var reason))
                {
                    failureReason = $"{change.Path} 校验失败：{reason}";
                    return false;
                }

                doneFiles++;
                _ = progressed;
            }

            return true;
        }

        /// <summary>把暂存文件替换到安装目录（失败整批回滚）。</summary>
        private void ApplyLayer(List<UpdateFileChange> changes, UpdateManifest remote)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var transaction = UpdateApplier.BeginTransaction(m_InstallRoot, timestamp);
            var done = 0;

            try
            {
                foreach (var change in changes)
                {
                    var targetPath = Path.Combine(
                        m_InstallRoot,
                        change.Path.Replace('/', Path.DirectorySeparatorChar));

                    Report(UpdatePhase.Applying, $"应用 {change.Path}", change.Path, done, changes.Count, 0, 0);

                    if (change.NeedsDownload)
                    {
                        var stagedPath = UpdateApplier.GetStagingPath(
                            m_InstallRoot,
                            UpdateSourceClient.BodyLayerDirectoryName,
                            change.Path);
                        transaction.PutFile(UpdateSourceClient.BodyLayerDirectoryName, change.Path, stagedPath, targetPath);
                    }
                    else
                    {
                        transaction.RemoveFile(UpdateSourceClient.BodyLayerDirectoryName, change.Path, targetPath);
                    }

                    done++;
                }
            }
            catch (Exception exception)
            {
                Log($"替换阶段失败，正在回滚：{exception.Message}");
                transaction.RollbackAll();
                throw new InvalidOperationException($"替换文件失败（已回滚）：{exception.Message}", exception);
            }

            UpdateApplier.SaveLocalManifest(m_InstallRoot, remote);
            UpdateApplier.ClearStaging(m_InstallRoot);
            UpdateApplier.PruneBackups(m_InstallRoot);
            Log($"已写入本地清单快照：{DescribeVersion(remote)}");
        }

        /// <summary>
        /// 启动游戏。
        /// </summary>
        /// <param name="executablePath">游戏可执行文件。</param>
        /// <param name="arguments">附加参数。</param>
        public static void LaunchGame(string executablePath, string arguments)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = true,
            });
        }

        /// <summary>
        /// 检查游戏是否正在运行（更新前必须确认，否则文件占用会导致替换失败）。
        /// </summary>
        /// <param name="processName">进程名（不含扩展名）。</param>
        /// <returns>正在运行返回 <c>true</c>。</returns>
        public static bool IsGameRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>版本描述文本。</summary>
        private static string DescribeVersion(UpdateManifest manifest)
        {
            var builder = new StringBuilder("本体 ");
            builder.Append(string.IsNullOrEmpty(manifest.body?.version) ? "未知" : manifest.body.version);

            if (manifest.HasContentLayer)
            {
                builder.Append(" / 资源 ").Append(manifest.content.version);
            }

            if (manifest.HasCodeLayer)
            {
                builder.Append(" / 代码 ").Append(manifest.code.version);
            }

            return builder.ToString();
        }

        /// <summary>字节数的可读格式。</summary>
        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return $"{bytes / (1024f * 1024f):F1} MB";
            }

            if (bytes >= 1024L)
            {
                return $"{bytes / 1024f:F0} KB";
            }

            return bytes + " B";
        }

        /// <summary>上报进度。</summary>
        private void Report(
            UpdatePhase phase,
            string message,
            string currentFile = "",
            int filesDone = 0,
            int filesTotal = 0,
            long totalBytes = 0,
            long bytesDone = 0)
        {
            m_OnProgress?.Invoke(new UpdateProgress
            {
                Phase = phase,
                Message = message,
                CurrentFile = currentFile,
                FilesDone = filesDone,
                FilesTotal = filesTotal,
                TotalBytes = totalBytes,
                BytesDone = bytesDone,
            });
        }

        /// <summary>写日志。</summary>
        private void Log(string message)
        {
            m_OnLog?.Invoke(message);
        }
    }
}
