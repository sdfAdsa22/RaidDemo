using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RaidDemo.Kernel.Updates;

namespace RaidDemo.Launcher.Update
{
    /// <summary>
    /// 更新落地：暂存、校验、备份、原子替换与回滚。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有"备份 + 回滚"：</b>更新做到一半（换到第 150 个文件时）失败，
    /// 玩家手里的就是"一半新一半旧"的安装——这种状态连报错都不可信。
    /// 因此替换前先把旧文件挪进备份目录，任何一步失败都能整批回退到更新前。
    /// 对玩家来说只有两种结果：更新成功，或者什么都没发生。</para>
    ///
    /// <para><b>为什么用"移动"而不是"复制覆盖"：</b>产物与安装目录在同一个卷上时，
    /// <c>File.Move</c> 是原子操作且几乎不耗时；复制覆盖在断电或崩溃时会留下半截文件。</para>
    ///
    /// <para><b>目录约定（都在安装根之下）：</b></para>
    /// <code>
    /// &lt;安装根&gt;/RaidDemo.exe            ← 游戏本体
    /// &lt;安装根&gt;/.raiddemo/
    ///     manifest.json                  ← 本地清单快照（"我现在是什么版本"的唯一依据）
    ///     launcher.log                   ← 启动器日志
    ///     staging/&lt;层&gt;/&lt;相对路径&gt;       ← 下载暂存区（未校验通过前不参与比对）
    ///     backup/&lt;时间戳&gt;/&lt;层&gt;/…        ← 替换前的旧文件（回滚用；保留最近两份）
    /// </code>
    /// </remarks>
    public static class UpdateApplier
    {
        /// <summary>元数据目录名（放在安装根下，随安装一起被卸载/清理）。</summary>
        public const string MetadataDirectoryName = ".raiddemo";

        /// <summary>本地清单快照文件名。</summary>
        public const string LocalManifestFileName = "manifest.json";

        /// <summary>暂存目录名。</summary>
        public const string StagingDirectoryName = "staging";

        /// <summary>备份目录名。</summary>
        public const string BackupDirectoryName = "backup";

        /// <summary>日志文件名。</summary>
        public const string LogFileName = "launcher.log";

        /// <summary>保留的备份份数（避免长期使用后占用失控）。</summary>
        private const int KeepBackupCount = 2;

        /// <summary>
        /// 清单的 JSON 选项。
        /// </summary>
        /// <remarks>
        /// <c>IncludeFields</c> 是必需的：清单模型为兼容 Unity 的 <c>JsonUtility</c> 使用 public 字段，
        /// 而 <c>System.Text.Json</c> 默认只处理属性——缺了它，读出来的清单会是一份空壳。
        /// </remarks>
        private static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
        };

        /// <summary>取元数据目录。</summary>
        public static string GetMetadataDirectory(string installRoot)
        {
            return Path.Combine(installRoot, MetadataDirectoryName);
        }

        /// <summary>取本地清单快照路径。</summary>
        public static string GetLocalManifestPath(string installRoot)
        {
            return Path.Combine(GetMetadataDirectory(installRoot), LocalManifestFileName);
        }

