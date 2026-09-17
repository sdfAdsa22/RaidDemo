using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using RaidDemo.Kernel.Updates;
using RaidDemo.Launcher.Update;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 主界面的动作：检查 / 下载 / 开始游戏、启动进程、进度回调、日志窗口、设置落盘。
    /// </summary>
    /// <remarks>
    /// <para><b>三个按钮共用一条流程：</b>差别只在"是否下载"与"是否启动"两个开关上。
    /// 分开写三套流程的话，早晚会出现"某个入口少做了一步校验"这类只在某条路径上暴露的缺陷。</para>
    /// </remarks>
    public sealed partial class LauncherForm
    {
        /// <summary>执行一次检查 / 下载 / 更新并启动。</summary>
        /// <param name="applyChanges">是否真正下载并应用（false = 只比对）。</param>
        /// <param name="launchAfterUpdate">成功后是否启动游戏。</param>
        private async Task RunAsync(bool applyChanges, bool launchAfterUpdate)
        {
            if (m_Busy)
            {
                return;
            }

            var profile = GetSelectedProfile();
            var source = profile?.Editable == true ? m_SourceText.Text.Trim() : profile?.ManifestSource;
            if (string.IsNullOrWhiteSpace(source))
            {
                MessageBox.Show(this, "请先在设置里选择或填写更新源地址。", "更新源为空", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (applyChanges && UpdateSession.IsGameRunning("RaidDemo"))
            {
                MessageBox.Show(
                    this,
                    "检测到 RaidDemo 正在运行。Windows 下运行中的文件无法被覆盖，请先退出游戏再更新。",
                    "游戏正在运行",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            m_HeadlineLabel.Text = applyChanges ? "正在更新…" : "正在检查更新…";
            m_StatusLabel.Text = "准备中…";
            m_SpeedLabel.Text = string.Empty;
            AppendLog($"=== {(applyChanges ? "更新" : "检查")}开始：{source} ===");

            // 声明为接口类型：Progress<T> 对 Report 是显式实现，用具体类型调用不到。
            IProgress<UpdateProgress> progress = new Progress<UpdateProgress>(OnProgress);
            var session = new UpdateSession(
                m_InstallRoot,
                source,
                update => progress.Report(update),
                AppendLog);

            var result = await Task.Run(() => session.Run(applyChanges));

            AppendLog(result.Summary);
            SetBusy(false);
            RefreshLocalVersion();
            m_StatusLabel.Text = result.Summary;

            if (!result.Success)
            {
                m_HeadlineLabel.Text = "更新失败";
                MessageBox.Show(this, result.Summary, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (applyChanges && launchAfterUpdate)
            {
                LaunchGame(source);
            }
        }

        /// <summary>进度回调（已在 UI 线程）。</summary>
        /// <param name="progress">进度。</param>
        private void OnProgress(UpdateProgress progress)
        {
            m_ProgressBar.Ratio = progress.Ratio;
            m_StatusLabel.Text = progress.Message;
            m_SpeedLabel.Text = DescribeVolume(progress);
        }

        /// <summary>
        /// 把进度整理成"文件数 + 体积"的说明文字。
        /// </summary>
        /// <remarks>
        /// 刻意不显示"MB/s"：更新会话没有提供速度采样，编一个数字出来只会在出问题时误导排查
        /// （"明明显示 40 MB/s 却卡了十分钟"）。要速度可以看文件数与总体积的推进。
        /// </remarks>
        private static string DescribeVolume(UpdateProgress progress)
        {
            if (progress.FilesTotal <= 0)
            {
                return string.Empty;
            }

            var text = $"{progress.FilesDone} / {progress.FilesTotal} 个文件";
            if (progress.TotalBytes <= 0)
            {
                return text;
            }

            const double megabyte = 1024d * 1024d;
            var done = progress.BytesDone / megabyte;
            var total = progress.TotalBytes / megabyte;
            return $"{text} · {done:F1} / {total:F1} MB";
        }

        /// <summary>启动游戏。</summary>
        /// <param name="source">当前更新源（透传给游戏内的资源热更）。</param>
        private void LaunchGame(string source)
        {
            var baseDirectory = AppContext.BaseDirectory;
            var executable = m_Config.GetGameExecutablePath(baseDirectory);
            if (!File.Exists(executable))
            {
                MessageBox.Show(this, "找不到游戏可执行文件：" + executable, "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var arguments = $"-updatesource \"{source}\"";
            var profile = GetSelectedProfile();
            if (profile != null && !string.IsNullOrWhiteSpace(profile.GameServer))
            {
                // 只预填服务器地址、不自动连接：游戏仍然先显示主菜单，玩家点「联机」时
                // 地址已经填好。以前传 -connect 会自动登录并跳过主菜单（负责人反馈的
                // "启动器启动后进的不是主菜单"），改成 -serverhost 之后启动路径与直接双击一致。
                arguments += $" -serverhost \"{profile.GameServer}\"";
                if (profile.HideAddress)
                {
                    // 隐藏源（云主机）：游戏侧把预填地址显示成"已隐藏"，连接时仍用真实地址。
                    arguments += " -hideserver";
                }
            }

            if (!string.IsNullOrWhiteSpace(m_Config.ExtraGameArguments))
            {
                arguments += " " + m_Config.ExtraGameArguments;
            }

            AppendLog($"启动游戏：{executable} {arguments}");
            m_StatusLabel.Text = "已启动游戏";
            UpdateSession.LaunchGame(executable, arguments);
        }

        /// <summary>自定义源的地址修改后保存配置。</summary>
        private void PersistEditableSource()
        {
            var profile = GetSelectedProfile();
            if (profile == null || !profile.Editable)
            {
                return;
            }

            profile.ManifestSource = m_SourceText.Text.Trim();
            if (TrySaveSettings(out var message))
            {
                AppendLog("已保存自定义更新源：" + profile.ManifestSource);
                return;
            }

            m_StatusLabel.Text = message;
        }

        /// <summary>
        /// 安装目录切换成功后的收尾（由 <see cref="InstallRootRow.Changed"/> 触发）。
        /// </summary>
        /// <param name="installRoot">新的安装根绝对路径。</param>
        /// <remarks>
        /// <b>为什么日志器要重建：</b>每个安装各自一份 <c>.raiddemo/launcher.log</c>，
        /// 日志跟着安装目录走，删除某份安装时不会在别处留下"孤儿日志"。
        /// </remarks>
        private void OnInstallRootChanged(string installRoot)
        {
            m_InstallRoot = installRoot;
            m_Log = new LauncherLog(Path.Combine(
                UpdateApplier.GetMetadataDirectory(m_InstallRoot),
                UpdateApplier.LogFileName));

            AppendLog("当前安装目录：" + m_InstallRoot);
            RefreshLocalVersion();

            // 日志窗口若开着，让它指向新目录的日志。
            m_LogWindow?.Close();
        }

        /// <summary>打开日志窗口（主界面不再显示日志）。</summary>
        private void OpenLogWindow()
        {
            if (m_LogWindow != null && !m_LogWindow.IsDisposed)
            {
                m_LogWindow.Activate();
                return;
            }

            var logPath = Path.Combine(
                UpdateApplier.GetMetadataDirectory(m_InstallRoot),
                UpdateApplier.LogFileName);
            m_LogWindow = new LogWindowForm(logPath);
            m_LogWindow.FormClosed += (_, __) => m_LogWindow = null;
            m_LogWindow.Show(this);
        }
    }
}
