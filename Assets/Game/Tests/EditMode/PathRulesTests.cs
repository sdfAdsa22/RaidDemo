using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 工程路径规则测试：保证仓库可以被克隆到任意位置、任意盘符、任意用户名下直接运行。
    /// </summary>
    /// <remarks>
    /// <para>这个测试要解决的问题：源码或配置里只要出现一处本机绝对路径，
    /// 别人克隆仓库后就会在该处失败，而失败现象往往与路径毫无关系，排查成本极高。
    /// 靠人工检查无法长期保证，因此把它变成一条会自动失败的测试。</para>
    ///
    /// <para>扫描范围是 Assets 而不是整个仓库：文档目录在讲解规范时必须举出错误写法，
    /// 那些例子本身包含盘符，属于预期内容，不应被判为违规。</para>
    /// </remarks>
    [TestFixture]
    public sealed class PathRulesTests
    {
        /// <summary>单个源码文件的行数上限，与工程规范第 5.2 节一致。</summary>
        private const int MaxFileLineCount = 400;

        /// <summary>临时校验文件所在目录名。该目录不会进入生产构建。</summary>
        private const string TempFolderName = "TempPathCheck";

        private static readonly string ProjectRoot = Directory.GetCurrentDirectory();

        /// <summary>需要跳过的文件：本测试与扫描器自身含有用于匹配的路径样例。</summary>
        private static readonly string[] ExcludedFiles =
        {
            nameof(PathRulesTests) + ".cs",
            nameof(PathRulesVerifier) + ".cs"
        };

        [Test]
        public void SourceFiles_DoNotContainAbsolutePaths()
        {
            var scanned = PathRulesVerifier.CountSourceFiles(ProjectRoot, ExcludedFiles);
            Assert.Greater(scanned, 0, $"未扫描到任何源码文件，请检查扫描路径 {PathRulesVerifier.ScanRoot} 是否正确。");

            var violations = PathRulesVerifier.ScanDirectory(ProjectRoot, ExcludedFiles);
            if (violations.Count == 0)
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"发现 {violations.Count} 处绝对路径。工程规范要求源码不含任何本机路径，");
            report.AppendLine("否则他人克隆仓库后将无法直接运行。应改用：");
            report.AppendLine("  工程内资源      → AssetDatabase / Resources / Addressables");
            report.AppendLine("  存档与配置      → Application.persistentDataPath / streamingAssetsPath");
            report.AppendLine("  路径拼接        → Path.Combine");
            report.AppendLine();
            foreach (var violation in violations)
            {
                report.AppendLine("  " + violation);
                report.AppendLine("      " + violation.LineContent.Trim());
            }

            Assert.Fail(report.ToString());
        }

        /// <summary>
        /// 验证扫描器本身是有效的。
        /// </summary>
        /// <remarks>
        /// 一个从不报错的检查等于没有检查。本用例写入一份确定包含绝对路径的临时源码，
        /// 断言扫描器能把它找出来，从而证明上一条测试的通过是有意义的。
        /// </remarks>
        [Test]
        public void Verifier_DetectsDeliberatelyInjectedAbsolutePath()
        {
            var tempDirectory = Path.Combine(ProjectRoot, PathRulesVerifier.ScanRoot, TempFolderName);
            var tempFile = Path.Combine(tempDirectory, "InjectedViolation.cs");

            // 用字符拼接构造，避免本文件自身被扫描器判定为违规。
            var driveLetter = "D";
            var separator = (char)92;
            var injectedLine = $"private const string Bad = @\"{driveLetter}:{separator}SomeFolder{separator}file.json\";";

            try
            {
                Directory.CreateDirectory(tempDirectory);
                File.WriteAllLines(tempFile, new[]
                {
                    "namespace TempCheck",
                    "{",
                    "    internal static class InjectedViolation",
                    "    {",
                    "        " + injectedLine,
                    "    }",
                    "}"
                });

                var violations = PathRulesVerifier.ScanDirectory(ProjectRoot, ExcludedFiles);

                Assert.IsNotEmpty(
                    violations,
                    "扫描器未能发现故意注入的绝对路径，说明该检查已失效，请修复后再继续。");

                var found = false;
                foreach (var violation in violations)
                {
                    if (violation.FilePath.EndsWith("InjectedViolation.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                Assert.IsTrue(found, "扫描器应当报告注入文件，而不是其他文件。");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        [Test]
        public void SourceFiles_DoNotExceedLineLimit()
        {
            var root = Path.Combine(ProjectRoot, PathRulesVerifier.ScanRoot);
            if (!Directory.Exists(root))
            {
                Assert.Fail($"扫描路径不存在：{PathRulesVerifier.ScanRoot}");
            }

            var violations = new StringBuilder();
            var count = 0;

            foreach (var file in Directory.EnumerateFiles(root, PathRulesVerifier.SourceSearchPattern, SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(ProjectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // 测试代码天然较长，且不参与运行时维护成本，因此豁免行数限制。
                if (relativePath.Contains("/Tests/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lineCount = File.ReadAllLines(file).Length;
                if (lineCount > MaxFileLineCount)
                {
                    violations.AppendLine($"  {relativePath} 共 {lineCount} 行，超过上限 {MaxFileLineCount} 行");
                    count++;
                }
            }

            if (count > 0)
            {
                Assert.Fail($"发现 {count} 个文件超过行数上限，请按职责拆分：\n{violations}");
            }
        }
    }
}
