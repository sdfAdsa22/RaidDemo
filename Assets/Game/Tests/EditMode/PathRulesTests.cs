using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 工程路径规则测试：保证仓库可以克隆到任意位置、任意盘符、任意用户名下直接运行。
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
        public void Assets_DoNotContainAbsolutePaths()
        {
            var scanned = PathRulesVerifier.CountScannableFiles(ProjectRoot, ExcludedFiles);
            Assert.Greater(scanned, 0, $"未扫描到任何文件，请检查扫描路径 {PathRulesVerifier.ScanRoot} 是否正确。");

            var violations = PathRulesVerifier.ScanDirectory(ProjectRoot, ExcludedFiles);
            if (violations.Count == 0)
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"发现 {violations.Count} 处绝对路径。工程规范要求源码与配置不含任何本机路径，");
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
        /// 验证扫描器本身有效。
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

            // 用字符拼接构造路径，避免本文件自身被扫描器判定为违规。
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

        /// <summary>
        /// URL 不应被误判为盘符路径。
        /// </summary>
        /// <remarks>
        /// 网址中的 https:// 含有一个冒号，若正则不加以区分会被当作盘符 s。
        /// 这个用例锁定该行为，避免后续修改正则时引入大量误报——
        /// 一个满屏误报的检查最终会被人忽略，等同于没有检查。
        /// </remarks>
        /// <summary>
        /// Windows 风格的反斜杠路径也要能被识别为测试文件。
        /// </summary>
        /// <remarks>
        /// <see cref="PathRulesVerifier.ToRelativePath"/> 返回平台原生分隔符，
        /// 而判断依据里写的是 <c>/Tests/</c>。这条用例钉住"先统一分隔符"这个前提，
        /// 否则"测试代码豁免行数上限"会在 Windows 上静默失效（见 IsTestFile 的注释）。
        /// </remarks>
        [Test]
        public void Verifier_RecognizesWindowsStyleTestPaths()
        {
            var separator = (char)92;
            var windowsPath = $"Assets{separator}Game{separator}Tests{separator}EditMode{separator}SomeTests.cs";
            var unixPath = "Assets/Game/Tests/EditMode/SomeTests.cs";
            var productionPath = $"Assets{separator}Game{separator}Bootstrap{separator}SceneBootstrap.cs";

            Assert.IsTrue(PathRulesVerifier.IsTestFile(windowsPath), "反斜杠路径也应被识别为测试文件。");
            Assert.IsTrue(PathRulesVerifier.IsTestFile(unixPath), "斜杠路径应被识别为测试文件。");
            Assert.IsFalse(PathRulesVerifier.IsTestFile(productionPath), "生产代码不应被误判为测试文件。");
        }

        [Test]
        public void Verifier_DoesNotFlagUrls()
        {
            var lines = new[]
            {
                "url: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal",
                "link: http://example.com/a/b",
                "asset: Assets/Game/Content/item.json"
            };

            var violations = PathRulesVerifier.ScanLines("Sample.cs", lines);

            Assert.IsEmpty(violations, "网址协议前缀与工程相对路径都不应被判定为绝对路径。");
        }

        /// <summary>
        /// URL 中若确实内嵌了盘符路径，仍应被识别出来。
        /// </summary>
        /// <remarks>
        /// 这条与上一条是一对：前者防止误报，后者防止漏报。
        /// 例如 file:///c:/temp 里确实含有一个本机盘符路径，这类写法同样不可移植，必须报出来。
        /// </remarks>
        [Test]
        public void Verifier_FlagsDrivePathEmbeddedInUrl()
        {
            var separator = (char)47;
            var lines = new[] { $"var uri = \"file:{separator}{separator}{separator}C:{separator}temp\";" };

            var violations = PathRulesVerifier.ScanLines("Sample.cs", lines);

            Assert.IsNotEmpty(violations, "URL 中内嵌的盘符路径属于不可移植写法，应当被识别。");
        }

        [Test]
        public void SourceFiles_DoNotExceedLineLimit()
        {
            var violations = new StringBuilder();
            var count = 0;

            foreach (var file in PathRulesVerifier.EnumerateScannableFiles(ProjectRoot, ExcludedFiles))
            {
                var relativePath = PathRulesVerifier.ToRelativePath(ProjectRoot, file);

                // 只对源码检查行数；资产与配置文件不受此规则约束。
                if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 测试代码天然较长，且不参与运行时维护成本，因此豁免行数限制。
                if (PathRulesVerifier.IsTestFile(relativePath))
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