        /// <summary>取暂存区路径（某一层里的某个文件）。</summary>
        public static string GetStagingPath(string installRoot, string layer, string relativePath)
        {
            return Path.Combine(
                GetMetadataDirectory(installRoot),
                StagingDirectoryName,
                layer,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// 读取本地清单快照。
        /// </summary>
        /// <param name="installRoot">安装根。</param>
        /// <returns>快照清单；不存在或损坏时返回 <c>null</c>（视为首次安装）。</returns>
        public static UpdateManifest LoadLocalManifest(string installRoot)
        {
            var path = GetLocalManifestPath(installRoot);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<UpdateManifest>(
                    File.ReadAllText(path),
                    ManifestJsonOptions);
            }
            catch (Exception)
            {
                // 快照损坏不是灾难：当成"本地清单未知"，下一次更新会按全量比对，
                // 结果最坏是重复下载已有文件，而不会破坏安装。
                return null;
            }
        }

        /// <summary>写入本地清单快照（更新成功后调用）。</summary>
        public static void SaveLocalManifest(string installRoot, UpdateManifest manifest)
        {
            var directory = GetMetadataDirectory(installRoot);
            Directory.CreateDirectory(directory);

            File.WriteAllText(
                GetLocalManifestPath(installRoot),
                JsonSerializer.Serialize(manifest, ManifestWriteOptions));
        }

        /// <summary>写入清单时的 JSON 选项（缩进 + 中文不转义，便于人工核对）。</summary>
        private static readonly JsonSerializerOptions ManifestWriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// 校验暂存文件的大小与哈希。
        /// </summary>
        /// <param name="stagedPath">暂存文件路径。</param>
        /// <param name="entry">清单条目。</param>
        /// <returns>通过返回 <c>true</c>；失败返回 <c>false</c> 并给出原因。</returns>
        public static bool VerifyStagedFile(string stagedPath, ManifestFileEntry entry, out string failureReason)
        {
            failureReason = null;

            if (!File.Exists(stagedPath))
            {
                failureReason = "暂存文件不存在";
                return false;
            }

            var info = new FileInfo(stagedPath);
            if (entry.size > 0 && info.Length != entry.size)
            {
                failureReason = $"大小不符（期望 {entry.size}，实际 {info.Length}）";
                return false;
            }

            var actualHash = UpdateFileHash.ComputeFileHash(stagedPath);
            if (!string.Equals(actualHash, entry.sha256, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "SHA-256 不符";
                return false;
            }

            return true;
        }

        /// <summary>清空某一层的暂存目录（重试下载前调用）。</summary>
        public static void ClearStagingLayer(string installRoot, string layer)
        {
            var path = Path.Combine(GetMetadataDirectory(installRoot), StagingDirectoryName, layer);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        /// <summary>清理暂存区（更新成功后调用）。</summary>
        public static void ClearStaging(string installRoot)
        {
            var path = Path.Combine(GetMetadataDirectory(installRoot), StagingDirectoryName);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        /// <summary>创建一次替换事务（负责备份与回滚）。</summary>
        /// <param name="installRoot">安装根。</param>
        /// <param name="timestamp">备份目录用的时间戳（便于人工定位）。</param>
        public static ApplyTransaction BeginTransaction(string installRoot, string timestamp)
        {
            return new ApplyTransaction(
                Path.Combine(GetMetadataDirectory(installRoot), BackupDirectoryName, timestamp));
        }

        /// <summary>清理过旧的备份目录，只保留最近若干份。</summary>
        public static void PruneBackups(string installRoot)
        {
            var backupsRoot = Path.Combine(GetMetadataDirectory(installRoot), BackupDirectoryName);
            if (!Directory.Exists(backupsRoot))
            {
                return;
            }

            var directories = new List<DirectoryInfo>(new DirectoryInfo(backupsRoot).GetDirectories());
            directories.Sort((left, right) => string.CompareOrdinal(right.Name, left.Name));

            for (var index = KeepBackupCount; index < directories.Count; index++)
            {
                try
                {
                    directories[index].Delete(true);
                }
                catch (Exception)
                {
                    // 备份清理失败不影响任何功能，静默跳过即可。
                }
            }
        }
    }

    /// <summary>
    /// 一次替换事务：每个文件"先备份旧文件，再放入新文件"，失败时整批回滚。
    /// </summary>
    public sealed class ApplyTransaction
    {
        /// <summary>已经完成的步骤（回滚时按相反顺序执行）。</summary>
        private readonly List<AppliedStep> m_Steps = new List<AppliedStep>();

        /// <summary>备份目录（本次事务的旧文件都放这里）。</summary>
        public string BackupDirectory { get; }

        /// <summary>创建事务。</summary>
        /// <param name="backupDirectory">本次事务的备份目录。</param>
        public ApplyTransaction(string backupDirectory)
        {
            BackupDirectory = backupDirectory;
        }

        /// <summary>已完成步骤数（用于进度与日志）。</summary>
        public int AppliedCount
        {
            get { return m_Steps.Count; }
        }

        /// <summary>
        /// 把暂存文件放到目标位置（目标已存在时先备份再覆盖）。
        /// </summary>
        /// <param name="layer">层名（备份目录中使用）。</param>
        /// <param name="relativePath">相对路径（正斜杠）。</param>
        /// <param name="stagedPath">暂存文件路径。</param>
        /// <param name="targetPath">目标文件路径。</param>
        public void PutFile(string layer, string relativePath, string stagedPath, string targetPath)
        {
            var backupPath = TryBackupExisting(layer, relativePath, targetPath);

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Move(stagedPath, targetPath, overwrite: true);
            m_Steps.Add(new AppliedStep(targetPath, backupPath, wasAdded: backupPath == null));
        }

        /// <summary>
        /// 删除一个已从清单中移除的文件（先备份）。
        /// </summary>
        /// <param name="layer">层名。</param>
        /// <param name="relativePath">相对路径。</param>
        /// <param name="targetPath">目标文件路径。</param>
        public void RemoveFile(string layer, string relativePath, string targetPath)
        {
            if (!File.Exists(targetPath))
            {
                return;
            }

            var backupPath = TryBackupExisting(layer, relativePath, targetPath);
            File.Delete(targetPath);
            m_Steps.Add(new AppliedStep(targetPath, backupPath, wasAdded: false));
        }

        /// <summary>
        /// 回滚全部已完成步骤：新增的文件删掉，覆盖/删除的文件从备份还原。
        /// </summary>
        public void RollbackAll()
        {
            for (var index = m_Steps.Count - 1; index >= 0; index--)
            {
                var step = m_Steps[index];
                try
                {
                    if (step.WasAdded)
                    {
                        if (File.Exists(step.TargetPath))
                        {
                            File.Delete(step.TargetPath);
                        }
                    }
                    else if (step.BackupPath != null && File.Exists(step.BackupPath))
                    {
                        var directory = Path.GetDirectoryName(step.TargetPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.Move(step.BackupPath, step.TargetPath, overwrite: true);
                    }
                }
                catch (Exception)
                {
                    // 回滚本身失败时继续处理其余文件：尽量恢复更多，而不是在第一处停下。
                }
            }

            m_Steps.Clear();
        }

        /// <summary>把目标位置现有文件挪进备份目录，返回备份路径（不存在则返回 null）。</summary>
        private string TryBackupExisting(string layer, string relativePath, string targetPath)
        {
            if (!File.Exists(targetPath))
            {
                return null;
            }

            var backupPath = Path.Combine(
                BackupDirectory,
                layer,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            var backupParent = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(backupParent))
            {
                Directory.CreateDirectory(backupParent);
            }

            File.Move(targetPath, backupPath, overwrite: true);
            return backupPath;
        }

        /// <summary>一次已完成的操作。</summary>
        private readonly struct AppliedStep
        {
            /// <summary>目标路径。</summary>
            public readonly string TargetPath;

            /// <summary>备份路径（本次新增的文件为 null）。</summary>
            public readonly string BackupPath;

            /// <summary>是否为本次新增（回滚时直接删除）。</summary>
            public readonly bool WasAdded;

            /// <summary>创建一个步骤记录。</summary>
            public AppliedStep(string targetPath, string backupPath, bool wasAdded)
            {
                TargetPath = targetPath;
                BackupPath = backupPath;
                WasAdded = wasAdded;
            }
        }
    }
}
