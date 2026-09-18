using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 启动器单实例保护：第二次启动不再开第二个窗口，而是把已有窗口拉到前台。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-01 的 GUI 侧面——双击两次 exe 会出现两个
    /// "RaidDemo 启动器"窗口，两个都能点下载，随后就是并发暂存区互踩。
    /// 互斥体挡住的是"同时更新"，这里挡住的是"同时存在两个界面"。</para>
    ///
    /// <para><b>为什么用窗口激活而不是直接退出：</b>玩家再点一次图标时预期是"把启动器叫到前面"，
    /// 静默退出会让人以为程序没启动。找不到窗口（例如第一个实例正在启动中）时也只返回，
    /// 不抛出异常——单实例保护不该成为新的故障点。</para>
    /// </remarks>
    public sealed class SingleInstanceGuard : IDisposable
    {
        /// <summary>界面实例互斥体名（本地会话命名空间）。</summary>
        private const string MutexName = @"Local\RaidDemo.Launcher.Ui";

        private const int RestoreWindowCommand = 9;

        private readonly Mutex m_Mutex;
        private bool m_Held;

        private SingleInstanceGuard(Mutex mutex)
        {
            m_Mutex = mutex;
        }

        /// <summary>尝试成为唯一界面实例；已有实例时返回 <c>null</c>。</summary>
        public static SingleInstanceGuard TryAcquire()
        {
            var mutex = new Mutex(false, MutexName);
            try
            {
                if (!mutex.WaitOne(TimeSpan.Zero))
                {
                    mutex.Dispose();
                    return null;
                }
            }
            catch (UnauthorizedAccessException)
            {
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceGuard(mutex) { m_Held = true };
        }

        /// <summary>把已有启动器窗口还原并拉到前台。</summary>
        /// <returns>找到并激活返回 <c>true</c>。</returns>
        public static bool ActivateExistingWindow()
        {
            var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id == current.Id)
                {
                    continue;
                }

                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                ShowWindow(handle, RestoreWindowCommand);
                SetForegroundWindow(handle);
                return true;
            }

            return false;
        }

        /// <summary>释放互斥体。</summary>
        public void Dispose()
        {
            if (m_Held)
            {
                try
                {
                    m_Mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 退出路径上的跨线程释放不值得让程序崩溃。
                }

                m_Held = false;
            }

            m_Mutex.Dispose();
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);
    }
}
