using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>一处路径规则违规。</summary>
    public readonly struct PathViolation
    {
        public PathViolation(string filePath, int lineNumber, string description, string lineContent)
        {
            FilePath = filePath;
            LineNumber = lineNumber;
            Description = description;
            LineContent = lineContent;
        }

        /// <summary>仓库相对路径。</summary>
        public string FilePath { get; }

        /// <summary>行号，从 1 开始。</summary>
        public int LineNumber { get; }

        /// <summary>违规类型描述。</summary>
        public string Description { get; }

        /// <summary>该行的原始内容，便于定位。</summary>
        public string LineContent { get; }

        public override string ToString()
        {
            return $"{FilePath}:{LineNumber} {Description}";
        }
    }

    /// <summary>
    /// 路径规则扫描器。
    /// </summary>
    /// <remarks>
    /// <para>把扫描逻辑抽成独立类型，是为了让它可以被单独验证。
    /// 一个从不失败的检查等于没有检查，因此 <see cref="PathRulesTests"/> 中专门有一个用例
    /// 写入一份带绝对路径的临时源码，断言扫描器确实能抓到它。</para>
    ///
    /// <para>本类型不参与游戏构建，仅存在于测试程序集。</para>
    /// </remarks>
    public static class PathRulesVerifier
    {
        /// <summary>相对工程根目录的扫描起点。</summary>
        public const string ScanRoot = "Assets";

        /// <summary>扫描的源码扩展名。</summary>
        public const string SourceSearchPattern = "*.cs";

        /// <summary>
        /// 匹配 Windows 盘符绝对路径，例如 C:\ 或 D:/。
        /// </summary>
        private static readonly Regex WindowsDrivePath = new Regex(
            @"[A-Za-z]:[\\/]",
            RegexOptions.Compiled);

        /// <summary>
        /// 匹配出现在字符串字面量中的 Unix 绝对路径，例如 /Users/ 或 /home/。
        /// 限定在字面量内可以避免把注释里的斜线误判为路径。
        /// </summary>
        private static readonly Regex UnixHomePath = new Regex(
            @"""[^""\r\n]*/(?:Users|home|var|opt)/",
            RegexOptions.Compiled);

        /// <summary>
        /// 扫描指定的代码片段，返回其中的违规项。
        /// </summary>
        /// <param name="filePath">用于报告的仓库相对路径。</param>
        /// <param name="lines">按行拆分的源码内容。</param>
        /// <returns>违规列表；无违规则为空。</returns>
        public static List<PathViolation> ScanLines(string filePath, string[] lines)
        {
            var violations = new List<PathViolation>();

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineNumber = i + 1;

                if (WindowsDrivePath.IsMatch(line))
                {
                    violations.Add(new PathViolation(filePath, lineNumber, "盘符绝对路径", line));
                }

                if (UnixHomePath.IsMatch(line))
                {
                    violations.Add(new PathViolation(filePath, lineNumber, "Unix 绝对路径", line));
                }
            }

            return violations;
        }

        /// <summary>
        /// 扫描指定目录下的全部源码文件。
        /// </summary>
        /// <param name="projectRoot">工程根目录（Assets 的父目录）。</param>
        /// <param name="excludedFileNames">需要跳过的文件名，例如扫描器自身的测试文件。</param>
        /// <returns>违规列表；无违规则为空。</returns>
        public static List<PathViolation> ScanDirectory(string projectRoot, params string[] excludedFileNames)
        {
            var violations = new List<PathViolation>();
            var root = Path.Combine(projectRoot, ScanRoot);
            if (!Directory.Exists(root))
            {
                return violations;
            }

            foreach (var file in Directory.EnumerateFiles(root, SourceSearchPattern, SearchOption.AllDirectories))
            {
                if (IsExcluded(file, excludedFileNames))
                {
                    continue;
                }

                var relativePath = ToRelativePath(projectRoot, file);
                violations.AddRange(ScanLines(relativePath, File.ReadAllLines(file)));
            }

            return violations;
        }

        /// <summary>统计目录下的源码文件数量，用于确认扫描确实执行了。</summary>
        public static int CountSourceFiles(string projectRoot, params string[] excludedFileNames)
        {
            var root = Path.Combine(projectRoot, ScanRoot);
            if (!Directory.Exists(root))
            {
                return 0;
            }

            var count = 0;
            foreach (var file in Directory.EnumerateFiles(root, SourceSearchPattern, SearchOption.AllDirectories))
            {
                if (!IsExcluded(file, excludedFileNames))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsExcluded(string filePath, string[] excludedFileNames)
        {
            if (excludedFileNames == null || excludedFileNames.Length == 0)
            {
                return false;
            }

            var fileName = Path.GetFileName(filePath);
            foreach (var excluded in excludedFileNames)
            {
                if (string.Equals(fileName, excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ToRelativePath(string projectRoot, string absolutePath)
        {
            if (absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath
                    .Substring(projectRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return Path.GetFileName(absolutePath);
        }
    }
}
