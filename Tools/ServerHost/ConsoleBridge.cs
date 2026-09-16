using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 让 WinExe 在命令行模式下也能把结果写到父进程的控制台。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>本工程是 WinExe——玩家双击时不能弹出一个黑框，
    /// 但打包脚本与验收脚本要靠 stdout / stderr 与退出码判定成败。WinExe 默认不与调用方的
    /// 控制台关联，输出会掉进黑洞：脚本只能看到"退出码 1"，看不到"为什么失败"。</para>
    ///
    /// <para><b>为什么要显式替换 Console.Out：</b>与启动器踩过的坑同源——
    /// 只设 <c>Console.OutputEncoding</c> 在"输出被重定向到管道 / 文件"时不生效，
    /// 写出的仍是系统 ANSI 代码页（GBK），脚本按 UTF-8 读会得到乱码，
    /// 而乱码会让基于文案的判据莫名其妙地失败。显式替换才真正控制编码。</para>
    ///
    /// <para><b>关联失败不是错误：</b>从资源管理器双击、或父进程没有控制台时
    /// <c>AttachConsole</c> 会失败，此时 .NET 的标准输出流指向空设备，写入被安静丢弃——
    /// 界面与退出码照常工作，这正是我们要的行为。</para>
    /// </remarks>
    internal static class ConsoleBridge
    {
        /// <summary>把当前进程关联到父进程的控制台（Windows 的 ATTACH_PARENT_PROCESS）。</summary>
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        /// <summary>取当前进程关联的控制台窗口句柄；没有控制台时为 0。</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        /// <summary>取关联到当前控制台的进程列表（返回值 = 关联进程数）。</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList(uint[] processList, uint count);

        /// <summary>显示 / 隐藏窗口（<c>SW_HIDE</c>）。</summary>
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        /// <summary><c>ShowWindow</c> 的隐藏命令。</summary>
        private const int HideWindow = 0;

        /// <summary>
        /// 关联父进程控制台并把标准输出 / 错误切到 UTF-8。
        /// </summary>
        /// <returns>是否成功关联到控制台。</returns>
        public static bool Open()
        {
            var attached = AttachConsole(AttachParentProcess);

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });

            return attached;
        }

        /// <summary>
        /// 隐藏"本进程自己创建"的控制台窗口（双击启动时才有）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不能无条件隐藏：</b>从脚本（PowerShell / 打包脚本）调用时，
        /// 本进程共用父进程的控制台，<c>GetConsoleWindow()</c> 返回的是**父窗口**——
        /// 无条件隐藏会把用户正在用的终端窗口一起藏掉。用
        /// <c>GetConsoleProcessList</c> 数一下关联进程：只有本进程时说明这个控制台是为我们新建的，
        /// 才可以隐藏。</para>
        ///
        /// <para>隐藏而不是关闭（<c>FreeConsole</c>）：命令行模式下输出仍然要写到这个控制台。</para>
        /// </remarks>
        public static void HideOwnConsoleWindow()
        {
            var window = GetConsoleWindow();
            if (window == IntPtr.Zero)
            {
                return;
            }

            var processes = new uint[2];
            var count = GetConsoleProcessList(processes, (uint)processes.Length);
            if (count > 1)
            {
                return;
            }

            ShowWindow(window, HideWindow);
        }
    }
}
