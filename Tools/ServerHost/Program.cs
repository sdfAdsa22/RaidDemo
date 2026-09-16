using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 服务器面板入口。
    /// </summary>
    /// <remarks>
    /// <para><b>两种运行形态，同一套代码：</b></para>
    /// <list type="bullet">
    /// <item>不带参数 → 图形界面（玩家用法：双击就能开服）；</item>
    /// <item>带参数 → 命令行（打包脚本与验收脚本用法），退出码 0 表示成功、1 表示失败。</item>
    /// </list>
    ///
    /// <para><b>为什么命令行形态是必须的：</b>打包时要生成默认配置（<c>--write-default-config</c>），
    /// 验收时要证明"配置会被服务器接受、非法配置会被拒绝"（<c>--check</c>），
    /// 界面本身也要能被截图核对（<c>--render</c>）。这三件事都靠点鼠标做不出来，
    /// 而做不出来就意味着改完代码之后没人能证明它们仍然成立。</para>
    /// </remarks>
    public static class Program
    {
        /// <summary>渲染前让界面结算的轮数。</summary>
        private const int RenderSettleIterations = 6;

        /// <summary>每轮之间的等待（毫秒）。</summary>
        private const int RenderSettleDelayMilliseconds = 60;

        /// <summary>入口。</summary>
        /// <param name="args">命令行参数。</param>
        /// <returns>进程退出码。</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            var command = CommandLine.Parse(args);
            ConsoleBridge.Open();

            if (command.Error != null)
            {
                Console.Error.WriteLine($"参数有误：{command.Error}");
                WriteHelp();
                return 1;
            }

            switch (command.Mode)
            {
                case HostMode.Help:
                    WriteHelp();
                    return 0;

                case HostMode.WriteDefaultConfig:
                    return WriteDefaultConfig(command.Directory);

                case HostMode.Check:
                    return Check(command.Directory);

                case HostMode.Render:
                    return Render(command.RenderPath, command.Directory);

                default:
                    // 图形界面：双击时把系统新建的控制台窗口藏起来（脚本调用时共用父窗口，不受影响）。
                    ConsoleBridge.HideOwnConsoleWindow();
                    return RunUserInterface(command.Directory);
            }
        }

        /// <summary>在目标目录写出默认配置。</summary>
        private static int WriteDefaultConfig(string directory)
        {
            if (!ServerConfigStore.TryWriteDefault(directory, out var path, out var error))
            {
                Console.Error.WriteLine($"写出默认配置失败：{error}");
                return 1;
            }

            Console.WriteLine($"已写出默认配置：{path}");
            return 0;
        }

        /// <summary>校验配置并打印生效值。</summary>
        private static int Check(string directory)
        {
            var layout = ServerLayout.FromDirectory(directory);

            if (!ServerConfigStore.TryLoad(layout.ConfigPath, out var document, out var loadError))
            {
                Console.Error.WriteLine($"配置有误：{loadError}");
                return 1;
            }

            if (!ServerConfigValidation.TryValidate(document, out var errors))
            {
                foreach (var item in errors)
                {
                    Console.Error.WriteLine($"配置有误：{item}");
                }

                return 1;
            }

            Console.WriteLine(ServerConfigStore.Describe(layout, document));
            return 0;
        }

        /// <summary>
        /// 把界面渲染成 PNG。
        /// </summary>
        /// <remarks>
        /// 界面是"面板能不能用"的一部分，而自动化测试跑不进鼠标。离屏渲染解决其中一半问题：
        /// 布局是否错位、控件是否被裁掉、字段有没有漏掉，都能在一张图里看出来
        /// （启动器当初也是用同一种办法核对新增的安装目录行）。
        /// </remarks>
        private static int Render(string path, string directory)
        {
            try
            {
                using var form = new ServerHostForm(directory, interactive: false);
                form.Show();

                // 让布局、控件与文字完成首帧绘制再截图。
                // 首次运行时自包含单文件要把原生库解出来，启动明显慢于后续，只跑一轮消息循环
                // 可能截到"控件还没画出来"的图——那种图看着像缺陷，其实只是抓得太早。
                for (var i = 0; i < RenderSettleIterations; i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(RenderSettleDelayMilliseconds);
                }

                form.Refresh();
                Application.DoEvents();

                using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

                var target = Path.GetFullPath(path);
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                bitmap.Save(target, ImageFormat.Png);
                form.Close();

                Console.WriteLine($"已渲染界面：{target}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"渲染界面失败：{exception.Message}");
                return 1;
            }
        }

        /// <summary>启动图形界面。</summary>
        private static int RunUserInterface(string directory)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var form = new ServerHostForm(directory, interactive: true);
            Application.Run(form);
            return 0;
        }

        /// <summary>打印帮助。</summary>
        private static void WriteHelp()
        {
            Console.WriteLine("RaidDemo 服务器面板");
            Console.WriteLine();
            Console.WriteLine("用法（不带参数 = 图形界面，管理面板旁边的这台服务器）：");
            Console.WriteLine("  RaidDemo.ServerHost.exe [--dir <服务器目录>]");
            Console.WriteLine("  RaidDemo.ServerHost.exe --write-default-config <目录>");
            Console.WriteLine("  RaidDemo.ServerHost.exe --check <目录>");
            Console.WriteLine("  RaidDemo.ServerHost.exe --render <输出.png> [--dir <服务器目录>]");
            Console.WriteLine();
            Console.WriteLine("参数：");
            Console.WriteLine("  --dir <目录>              服务器目录（含 RaidDemoServer.exe）；缺省 = 面板所在目录");
            Console.WriteLine("  --write-default-config    写出默认 server.config.json（目录不存在则创建）");
            Console.WriteLine("  --check                   校验 server.config.json 并打印生效值");
            Console.WriteLine("  --render <png>            把面板界面渲染成 PNG（界面自检用）");
            Console.WriteLine("  --help                    显示本帮助");
            Console.WriteLine();
            Console.WriteLine("配置文件：server.config.json（与服务器程序同目录）；优先级：命令行 > 配置文件 > 内置默认。");
        }
    }
}
