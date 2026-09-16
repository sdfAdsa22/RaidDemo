using System;
using System.Diagnostics;
using System.Threading.Tasks;
using RaidDemo.Kernel.Server;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 启动与停止专用服务器进程。
    /// </summary>
    /// <remarks>
    /// <para><b>参数与 Linux 脚本逐字对齐：</b><c>-server -batchmode -nographics -logFile Logs/server.log
    /// -config server.config.json</c>。两套系统的启动方式必须是同一份，否则 Windows 上能开、
    /// Linux 上开不了这类问题每次都要重新查一遍参数差异。</para>
    ///
    /// <para><b>为什么不把配置项拼进命令行：</b>配置文件才是面板与玩家的输入通道；命令行优先的规则
    /// 保留给既有验收脚本与云主机部署命令（见 LaunchOptions 的注释）。面板把配置转成命令行，
    /// 等于让面板里看到的与服务器实际用的分成两条路径，也就埋下了漂移。</para>
    ///
    /// <para><b>停止优先走状态页：</b>让服务器自己收尾（保存存档、通知客户端）比杀进程可靠得多；
    /// 只有状态页不可用或超时，才退化为结束整棵进程树（Unity 可能带起子进程）。</para>
    /// </remarks>
    internal sealed class ServerProcessLauncher : IDisposable
    {
        /// <summary>日志相对路径（工作目录 = 服务器目录）。</summary>
        public const string LogFileArgument = "Logs/server.log";

        /// <summary>等服务器自行退出的时限（毫秒）。</summary>
        private const int GracefulStopTimeoutMilliseconds = 15000;

        /// <summary>强杀后等进程消失的时限（毫秒）。</summary>
        private const int KillTimeoutMilliseconds = 5000;

        /// <summary>子进程。</summary>
        private Process m_Process;

        /// <summary>服务器是否正在运行（本面板启动的那个）。</summary>
        public bool IsRunning
        {
            get
            {
                try
                {
                    return m_Process != null && !m_Process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        /// <summary>进程号；未运行时为 0。</summary>
        public int ProcessId
        {
            get
            {
                try
                {
                    return IsRunning ? m_Process.Id : 0;
                }
                catch (InvalidOperationException)
                {
                    return 0;
                }
            }
        }

        /// <summary>服务器进程退出（在后台线程触发）。</summary>
        public event EventHandler Exited;

        /// <summary>
        /// 启动服务器。
        /// </summary>
        /// <param name="layout">服务器目录布局。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否启动成功。</returns>
        public bool TryStart(ServerLayout layout, out string error)
        {
            error = null;

            if (IsRunning)
            {
                error = "服务器已经在运行。";
                return false;
            }

            if (!layout.HasExecutable)
            {
                error = $"找不到服务器程序：{layout.ExecutablePath}";
                return false;
            }

            // 先把日志目录建出来：日志目录不存在时，玩家拿到的现象是"日志没有生成"，
            // 而那正是用来判断服务器为什么没起来的依据。
            if (!ServerLayout.EnsureDirectory(layout.LogDirectory, out var directoryError))
            {
                error = directoryError;
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = layout.ExecutablePath,
                WorkingDirectory = layout.Directory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in BuildArguments())
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                m_Process = Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                error = $"启动失败：{exception.Message}";
                return false;
            }

            if (m_Process == null)
            {
                error = "启动失败：系统没有返回进程句柄。";
                return false;
            }

            m_Process.EnableRaisingEvents = true;
            m_Process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// 停止服务器（先请它自己停，再兜底强杀）。
        /// </summary>
        /// <param name="layout">服务器目录布局（取状态页地址用）。</param>
        /// <param name="document">当前配置（状态页端口来自它）。</param>
        /// <returns>给玩家看的一句话结果。</returns>
        public async Task<string> StopAsync(ServerLayout layout, ServerConfigDocument document)
        {
            if (!IsRunning)
            {
                return "服务器没有在运行。";
            }

            var dashboardPort = ServerConfigStore.EffectiveInteger(
                document?.dashboardPort ?? ServerConfigDocument.UnsetInt,
                ServerConfigDocument.DefaultDashboardPort);
            var dashboardUrl = ServerLayout.DashboardUrl(dashboardPort);

            var requested = dashboardUrl != null
                && await DashboardClient.TryStopServerAsync(dashboardUrl).ConfigureAwait(true);
            if (requested && await WaitForExitAsync(GracefulStopTimeoutMilliseconds).ConfigureAwait(true))
            {
                return "服务器已按请求正常停止。";
            }

            var killed = KillTree();
            await WaitForExitAsync(KillTimeoutMilliseconds).ConfigureAwait(true);

            if (requested)
            {
                return "状态页没有在时限内停下来，已结束服务器进程树。";
            }

            return killed ? "已结束服务器进程树（状态页不可用）。" : "服务器进程已经退出。";
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // 刻意不杀进程：面板关掉而服务器继续服务是合法用法（关面板不等于关服务器）。
            // 窗体关闭时会先问玩家要怎么处理，见 ServerHostForm 的关闭流程。
            m_Process?.Dispose();
            m_Process = null;
        }

        /// <summary>传给服务器进程的参数（与 Linux 侧 deploy/server.sh 同一组开关）。</summary>
        private static string[] BuildArguments()
        {
            return new[]
            {
                "-server",
                "-batchmode",
                "-nographics",
                "-logFile",
                LogFileArgument,
                "-config",
                ServerConfigDocument.FileName,
            };
        }

        /// <summary>等进程退出，超时返回 false。</summary>
        private async Task<bool> WaitForExitAsync(int timeoutMilliseconds)
        {
            if (!IsRunning)
            {
                return true;
            }

            var exitTask = m_Process.WaitForExitAsync();
            var completed = await Task.WhenAny(exitTask, Task.Delay(timeoutMilliseconds)).ConfigureAwait(true);
            return completed == exitTask;
        }

        /// <summary>结束整棵进程树。</summary>
        private bool KillTree()
        {
            try
            {
                if (!IsRunning)
                {
                    return false;
                }

                m_Process.Kill(entireProcessTree: true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
