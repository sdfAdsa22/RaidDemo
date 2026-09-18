using System;
using System.IO;
using RaidDemo.Kernel.Updates;

namespace RaidDemo.Launcher.Update
{
    /// <summary>
    /// 安装根路径守卫：把清单里的相对路径解析成"一定位于安装根之内"的绝对路径。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么协议层校验过了这里还要再校验：</b>纵深防御。协议校验保证"进来的是干净路径"，
    /// 这里保证"出去的是根内路径"——任何未来新增的写入/删除入口只要走这个函数，
    /// 就不会因为某处忘了校验而变成任意文件写。M13-02 的探针正是
    /// <c>Path.Combine(installRoot, "..\\escape.txt")</c> 直接落盘，绕过了所有判断。</para>
    ///
    /// <para><b>为什么必须 GetFullPath 之后再比较前缀：</b><c>Path.Combine</c> 只是字符串拼接，
    /// <c>a/../b</c>、<c>./b</c>、重复分隔符都要等解析成绝对路径后才等价于真实落点；
    /// 只比较原始字符串会放过真实的越界路径。</para>
    /// </remarks>
    public static class InstallPathGuard
    {
        /// <summary>
        /// 解析一个清单相对路径，确保结果位于安装根之内。
        /// </summary>
        /// <param name="installRoot">安装根目录（可以尚不存在）。</param>
        /// <param name="relativePath">清单里的相对路径（正斜杠或反斜杠均可）。</param>
        /// <returns>解析后的绝对路径。</returns>
        /// <exception cref="InvalidOperationException">路径为空、不安全或解析后越出安装根。</exception>
        public static string ResolveInsideRoot(string installRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                throw new InvalidOperationException("安装根为空，无法解析更新路径。");
            }

            if (!ManifestFileEntry.TryNormalizeSafePath(relativePath, out var normalized, out var reason))
            {
                throw new InvalidOperationException($"清单路径不安全（{relativePath}）：{reason}。");
            }

            var root = Path.GetFullPath(installRoot);
            var target = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"清单路径越出安装根（{relativePath}）。");
            }

            return target;
        }
    }
}
