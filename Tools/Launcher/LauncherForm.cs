using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RaidDemo.Kernel.Updates;
using RaidDemo.Launcher.Theme;
using RaidDemo.Launcher.Update;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 启动器主界面：一整张卡通主视觉 + 左下的主按钮区，设置收进右侧抽屉，日志收进独立窗口。
    /// </summary>
    /// <remarks>
    /// <para><b>界面仍然只做四件事：</b>选更新源、看版本、下载 / 检查更新、启动游戏。
    /// 它不承担任何业务判断——所有流程都在 <see cref="UpdateSession"/> 里，
    /// 界面只是把进度与结果显示出来。这样"界面上的行为"与"脚本里的行为"天然一致。</para>
    ///
    /// <para><b>所有耗时动作都在后台线程：</b>下载 158 MB 时界面必须保持可响应（可关闭、可看日志），
    /// 否则玩家会以为程序卡死并强杀进程——那正好会撞上"更新到一半"的最坏时机。</para>
    ///
    /// <para><b>为什么拆成多个分部文件：</b>布局、抽屉、动作三件事各自独立，
    /// 全塞进一个文件会顶过工程规范的单文件 400 行上限（5.2 节），
    /// 也会让"改一处按钮位置"要在几百行里找目标。</para>
    /// </remarks>
    public sealed partial class LauncherForm : Form
    {
        /// <summary>窗口宽度（像素）。</summary>
        private const int WindowWidth = 1000;

        /// <summary>窗口高度（像素）。</summary>
        private const int WindowHeight = 620;

        /// <summary>窗口圆角半径（像素）。</summary>
        private const int WindowCornerRadius = 14;

        /// <summary>是否已有任务在跑（防止重复点击）。</summary>
        private bool m_Busy;

        /// <summary>配置。</summary>
        private LauncherConfig m_Config;

        /// <summary>安装根目录。</summary>
        private string m_InstallRoot;

        /// <summary>日志器。</summary>
        private LauncherLog m_Log;

        /// <summary>日志窗口（第一次点「日志」时才创建，关闭后置空）。</summary>
        private LogWindowForm m_LogWindow;

        /// <summary>主视觉缓存（跟随窗口尺寸重建，避免每帧重画上百个图元）。</summary>
        private Bitmap m_KeyArt;

        /// <summary>图标按钮的悬停提示（图标本身不写字，必须给个说明）。</summary>
        private readonly ToolTip m_ToolTip = new ToolTip();

        // ---- 控件（在 LauncherForm.Layout.cs 里创建） ----
        private FlatButton m_PlayButton;
        private FlatButton m_DownloadButton;
        private FlatButton m_CheckButton;
        private FlatButton m_LogButton;
        private FlatButton m_SettingsButton;
        private FlatButton m_MinimizeButton;
        private FlatButton m_CloseButton;
        private Label m_BrandLabel;
        private Label m_HeadlineLabel;
        private Label m_StatusLabel;
        private Label m_SpeedLabel;
        private Label m_VersionLabel;
        private FlatProgressBar m_ProgressBar;
        private Panel m_Drawer;
        private ComboBox m_SourceCombo;
        private TextBox m_SourceText;
        private Label m_ServerValueLabel;
        private InstallRootRow m_InstallRow;

        /// <summary>创建主窗口。</summary>
        public LauncherForm()
        {
            BuildLayout();
            BuildChildren();
            Load += (_, __) => LoadConfiguration();
            Shown += (_, __) => OnShownLayout();
        }

        /// <summary>
        /// 给无边框窗口加系统投影。
        /// </summary>
        /// <remarks>
        /// 不要这层投影，窗口会像一张贴纸粘在桌面上：主视觉的深色边缘直接与桌面相邻，
        /// 分不出"窗口到哪结束"。
        /// </remarks>
        protected override CreateParams CreateParams
        {
            get
            {
                const int CsDropShadow = 0x00020000;
                var parameters = base.CreateParams;
                parameters.ClassStyle |= CsDropShadow;
                return parameters;
            }
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

            // "安装目录"一行需要配置对象，因此在这里（而不是布局阶段）创建并挂到抽屉上。
            m_InstallRow = new InstallRootRow(
                m_Config, this, AppendLog, m_DrawerFieldHost.ClientSize.Width);
            m_InstallRow.AddTo(m_DrawerFieldHost);
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

            // 隐藏地址的档案（通常是云主机：公网 IP 不该出现在演示视频与截图里）只把输入框留空；
            // 连接用的地址始终来自档案本身，所以隐藏不影响更新与联机。
            m_SourceText.Text = profile.HideAddress ? string.Empty : profile.ManifestSource;
            m_SourceText.ReadOnly = !profile.Editable;
            m_ServerValueLabel.Text = profile.HideAddress
                ? "(已隐藏)"
                : (string.IsNullOrWhiteSpace(profile.GameServer) ? "(未设置)" : profile.GameServer);

            m_Config.SelectedSource = profile.Name;
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

        /// <summary>刷新版本显示与主按钮文案。</summary>
        private void RefreshLocalVersion()
        {
            var local = UpdateApplier.LoadLocalManifest(m_InstallRoot);
            if (local == null)
            {
                m_VersionLabel.Text = "未安装";
                m_HeadlineLabel.Text = "准备进入战局";
                m_StatusLabel.Text = "首次启动会先下载本体";
                m_SpeedLabel.Text = string.Empty;
                return;
            }

            var text = "本体 " + (string.IsNullOrEmpty(local.body?.version) ? "未知" : local.body.version);
            if (local.HasContentLayer)
            {
                text += " · 资源 " + local.content.version;
            }

            m_VersionLabel.Text = text;

            if (!m_Busy)
            {
                m_HeadlineLabel.Text = "准备进入战局";
                m_StatusLabel.Text = "本地版本已就绪";
                m_SpeedLabel.Text = string.Empty;
            }
        }

        /// <summary>切换忙碌状态（三个按钮 + 抽屉一起禁用，防止改到一半的参数混进流程）。</summary>
        /// <param name="busy">是否忙碌。</param>
        private void SetBusy(bool busy)
        {
            m_Busy = busy;
            m_PlayButton.Enabled = !busy;
            m_DownloadButton.Enabled = !busy;
            m_CheckButton.Enabled = !busy;
            m_SettingsButton.Enabled = !busy;
            m_InstallRow?.SetBusy(busy);
            m_ProgressBar.Visible = busy;

            if (!busy)
            {
                m_ProgressBar.Ratio = 0f;
            }
        }

        /// <summary>追加一行日志（只落文件；界面上看日志要打开日志窗口）。</summary>
        /// <param name="message">内容。</param>
        private void AppendLog(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                m_Log?.Write(message);
            }
        }
    }
}
