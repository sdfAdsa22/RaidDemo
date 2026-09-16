using System;
using System.Globalization;
using System.Windows.Forms;
using RaidDemo.Kernel.Server;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 服务器面板主窗体：改配置、启停服务器、看日志与状态。
    /// </summary>
    /// <remarks>
    /// <para><b>它在整条链路里的位置：</b>面板改 <c>server.config.json</c> → 启动
    /// <c>RaidDemoServer.exe</c> → 服务器读同一份配置 → 面板按 <c>Logs/server.log</c> 与
    /// <c>/status.json</c> 回报状态。四步里面板只拥有第一步与第三步，另外两步的权威在游戏侧——
    /// 因此面板从不"自己判断服务器好不好"，它只转述进程与状态页给出的结论。</para>
    ///
    /// <para><b>为什么启动前自动保存：</b>在面板里改了端口却没点保存、然后点启动，
    /// 得到的是"服务器还听在旧端口上"这种最难查的错位（面板显示 8000、服务器实际 7777）。
    /// 自动保存把这两件事合成一个动作，代价只是玩家少点一次按钮。</para>
    ///
    /// <para><b>为什么关面板不自动停服务器：</b>开服的人常常需要关掉面板去干别的，
    /// 服务器继续服务是正常用法。关闭时弹一次选择（停 / 不停），把决定权留给玩家。</para>
    /// </remarks>
    internal sealed partial class ServerHostForm : Form
    {
        /// <summary>状态页轮询间隔（毫秒）。</summary>
        private const int DashboardPollIntervalMilliseconds = 2000;

        /// <summary>日志框保留的最大字符数（超出后裁掉最早的部分，避免长跑吃掉内存）。</summary>
        private const int MaxLogCharacters = 200000;

        /// <summary>日志框裁剪后保留的字符数。</summary>
        private const int TrimmedLogCharacters = 150000;

        /// <summary>是否启动定时器与日志跟随（渲染截图时为 false）。</summary>
        private readonly bool m_Interactive;

        /// <summary>进程控制。</summary>
        private readonly ServerProcessLauncher m_Launcher = new ServerProcessLauncher();

        /// <summary>日志跟随器。</summary>
        private LogTailer m_Tailer;

        /// <summary>状态页轮询定时器。</summary>
        private Timer m_DashboardTimer;

        /// <summary>当前服务器目录布局。</summary>
        private ServerLayout m_Layout;

        /// <summary>当前生效的配置（用于停止时取状态页端口）。</summary>
        private ServerConfigDocument m_Document = ServerConfigStore.CreateDefault();

        /// <summary>是否已有一次状态页轮询在飞（避免慢响应时堆请求）。</summary>
        private bool m_Polling;

        /// <summary>上一次操作留下的提示（与进程状态一起显示）。</summary>
        private string m_LastMessage;

        /// <summary>
        /// 建立窗体。
        /// </summary>
        /// <param name="directory">服务器目录（绝对路径）。</param>
        /// <param name="interactive">是否启用轮询与日志跟随。</param>
        public ServerHostForm(string directory, bool interactive)
        {
            m_Interactive = interactive;

            BuildInterface();
            LoadFromDirectory(directory);

            m_Launcher.Exited += OnServerExited;
            FormClosing += OnFormClosing;
            FormClosed += (_, _) => ReleaseResources();

            if (m_Interactive)
            {
                m_DashboardTimer = new Timer { Interval = DashboardPollIntervalMilliseconds };
                m_DashboardTimer.Tick += OnDashboardTick;
                m_DashboardTimer.Start();
            }
        }

        /// <summary>
        /// 切换服务器目录并重新装载配置与日志。
        /// </summary>
        /// <param name="directory">新目录。</param>
        public void LoadFromDirectory(string directory)
        {
            m_Layout = ServerLayout.FromDirectory(directory);
            m_DirectoryBox.Text = m_Layout.Directory;

            if (!ServerConfigStore.TryLoad(m_Layout.ConfigPath, out var document, out var error))
            {
                m_LastMessage = $"配置文件有误：{error}（已显示默认值，保存会覆盖它）";
                document = ServerConfigStore.CreateDefault();
            }
            else
            {
                m_LastMessage = m_Layout.HasConfig ? "已读取配置文件。" : "没有配置文件，显示的是内置默认值。";
            }

            m_Document = document;
            PopulateFromDocument(document);
            RestartTailer();
            UpdateStatus();
        }

        /// <summary>把配置填进界面（未设置的字段显示默认值：玩家关心的是实际生效的那套值）。</summary>
        /// <param name="document">配置内容。</param>
        public void PopulateFromDocument(ServerConfigDocument document)
        {
            m_PortBox.Text = IntegerText(document.port, ServerConfigDocument.DefaultPort);
            m_RoomBox.Text = ServerConfigStore.EffectiveText(document.room, ServerConfigDocument.DefaultRoomName);
            m_SaveDirectoryBox.Text = ServerConfigStore.EffectiveText(
                document.saveDir,
                ServerConfigDocument.DefaultSaveDirectory);
            m_MapBox.Text = ServerConfigStore.EffectiveText(document.map, ServerConfigDocument.DefaultMapScene);
            m_LogLevelBox.SelectedItem = ServerConfigStore.EffectiveText(
                document.logLevel,
                ServerConfigDocument.DefaultLogLevel);
            m_RaidDurationBox.Text = NumberText(
                document.raidDuration,
                ServerConfigDocument.DefaultRaidDurationSeconds);
            m_AutoStartBox.Text = NumberText(document.autoStart, ServerConfigDocument.DefaultAutoStartSeconds);
            m_GraceBox.Text = NumberText(document.grace, ServerConfigDocument.DefaultReconnectGraceSeconds);
            m_WatchdogBox.Text = NumberText(
                document.watchdog,
                ServerConfigDocument.DefaultTransportWatchdogSeconds);
            m_DashboardPortBox.Text = IntegerText(document.dashboardPort, ServerConfigDocument.DefaultDashboardPort);
            m_DiscoveryPortBox.Text = IntegerText(
                document.discoveryPort,
                ServerConfigDocument.DefaultDiscoveryPort);
        }

        /// <summary>
        /// 读取界面上的配置。
        /// </summary>
        /// <param name="document">解析结果。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否成功。</returns>
        /// <remarks>
        /// 留空的字段写成哨兵值（未设置）：这与手写配置文件"只写想改的那几项"是同一种语义，
        /// 服务器会把它们回退到内置默认。
        /// </remarks>
        public bool TryReadDocument(out ServerConfigDocument document, out string error)
        {
            document = null;
            error = null;

            var result = new ServerConfigDocument
            {
                room = m_RoomBox.Text.Trim(),
                saveDir = m_SaveDirectoryBox.Text.Trim(),
                map = m_MapBox.Text.Trim(),
                logLevel = (m_LogLevelBox.SelectedItem as string ?? string.Empty).Trim(),
            };

            if (!TryReadInteger(m_PortBox, "端口", out var port, out error)
                || !TryReadInteger(m_DashboardPortBox, "状态页端口", out var dashboardPort, out error)
                || !TryReadInteger(m_DiscoveryPortBox, "发现端口", out var discoveryPort, out error)
                || !TryReadNumber(m_RaidDurationBox, "战局时长上限", out var raidDuration, out error)
                || !TryReadNumber(m_AutoStartBox, "自动开局", out var autoStart, out error)
                || !TryReadNumber(m_GraceBox, "掉线宽限", out var grace, out error)
                || !TryReadNumber(m_WatchdogBox, "自愈看门狗", out var watchdog, out error))
            {
                return false;
            }

            result.port = port;
            result.dashboardPort = dashboardPort;
            result.discoveryPort = discoveryPort;
            result.raidDuration = raidDuration;
            result.autoStart = autoStart;
            result.grace = grace;
            result.watchdog = watchdog;

            if (!ServerConfigValidation.TryValidateSummary(result, out var validationError))
            {
                error = validationError;
                return false;
            }

            document = result;
            return true;
        }

        /// <summary>异步停止服务器（关闭窗体与点按钮共用）。</summary>
        /// <returns>停止过程的等待任务。</returns>
        public async System.Threading.Tasks.Task StopServerAsync()
        {
            m_StopButton.Enabled = false;
            var report = await m_Launcher.StopAsync(m_Layout, m_Document).ConfigureAwait(true);
            m_LastMessage = report;
            UpdateStatus();
        }

        /// <summary>把一句提示记进状态栏。</summary>
        /// <param name="message">提示文本。</param>
        public void Report(string message)
        {
            m_LastMessage = message;
            UpdateStatus();
        }

        /// <summary>刷新状态文字与按钮可用性。</summary>
        private void UpdateStatus()
        {
            var running = m_Launcher.IsRunning;
            var state = running ? $"服务器运行中（进程号 {m_Launcher.ProcessId}）。" : "服务器未运行。";
            m_StatusLabel.Text = string.IsNullOrEmpty(m_LastMessage) ? state : $"{state}  {m_LastMessage}";

            m_StartButton.Enabled = !running;
            m_StopButton.Enabled = running;
            m_SaveButton.Enabled = true;
            m_DefaultsButton.Enabled = !running;
        }

        /// <summary>重建日志跟随器（切换目录时）。</summary>
        private void RestartTailer()
        {
            m_Tailer?.Dispose();
            m_LogBox.Clear();

            if (!m_Interactive)
            {
                return;
            }

            m_Tailer = new LogTailer(m_Layout.LogFilePath);
            m_Tailer.TextAppended += OnLogTextAppended;
            m_Tailer.Start();
        }

        /// <summary>整数文本：哨兵值显示默认值。</summary>
        private static string IntegerText(int value, int fallback)
        {
            return ServerConfigStore.EffectiveInteger(value, fallback).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>浮点文本：哨兵值显示默认值。</summary>
        private static string NumberText(float value, float fallback)
        {
            return ServerConfigStore.FormatNumber(ServerConfigStore.EffectiveNumber(value, fallback));
        }

        /// <summary>读一个整数输入框（留空 = 未设置）。</summary>
        private static bool TryReadInteger(TextBox box, string label, out int value, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                value = ServerConfigDocument.UnsetInt;
                return true;
            }

            if (!ServerConfigValidation.TryParseInteger(box.Text, out value))
            {
                error = $"{label}需要整数，实际收到「{box.Text}」。";
                return false;
            }

            return true;
        }

        /// <summary>读一个浮点输入框（留空 = 未设置）。</summary>
        private static bool TryReadNumber(TextBox box, string label, out float value, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                value = ServerConfigDocument.UnsetFloat;
                return true;
            }

            if (!ServerConfigValidation.TryParseNumber(box.Text, out value))
            {
                error = $"{label}需要数字，实际收到「{box.Text}」。";
                return false;
            }

            return true;
        }
    }
}
