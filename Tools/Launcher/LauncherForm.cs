using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using RaidDemo.Kernel.Updates;
using RaidDemo.Launcher.Update;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 启动器主界面。
    /// </summary>
    /// <remarks>
    /// <para><b>界面只做四件事：</b>选更新源、看版本、检查更新、更新并启动。
    /// 它不承担任何业务判断——所有流程都在 <see cref="UpdateSession"/> 里，
    /// 界面只是把进度与结果显示出来。这样"界面上的行为"与"脚本里的行为"天然一致。</para>
    ///
    /// <para><b>所有耗时动作都在后台线程：</b>下载 158 MB 时界面必须保持可响应（可关闭、可看日志），
    /// 否则玩家会以为程序卡死并强杀进程——那正好会撞上"更新到一半"的最坏时机。</para>
    /// </remarks>
    public sealed class LauncherForm : Form
    {
        /// <summary>是否已有任务在跑（防止重复点击）。</summary>
        private bool m_Busy;

        /// <summary>配置。</summary>
        private LauncherConfig m_Config;

        /// <summary>安装根目录。</summary>
        private string m_InstallRoot;

        /// <summary>日志器。</summary>
        private LauncherLog m_Log;

        private readonly ComboBox m_SourceCombo = new ComboBox();
        private readonly TextBox m_SourceText = new TextBox();
        private readonly Label m_ServerLabel = new Label();
        private readonly Label m_VersionLabel = new Label();
        private readonly Button m_CheckButton = new Button();
        private readonly Button m_UpdateButton = new Button();
        private readonly CheckBox m_LaunchCheck = new CheckBox();
        private readonly ProgressBar m_Progress = new ProgressBar();
        private readonly Label m_StatusLabel = new Label();
        private readonly TextBox m_LogBox = new TextBox();

        /// <summary>"安装目录"一行（控件与校验逻辑都在 <see cref="InstallRootRow"/> 里）。</summary>
        private InstallRootRow m_InstallRow;

        /// <summary>创建主窗口。</summary>
        public LauncherForm()
        {
            BuildLayout();
            Load += (_, __) => LoadConfiguration();
        }

        /// <summary>构建界面（纯代码布局，避免额外的设计器文件）。</summary>
        private void BuildLayout()
        {
            Text = "RaidDemo 启动器";
            StartPosition = FormStartPosition.CenterScreen;
            // 最小宽度 700：保证"安装目录"那一行的浏览按钮（右边缘在 x=680）不会被拉出可视区。
            MinimumSize = new Size(700, 540);
            Size = new Size(720, 600);
            Font = new Font("Microsoft YaHei UI", 9f);

            var sourceLabel = new Label { Text = "更新源", Location = new Point(16, 20), AutoSize = true };
            m_SourceCombo.Location = new Point(90, 16);
            m_SourceCombo.Width = 200;
            m_SourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            m_SourceCombo.SelectedIndexChanged += (_, __) => OnProfileChanged();

            m_SourceText.Location = new Point(300, 16);
            m_SourceText.Width = 380;
            m_SourceText.Leave += (_, __) => PersistEditableSource();

            var serverTitle = new Label { Text = "游戏服务器", Location = new Point(16, 88), AutoSize = true };
            m_ServerLabel.Location = new Point(90, 88);
            m_ServerLabel.AutoSize = true;
            m_ServerLabel.ForeColor = Color.DimGray;

            var versionTitle = new Label { Text = "本地版本", Location = new Point(16, 116), AutoSize = true };
            m_VersionLabel.Location = new Point(90, 116);
            m_VersionLabel.AutoSize = true;

            m_CheckButton.Text = "检查更新";
            m_CheckButton.Location = new Point(16, 150);
            m_CheckButton.Size = new Size(140, 32);
            m_CheckButton.Click += async (_, __) => await RunAsync(applyChanges: false);

            m_UpdateButton.Text = "更新并启动";
            m_UpdateButton.Location = new Point(170, 150);
            m_UpdateButton.Size = new Size(140, 32);
            m_UpdateButton.Click += async (_, __) => await RunAsync(applyChanges: true);

            m_LaunchCheck.Text = "更新后启动游戏";
            m_LaunchCheck.Location = new Point(330, 156);
            m_LaunchCheck.AutoSize = true;
            m_LaunchCheck.Checked = true;

            m_Progress.Location = new Point(16, 196);
            m_Progress.Size = new Size(664, 18);
            m_Progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            m_StatusLabel.Location = new Point(16, 222);
            m_StatusLabel.Size = new Size(664, 22);
            m_StatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            m_StatusLabel.Text = "就绪";

            m_LogBox.Location = new Point(16, 250);
            m_LogBox.Multiline = true;
            m_LogBox.ReadOnly = true;
            m_LogBox.ScrollBars = ScrollBars.Vertical;
            m_LogBox.Size = new Size(664, 300);
            m_LogBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            m_LogBox.BackColor = Color.FromArgb(28, 30, 34);
            m_LogBox.ForeColor = Color.Gainsboro;

            Controls.AddRange(new Control[]
            {
                sourceLabel, m_SourceCombo, m_SourceText,
                serverTitle, m_ServerLabel,
                versionTitle, m_VersionLabel,
                m_CheckButton, m_UpdateButton, m_LaunchCheck,
                m_Progress, m_StatusLabel, m_LogBox,
            });
        }

        /// <summary>加载配置并刷新界面。</summary>
        private void LoadConfiguration()
        {
            var baseDirectory = AppContext.BaseDirectory;
            m_Config = LauncherConfig.Load(baseDirectory);
            m_InstallRoot = m_Config.GetInstallRootPath(baseDirectory);
            m_Log = new LauncherLog(Path.Combine(
                UpdateApplier.GetMetadataDirectory(m_InstallRoot),
                UpdateApplier.LogFileName));

            // "安装目录"一行需要配置对象，因此在这里（而不是布局阶段）创建并挂上窗体。
            m_InstallRow = new InstallRootRow(m_Config, this, AppendLog, new Point(16, 50));
            m_InstallRow.AddTo(this);
            m_InstallRow.ShowCurrent(m_InstallRoot);
            m_InstallRow.Changed += OnInstallRootChanged;

            m_SourceCombo.Items.Clear();
            foreach (var profile in m_Config.Sources)
            {
                m_SourceCombo.Items.Add(profile.Name);
            }

            var index = m_SourceCombo.Items.IndexOf(m_Config.SelectedSource);
            m_SourceCombo.SelectedIndex = index >= 0 ? index : 0;

            AppendLog($"配置文件：{m_Config.LoadedFromPath}");
            AppendLog($"安装根：{m_InstallRoot}");
            RefreshLocalVersion();
        }

        /// <summary>切换更新源档案。</summary>
        private void OnProfileChanged()
        {
            var profile = GetSelectedProfile();
            if (profile == null)
            {
                return;
            }

            m_SourceText.Text = profile.ManifestSource;
            m_SourceText.ReadOnly = !profile.Editable;
            m_ServerLabel.Text = string.IsNullOrWhiteSpace(profile.GameServer) ? "(未设置)" : profile.GameServer;

            m_Config.SelectedSource = profile.Name;
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
            try
            {
                m_Config.Save(AppContext.BaseDirectory);
                AppendLog("已保存自定义更新源：" + profile.ManifestSource);
            }
            catch (Exception exception)
            {
                AppendLog("保存配置失败：" + exception.Message);
            }
        }

        /// <summary>
        /// 安装目录切换成功后的收尾（由 <see cref="InstallRootRow.Changed"/> 触发）。
        /// </summary>
        /// <param name="installRoot">新的安装根绝对路径。</param>
        /// <remarks>
        /// <para><b>为什么日志器要重建：</b>每个安装各自一份 <c>.raiddemo/launcher.log</c>，
        /// 日志跟着安装目录走，删除某份安装时不会在别处留下"孤儿日志"，
        /// 排查时也总能找到"当时那份安装"的记录。</para>
        ///
        /// <para><b>校验与落盘已在 <see cref="InstallRootRow"/> 里完成：</b>
        /// 这里只做"界面与运行时状态"的收尾——换日志器、刷新版本显示。
        /// 分工清楚的好处是：路径规则只有一处实现，不会出现"两个地方各校验一遍、规则还不一样"。</para>
        /// </remarks>
        private void OnInstallRootChanged(string installRoot)
        {
            m_InstallRoot = installRoot;
            m_Log = new LauncherLog(Path.Combine(
                UpdateApplier.GetMetadataDirectory(m_InstallRoot),
                UpdateApplier.LogFileName));

            AppendLog("当前安装目录：" + m_InstallRoot);
            RefreshLocalVersion();
        }

        /// <summary>刷新"本地版本"显示。</summary>
        private void RefreshLocalVersion()
        {
            var local = UpdateApplier.LoadLocalManifest(m_InstallRoot);
            if (local == null)
            {
                m_VersionLabel.Text = "未安装（首次运行将全量下载）";
                return;
            }

            var text = "本体 " + (string.IsNullOrEmpty(local.body?.version) ? "未知" : local.body.version);
            if (local.HasContentLayer)
            {
                text += " / 资源 " + local.content.version;
            }

            if (local.HasCodeLayer)
            {
                text += " / 代码 " + local.code.version;
            }

            m_VersionLabel.Text = text;
        }

        /// <summary>取当前选中的档案对象。</summary>
        private UpdateSourceProfile GetSelectedProfile()
        {
            if (m_SourceCombo.SelectedIndex < 0 || m_SourceCombo.SelectedIndex >= m_Config.Sources.Count)
            {
                return null;
            }

            return m_Config.Sources[m_SourceCombo.SelectedIndex];
        }

        /// <summary>执行一次检查或更新。</summary>
        /// <param name="applyChanges">是否真正下载并应用。</param>
        private async Task RunAsync(bool applyChanges)
        {
            if (m_Busy)
            {
                return;
            }

            var profile = GetSelectedProfile();
            var source = profile?.Editable == true ? m_SourceText.Text.Trim() : profile?.ManifestSource;
            if (string.IsNullOrWhiteSpace(source))
            {
                MessageBox.Show(this, "请先选择或填写更新源地址。", "更新源为空", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            m_StatusLabel.Text = result.Summary;
            RefreshLocalVersion();
            SetBusy(false);

            if (!result.Success)
            {
                MessageBox.Show(this, result.Summary, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (applyChanges && m_LaunchCheck.Checked)
            {
                LaunchGame(source);
            }
        }

        /// <summary>启动游戏。</summary>
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
                arguments += $" -connect \"{profile.GameServer}\"";
            }

            if (!string.IsNullOrWhiteSpace(m_Config.ExtraGameArguments))
            {
                arguments += " " + m_Config.ExtraGameArguments;
            }

            AppendLog($"启动游戏：{executable} {arguments}");
            UpdateSession.LaunchGame(executable, arguments);
        }

        /// <summary>进度回调（已在 UI 线程）。</summary>
        private void OnProgress(UpdateProgress progress)
        {
            m_Progress.Style = ProgressBarStyle.Continuous;
            m_Progress.Value = (int)Math.Round(progress.Ratio * 100);
            m_StatusLabel.Text = progress.Message;
        }

        /// <summary>切换忙碌状态。</summary>
        private void SetBusy(bool busy)
        {
            m_Busy = busy;
            m_CheckButton.Enabled = !busy;
            m_UpdateButton.Enabled = !busy;
            m_SourceCombo.Enabled = !busy;
            m_InstallRow.SetBusy(busy);

            if (!busy)
            {
                m_Progress.Value = 0;
            }
        }

        /// <summary>追加一行日志（界面 + 文件）。</summary>
        private void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            m_Log?.Write(message);

            if (m_LogBox.InvokeRequired)
            {
                m_LogBox.BeginInvoke(new Action(() => AppendLog(message)));
                return;
            }

            m_LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
    }
}
