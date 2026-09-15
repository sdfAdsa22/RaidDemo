using System;
using System.Collections.Generic;

namespace RaidDemo.Kernel.Updates
{
    /// <summary>
    /// 一个文件的比对结论。
    /// </summary>
    public enum UpdateChangeKind
    {
        /// <summary>远端新增：本地没有这个文件。</summary>
        Added = 0,

        /// <summary>远端内容变化：本地有同名文件但哈希不同。</summary>
        Changed = 1,

        /// <summary>远端已删除：本地有、远端清单里没有了。</summary>
        Removed = 2,
    }

    /// <summary>
    /// 单文件的比对结果（下载计划的最小单位）。
    /// </summary>
    public sealed class UpdateFileChange
    {
        /// <summary>规范化后的相对路径（正斜杠）。</summary>
        public string Path;

        /// <summary>比对类型。</summary>
        public UpdateChangeKind Kind;

        /// <summary>远端文件大小；<see cref="UpdateChangeKind.Removed"/> 时为 0。</summary>
        public long Size;

        /// <summary>远端哈希；<see cref="UpdateChangeKind.Removed"/> 时保留本地哈希以便排障。</summary>
        public string Sha256;

        /// <summary>是否需要下载。删除项不需要下载，只需要在替换阶段清理。</summary>
        public bool NeedsDownload
        {
            get { return Kind != UpdateChangeKind.Removed; }
        }
    }

    /// <summary>
    /// 清单比对：把"本地已安装"与"远端最新"两份文件列表化成一份下载 / 删除计划。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成类：</b>这段逻辑是三层更新共用的核心，而且是最容易出错的一段——
    /// 它一旦写错，现象是"更新后游戏起不来"或"永远提示有更新"。放到纯逻辑层是为了能用 EditMode
    /// 测试逐条覆盖（新增 / 变更 / 删除 / 无变化 / 大小写差异 / 路径分隔符差异），
    /// 而不是靠"跑一遍游戏看看"来验证。</para>
    ///
    /// <para><b>比较规则：</b></para>
    /// <list type="number">
    /// <item>路径先规范化（<see cref="ManifestFileEntry.NormalizePath"/>），
    /// 再用不区分大小写的比较——Windows 文件系统不区分大小写，若在这里区分，
    /// 就会出现"同一份文件被判定为新增"的重复下载甚至互相覆盖。</item>
    /// <item>本地与远端都有：哈希相同 = 无需处理；哈希不同 = <see cref="UpdateChangeKind.Changed"/>。</item>
    /// <item>只有远端有 = <see cref="UpdateChangeKind.Added"/>。</item>
    /// <item>只有本地有 = <see cref="UpdateChangeKind.Removed"/>（更新时需要删除，否则残留文件会
    /// 让"回退到旧版本"变得不可能）。</item>
    /// <item>返回结果按路径排序，保证同样的输入永远得到同样的顺序——便于日志对比与测试断言。</item>
    /// </list>
    ///
    /// <para><b>只看哈希、不看大小与时间戳：</b>大小相同不代表内容相同（同名不同内容的小改动很常见），
    /// 时间戳在打包与复制过程中必然变化。哈希是唯一可靠判据，其余字段只用于展示与预估。</para>
    /// </remarks>
    public static class UpdateManifestDiff
    {
        /// <summary>
        /// 比对一层文件列表。
        /// </summary>
        /// <param name="installed">本地已安装的文件（清单快照；首次安装时为空）。</param>
        /// <param name="remote">远端最新清单里的文件。</param>
        /// <returns>需要下载或删除的文件，按路径升序。</returns>
        public static List<UpdateFileChange> CompareLayer(
            IEnumerable<ManifestFileEntry> installed,
            IEnumerable<ManifestFileEntry> remote)
        {
            var localMap = BuildMap(installed);
            var remoteMap = BuildMap(remote);
            var result = new List<UpdateFileChange>();

            foreach (var pair in remoteMap)
            {
                ManifestFileEntry localEntry;
                if (!localMap.TryGetValue(pair.Key, out localEntry))
                {
                    result.Add(new UpdateFileChange
                    {
                        Path = pair.Value.path,
                        Kind = UpdateChangeKind.Added,
                        Size = pair.Value.size,
                        Sha256 = pair.Value.sha256,
                    });
                    continue;
                }

                if (!string.Equals(localEntry.sha256, pair.Value.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new UpdateFileChange
                    {
                        Path = pair.Value.path,
                        Kind = UpdateChangeKind.Changed,
                        Size = pair.Value.size,
                        Sha256 = pair.Value.sha256,
                    });
                }
            }

            foreach (var pair in localMap)
            {
                if (!remoteMap.ContainsKey(pair.Key))
                {
                    result.Add(new UpdateFileChange
                    {
                        Path = pair.Value.path,
                        Kind = UpdateChangeKind.Removed,
                        Size = 0,
                        Sha256 = pair.Value.sha256,
                    });
                }
            }

            result.Sort(CompareByPath);
            return result;
        }

        /// <summary>统计需要下载的字节总数（用于进度条与"本次要下多少"的提示）。</summary>
        /// <param name="changes">比对结果。</param>
        /// <returns>需要下载的总字节数；删除项不计入。</returns>
        public static long SumDownloadBytes(IEnumerable<UpdateFileChange> changes)
        {
            long total = 0;
            if (changes == null)
            {
                return total;
            }

            foreach (var change in changes)
            {
                if (change != null && change.NeedsDownload)
                {
                    total += change.Size;
                }
            }

            return total;
        }

        /// <summary>判断比对结果里是否存在任何差异（含删除项）。</summary>
        /// <param name="changes">比对结果。</param>
        /// <returns>存在差异时为 <c>true</c>。</returns>
        public static bool HasChanges(IList<UpdateFileChange> changes)
        {
            return changes != null && changes.Count > 0;
        }

        /// <summary>把文件列表转成"规范化路径 → 条目"的字典。</summary>
        /// <remarks>
        /// 重复路径以最后一次出现为准：清单由构建脚本生成，理论上不会重复；
        /// 万一重复，采用后者比抛异常更符合"更新要尽量能跑完"的取向，且后续哈希校验仍会兜底。
        /// </remarks>
        private static Dictionary<string, ManifestFileEntry> BuildMap(IEnumerable<ManifestFileEntry> entries)
        {
            var map = new Dictionary<string, ManifestFileEntry>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
            {
                return map;
            }

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.path))
                {
                    continue;
                }

                map[ManifestFileEntry.NormalizePath(entry.path)] = entry;
            }

            return map;
        }

        /// <summary>按规范化路径升序排列（大小写不敏感）。</summary>
        private static int CompareByPath(UpdateFileChange left, UpdateFileChange right)
        {
            return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
