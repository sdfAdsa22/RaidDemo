using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using RaidDemo.Launcher.Update;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 启动器入口。
    /// </summary>
    /// <remarks>
    /// <para><b>两种运行形态，同一套流程：</b></para>
    /// <list type="bullet">
    /// <item>不带参数 → 图形界面（玩家用法）；</item>
    /// <item>带 <c>--check</c> / <c>--update</c> → 命令行（验收脚本与 CI 用法），
    /// 退出码 0 表示成功，1 表示失败。</item>
    /// </list>
    ///
    /// <para><b>为什么命令行形态是必须的：</b>批次 2 的验收要求"首次安装 / 增量更新 / 源切换 / 失败回退"
    /// 四条都能被重复验证。如果只有界面，每次验证都要人点一遍，就无法证明"改完代码后这四条仍然成立"。
    /// 命令行形态让它们变成可脚本化的判据——这也是本项目对"可复现"的一贯要求。</para>
    /// </remarks>
    public static class Program
    {
        /// <summary>入口。</summary>
        /// <param name="args">命令行参数。</param>
        /// <returns>进程退出码。</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            var options = CommandLineOptions.Parse(args);
            if (options.ShowHelp)
            {
                UseUtf8ConsoleOutput();
                PrintHelp();
                return 0;
            }

            if (options.HasHeadlessAction)
            {
                UseUtf8ConsoleOutput();
            }

            return options.HasHeadlessAction ? RunHeadless(options) : RunUserInterface();
        }

        /// <summary>
        /// 把标准输出切到 UTF-8。
        /// </summary>
        /// <remarks>
        /// 只设置 <c>Console.OutputEncoding</c> 在"输出被重定向到管道/文件"时**不生效**
        /// （实测：写出的仍是系统 ANSI 代码页 GBK，验收脚本按 UTF-8 读会得到乱码，
        /// 而乱码会让基于文案的判据莫名其妙地失败）。
        /// 显式替换 <c>Console.Out</c> 才能真正控制编码，这不是洁癖——它直接决定脚本判据可不可信。
        /// </remarks>
        private static void UseUtf8ConsoleOutput()
        {
            var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetError(new System.IO.StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        }

        /// <summary>启动图形界面。</summary>
        private static int RunUserInterface()
        {
            // 高 DPI 显示器上必须显式声明感知模式：不声明时 Windows 会把整个窗口
            // 当位图拉伸（150% 缩放下 1000×620 的客户区被拉成 1500×930），
            // 中文与描边全部发糊——负责人反馈的"启动器不清晰"就是这个原因。
            // 声明之后由 WinForms 按 DPI 缩放控件坐标，绘制仍是逐像素的。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
            return 0;
        }

        /// <summary>命令行形态：检查 / 更新（可选启动游戏）。</summary>
        private static int RunHeadless(CommandLineOptions options)
        {
            var baseDirectory = AppContext.BaseDirectory;
            var config = LauncherConfig.Load(baseDirectory);

            var source = options.Source ?? config.GetSelectedProfile()?.ManifestSource;
            if (string.IsNullOrWhiteSpace(source))
            {
                Console.Error.WriteLine("未指定更新源：请用 --source 传入，或在配置里设置。");
                return 1;
            }

            var installRoot = options.InstallRoot ?? config.GetInstallRootPath(baseDirectory);
            var log = new LauncherLog(Path.Combine(
                UpdateApplier.GetMetadataDirectory(installRoot),
                UpdateApplier.LogFileName));

            var gameExecutable = Path.GetFullPath(Path.Combine(
                installRoot,
                string.IsNullOrEmpty(options.Executable) ? config.GameExecutable : options.Executable));

            Console.WriteLine($"更新源：{source}");
            Console.WriteLine($"安装根：{installRoot}");
            log.Write($"命令行启动：更新源={source} 安装根={installRoot} 模式={(options.CheckOnly ? "检查" : "更新")}");

            if (!options.CheckOnly && UpdateSession.IsGameRunning("RaidDemo") && !options.Force)
            {
                Console.Error.WriteLine("检测到 RaidDemo 正在运行，文件会被占用。请先退出游戏，或加 --force 跳过检查。");
                return 1;
            }

            var session = new UpdateSession(
                installRoot,
                source,
                progress => PrintProgress(progress),
                log.Write);

            var result = session.Run(applyChanges: !options.CheckOnly);
            Console.WriteLine();
            Console.WriteLine(result.Summary);

            if (!result.Success)
            {
                log.Write("失败：" + result.FailureReason);
                return 1;
            }

            if (options.LaunchAfterUpdate)
            {
                if (!File.Exists(gameExecutable))
                {
                    Console.Error.WriteLine($"找不到游戏可执行文件：{gameExecutable}");
                    return 1;
                }

                var arguments = BuildGameArguments(config, source);
                Console.WriteLine($"启动游戏：{gameExecutable} {arguments}");
                log.Write($"启动游戏：{gameExecutable} {arguments}");
                UpdateSession.LaunchGame(gameExecutable, arguments);
            }

            return 0;
        }

        /// <summary>
        /// 组装传给游戏的参数。
        /// </summary>
        /// <remarks>
        /// 更新源与默认服务器地址来自启动器配置（ADR-007）：玩家在启动器里选过源之后，
        /// 游戏内不必再填一次地址；同时这些参数对游戏都是**可选**的——
        /// 直接双击 RaidDemo.exe 仍要能正常单机游玩。
        /// </remarks>
        private static string BuildGameArguments(LauncherConfig config, string source)
        {
            var arguments = new List<string>();
            arguments.Add($"-updatesource \"{source}\"");

            var profile = config.GetSelectedProfile();
            if (profile != null && !string.IsNullOrWhiteSpace(profile.GameServer))
            {
                arguments.Add($"-connect \"{profile.GameServer}\"");
            }

            if (!string.IsNullOrWhiteSpace(config.ExtraGameArguments))
            {
                arguments.Add(config.ExtraGameArguments);
            }

            return string.Join(" ", arguments);
        }

        /// <summary>把进度打到控制台（同一行覆盖刷新，结束阶段换行）。</summary>
        private static void PrintProgress(UpdateProgress progress)
        {
            if (progress.Phase == UpdatePhase.Completed || progress.Phase == UpdatePhase.Failed)
            {
                Console.WriteLine();
                return;
            }

            if (progress.FilesTotal > 0 && progress.Phase != UpdatePhase.Applying)
            {
                var percent = progress.TotalBytes > 0
                    ? (int)(progress.Ratio * 100)
                    : (progress.FilesTotal == 0 ? 100 : progress.FilesDone * 100 / progress.FilesTotal);

                Console.Write(
                    $"\r[{progress.Phase}] {percent,3}%  {progress.FilesDone}/{progress.FilesTotal} 文件  " +
                    $"{progress.Message,-60}");
            }
            else
            {
                Console.Write($"\r[{progress.Phase}] {progress.Message,-80}");
            }
        }

        /// <summary>打印帮助。</summary>
        private static void PrintHelp()
        {
            Console.WriteLine("RaidDemo 启动器");
            Console.WriteLine();
            Console.WriteLine("用法（不带参数 = 图形界面）：");
            Console.WriteLine("  RaidDemo.Launcher.exe --check  [--source <地址>] [--root <安装根>]");
            Console.WriteLine("  RaidDemo.Launcher.exe --update [--source <地址>] [--root <安装根>] [--launch] [--force]");
            Console.WriteLine();
            Console.WriteLine("参数：");
            Console.WriteLine("  --source <地址>  更新源（http(s)://… 或本地目录）；缺省用配置里选中的档案");
            Console.WriteLine("  --root <路径>    安装根目录；缺省用配置里的 InstallRoot");
            Console.WriteLine("  --check          只检查并打印计划，不下载");
            Console.WriteLine("  --update         下载并应用更新");
            Console.WriteLine("  --launch         更新成功后启动游戏");
            Console.WriteLine("  --force          跳过“游戏正在运行”的检查（仅供脚本使用）");
            Console.WriteLine("  --help           显示本帮助");
        }
    }

    /// <summary>命令行参数。</summary>
    internal sealed class CommandLineOptions
    {
        /// <summary>更新源地址。</summary>
        public string Source { get; private set; }

        /// <summary>安装根目录。</summary>
        public string InstallRoot { get; private set; }

        /// <summary>游戏可执行文件名（相对安装根）。</summary>
        public string Executable { get; private set; }

        /// <summary>只检查不更新。</summary>
        public bool CheckOnly { get; private set; }

        /// <summary>更新后启动游戏。</summary>
        public bool LaunchAfterUpdate { get; private set; }

        /// <summary>跳过"游戏正在运行"检查。</summary>
        public bool Force { get; private set; }

        /// <summary>显示帮助。</summary>
        public bool ShowHelp { get; private set; }

        /// <summary>是否存在需要无界面执行的动作。</summary>
        public bool HasHeadlessAction
        {
            get { return CheckOnly || UpdateRequested; }
        }

        /// <summary>是否明确要求更新。</summary>
        private bool UpdateRequested { get; set; }

        /// <summary>解析参数。</summary>
        /// <param name="args">原始参数。</param>
        /// <returns>解析结果。</returns>
        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                switch (argument.ToLowerInvariant())
                {
                    case "--source":
                        options.Source = NextValue(args, ref index);
                        break;
                    case "--root":
                        options.InstallRoot = NextValue(args, ref index);
                        break;
                    case "--exe":
                        options.Executable = NextValue(args, ref index);
                        break;
                    case "--check":
                        options.CheckOnly = true;
                        break;
                    case "--update":
                        options.UpdateRequested = true;
                        break;
                    case "--launch":
                        options.LaunchAfterUpdate = true;
                        break;
                    case "--force":
                        options.Force = true;
                        break;
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;
                }
            }

            return options;
        }

        /// <summary>取下一个参数值。</summary>
        private static string NextValue(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                return null;
            }

            index++;
            return args[index];
        }
    }
}
