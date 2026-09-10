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
    /// 路径规则扫描器：检查工程内是否存在本机绝对路径。
    /// </summary>
    /// <remarks>
    /// <para>把扫描逻辑抽成独立类型，是为了让它可以被单独验证。
    /// 一个从不失败的检查等于没有检查，因此测试中专门有一个用例写入带绝对路径的临时源码，
    /// 断言扫描器确实能抓到它。</para>
    ///
    /// <para>本类型仅存在于测试程序集，不参与游戏构建。</para>
    /// </remarks>
    public static class PathRulesVerifier
    {
        /// <summary>相对工程根目录的扫描起点。</summary>
        public const string ScanRoot = "Assets";

        /// <summary>源码文件的扫描模式。</summary>
        public const string SourceSearchPattern = "*.cs";

        /// <summary>
        /// 需要一并扫描的配置文件模式。
        /// </summary>
        /// <remarks>
        /// 绝对路径不只出现在源码里：Unity 的资产文件与项目配置同样可能记录本机路径，
        /// 而这类文件一旦被提交，克隆者往往很难看出问题出在哪。
        /// 因此扫描范围必须覆盖它们，只查 .cs 是不完整的。
        /// </remarks>
        public static readonly string[] ConfigurationSearchPatterns =
        {
            "*.asmdef",
            "*.json",
            "*.asset"
        };

        /// <summary>单元测试目录的路径片段，用于豁免行数限制。</summary>
        public const string TestPathFragment = "/Tests/";

        /// <summary>
        /// 匹配 Windows 盘符绝对路径，例如 C:\ 或 D:/。
        /// </summary>
        /// <remarks>
        /// 前置的负向后顾断言是必需的：没有它，网址中的 https:// 会被误判为
        /// 盘符 s 加上斜杠。加上断言后，只有冒号前是行首或非字母数字字符时才视为盘符，
        /// 既能抓到真实的盘符路径，也不会把 URL 当成违规。
        /// </remarks>
        private static readonly Regex WindowsDrivePath = new Regex(
            @"(?<![A-Za-z0-9])[A-Za-z]:[\\/]",
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
        /// <param name="lines">按行拆分的文件内容。</param>
        /// <returns>违规列表；无违规则为空列表。</returns>
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
        /// 枚举所有需要扫描的文件（源码与配置文件）。
        /// </summary>
        /// <param name="projectRoot">工程根目录，即 Assets 的父目录。</param>
        /// <param name="excludedFileNames">需要跳过的文件名。</param>
        public static List<string> EnumerateScannableFiles(string projectRoot, params string[] excludedFileNames)
        {
            var result = new List<string>();
            var root = Path.Combine(projectRoot, ScanRoot);
            if (!Directory.Exists(root))
            {
                return result;
            }

            foreach (var file in Directory.EnumerateFiles(root, SourceSearchPattern, SearchOption.AllDirectories))
            {
                AddIfNotExcluded(result, file, excludedFileNames);
            }

            foreach (var pattern in ConfigurationSearchPatterns)
            {
                foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                {
                    AddIfNotExcluded(result, file, excludedFileNames);
                }
            }

            return result;
        }

        /// <summary>统计待扫描文件数量，用于确认扫描确实执行了。</summary>
        public static int CountScannableFiles(string projectRoot, params string[] excludedFileNames)
        {
            return EnumerateScannableFiles(projectRoot, excludedFileNames).Count;
        }

        /// <summary>
        /// 扫描整个 Assets 目录，返回全部违规项。
        /// </summary>
        /// <param name="projectRoot">工程根目录。</param>
        /// <param name="excludedFileNames">需要跳过的文件名。</param>
        public static List<PathViolation> ScanDirectory(string projectRoot, params string[] excludedFileNames)
        {
            var violations = new List<PathViolation>();

            foreach (var file in EnumerateScannableFiles(projectRoot, excludedFileNames))
            {
                var relativePath = ToRelativePath(projectRoot, file);
                violations.AddRange(ScanLines(relativePath, File.ReadAllLines(file)));
            }

            return violations;
        }

        /// <summary>把绝对路径转换为便于阅读的仓库相对路径，用于测试报告。</summary>
        public static string ToRelativePath(string projectRoot, string absolutePath)
        {
            if (absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath
                    .Substring(projectRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return Path.GetFileName(absolutePath);
        }

        /// <summary>判断文件是否位于测试目录中（测试代码豁免行数限制）。</summary>
        public static bool IsTestFile(string relativePath)
        {
            return relativePath.IndexOf(TestPathFragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddIfNotExcluded(List<string> target, string file, string[] excludedFileNames)
        {
            if (IsExcluded(file, excludedFileNames))
            {
                return;
            }

            if (!target.Contains(file))
            {
                target.Add(file);
            }
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
    }
}
