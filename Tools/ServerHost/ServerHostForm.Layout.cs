using System;
using System.Drawing;
using System.Windows.Forms;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 面板窗体的界面搭建（控件与布局）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用代码搭界面而不是设计器文件：</b>本工程要与 Unity 侧共用一份配置模型，
    /// 用 <c>.Designer.cs</c> 会把界面与"字段有多少个"绑死在一个不能手改的文件里；
    /// 而字段数量正好取决于共享模型的字段个数（加一个配置项就要多一行）。
    /// 代码搭建让这件事保持在一个方法里可读、可在 review 里逐行核对。</para>
    ///
    /// <para><b>为什么标签里带上单位与范围：</b>面板是给玩家用的，而配置项的取值边界
    /// （端口 1~65535、战局时长 0~7200……）在出错之前没人会去翻文档。</para>
    /// </remarks>
    internal sealed partial class ServerHostForm
    {
        /// <summary>服务器目录输入框。</summary>
        private TextBox m_DirectoryBox;

        /// <summary>浏览目录按钮。</summary>
        private Button m_BrowseButton;

        /// <summary>在资源管理器里打开服务器目录。</summary>
        private Button m_OpenDirectoryButton;

        /// <summary>监听端口。</summary>
        private TextBox m_PortBox;

        /// <summary>房间名。</summary>
        private TextBox m_RoomBox;

        /// <summary>存档目录（相对路径）。</summary>
        private TextBox m_SaveDirectoryBox;

        /// <summary>开局加载的地图场景。</summary>
        private TextBox m_MapBox;

        /// <summary>日志等级。</summary>
        private ComboBox m_LogLevelBox;

        /// <summary>战局时长上限（秒）。</summary>
        private TextBox m_RaidDurationBox;

        /// <summary>自动开局等待（秒）。</summary>
        private TextBox m_AutoStartBox;

        /// <summary>掉线宽限（秒）。</summary>
        private TextBox m_GraceBox;

        /// <summary>状态页端口。</summary>
        private TextBox m_DashboardPortBox;

        /// <summary>局域网发现端口。</summary>
        private TextBox m_DiscoveryPortBox;

        /// <summary>传输层自愈看门狗（秒）。</summary>
        private TextBox m_WatchdogBox;

        /// <summary>保存配置。</summary>
        private Button m_SaveButton;

        /// <summary>恢复默认值。</summary>
        private Button m_DefaultsButton;

        /// <summary>启动服务器。</summary>
        private Button m_StartButton;

        /// <summary>停止服务器。</summary>
        private Button m_StopButton;

        /// <summary>复制连接信息。</summary>
        private Button m_CopyButton;

        /// <summary>打开日志目录。</summary>
        private Button m_OpenLogButton;

        /// <summary>打开状态页。</summary>
        private Button m_OpenDashboardButton;

        /// <summary>进程状态。</summary>
        private Label m_StatusLabel;

        /// <summary>状态页读数。</summary>
        private Label m_DashboardLabel;

        /// <summary>日志显示框。</summary>
        private TextBox m_LogBox;

        /// <summary>字段悬停提示（取值范围写在提示里，标签就能保持短，不会折行）。</summary>
        private ToolTip m_Hints;

        /// <summary>一个配置字段的说明（标签 + 悬停提示）。</summary>
        /// <remarks>
        /// 把取值范围放进提示而不是标签：全塞进标签会让"自愈看门狗（0 或 1~30 秒）"这类文字
        /// 折成两行，整个配置区高低不齐；而玩家真正需要范围的时候，通常正是鼠标停在那一格的时候。
        /// </remarks>
        private sealed class FieldHint
        {
            /// <summary>建立说明。</summary>
            /// <param name="label">字段标签。</param>
            /// <param name="hint">悬停提示。</param>
            public FieldHint(string label, string hint)
            {
                Label = label;
                Hint = hint;
            }

            /// <summary>字段标签。</summary>
            public string Label { get; }

            /// <summary>悬停提示。</summary>
            public string Hint { get; }
        }

        /// <summary>界面间距（像素）。</summary>
        private const int Gap = 6;

        /// <summary>窗体默认宽度（像素）。</summary>
        private const int DefaultWindowWidth = 900;

        /// <summary>窗体默认高度（像素）。</summary>
        private const int DefaultWindowHeight = 780;

        /// <summary>
        /// 字段标签列宽（像素）。
        /// </summary>
        /// <remarks>
        /// 标签里带着取值范围（"掉线宽限（5~600 秒）"），列窄了就会折成两行，
        /// 而折行会让整个配置区高低不齐、看上去像坏了。宽度按最长的那条标签留出余量。
        /// </remarks>
        private const int LabelColumnWidth = 148;

        /// <summary>字段输入列宽（像素）。</summary>
        private const int InputColumnWidth = 168;

        /// <summary>日志框高度（像素）。</summary>
        private const int LogBoxHeight = 220;

        /// <summary>搭出整个界面。</summary>
        private void BuildInterface()
        {
            SuspendLayout();

            Text = "RaidDemo 服务器面板";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(DefaultWindowWidth, DefaultWindowHeight);
            ClientSize = new Size(DefaultWindowWidth, DefaultWindowHeight);
            Font = new Font("Microsoft YaHei UI", 9f);

            m_Hints = new ToolTip
            {
                AutoPopDelay = 15000,
                InitialDelay = 300,
                ReshowDelay = 100,
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(Gap),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            root.Controls.Add(BuildDirectoryGroup(), 0, 0);
            root.Controls.Add(BuildConfigGroup(), 0, 1);
            root.Controls.Add(BuildButtonRow(), 0, 2);
            root.Controls.Add(BuildStatusRows(), 0, 3);
            root.Controls.Add(BuildLogGroup(), 0, 4);

            Controls.Add(root);
            ResumeLayout(performLayout: true);
        }

        /// <summary>服务器目录行：改目录 = 管理另一台服务器。</summary>
        private Control BuildDirectoryGroup()
        {
            m_DirectoryBox = new TextBox
            {
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BackColor = SystemColors.Window,
            };

            m_BrowseButton = CreateButton("浏览...");
            m_BrowseButton.Click += OnBrowseClicked;

            m_OpenDirectoryButton = CreateButton("打开目录");
            m_OpenDirectoryButton.Click += OnOpenDirectoryClicked;

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0),
            };
            row.Controls.Add(m_DirectoryBox);
            row.Controls.Add(m_BrowseButton);
            row.Controls.Add(m_OpenDirectoryButton);
            m_DirectoryBox.Width = DefaultWindowWidth - 260;

            var group = new GroupBox
            {
                Text = "服务器目录（看到的是生效的绝对路径）",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(Gap),
            };
            group.Controls.Add(row);
            return group;
        }

        /// <summary>配置字段：4 列 6 行，字段多了也不会把窗口撑得老高。</summary>
        private Control BuildConfigGroup()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 4,
                RowCount = 6,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InputColumnWidth));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InputColumnWidth));

            // 网格按"左列字段 / 右列字段"排布：加配置项时只需要在末尾补一行。
            m_PortBox = AddField(table, 0, 0, new FieldHint(
                "端口",
                "监听端口（UDP），1~65535。客户端用它连接服务器。"));
            m_RoomBox = AddField(table, 0, 2, new FieldHint(
                "房间名",
                "房间名，1~24 个字符（默认「默认房间」）。"));

            m_SaveDirectoryBox = AddField(table, 1, 0, new FieldHint(
                "存档目录",
                "相对服务器目录的存档目录（默认 server_saves）；不接受绝对路径，也不能含 ..。"));
            m_MapBox = AddField(table, 1, 2, new FieldHint(
                "地图场景",
                "开局加载的地图场景名（默认 GreyboxRaid）。"));

            m_LogLevelBox = AddLogLevelField(table, 2, 0, new FieldHint(
                "日志等级",
                "verbose / info / warning / error，控制写入 Logs/server.log 的详细程度。"));
            m_RaidDurationBox = AddField(table, 2, 2, new FieldHint(
                "战局时长上限(秒)",
                "0~7200 秒；0 表示不做超时判定（默认 480）。"));

            m_AutoStartBox = AddField(table, 3, 0, new FieldHint(
                "自动开局(秒)",
                "0~600 秒；0 表示等房主手动开局（默认 0）。"));
            m_GraceBox = AddField(table, 3, 2, new FieldHint(
                "掉线宽限(秒)",
                "5~600 秒，掉线玩家保留位置的时间（默认 60）。"));

            m_DashboardPortBox = AddField(table, 4, 0, new FieldHint(
                "状态页端口",
                "0~65535；0 表示关闭状态页（默认 8080）。"));
            m_DiscoveryPortBox = AddField(table, 4, 2, new FieldHint(
                "发现端口",
                "0~65535；0 表示关闭局域网自动发现（默认 47777）。"));

            m_WatchdogBox = AddField(table, 5, 0, new FieldHint(
                "自愈看门狗(秒)",
                "0 表示关闭；否则 1~30 秒，传输层多久没动静就重建连接（默认 2.5）。"));

            var group = new GroupBox
            {
                Text = "配置（保存后写入 server.config.json；启动时会自动保存）",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(Gap),
            };
            group.Controls.Add(table);
            return group;
        }

        /// <summary>按钮行。</summary>
        private Control BuildButtonRow()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0),
            };

            m_SaveButton = CreateButton("保存配置");
            m_SaveButton.Click += OnSaveClicked;
            m_DefaultsButton = CreateButton("恢复默认");
            m_DefaultsButton.Click += OnRestoreDefaultsClicked;
            m_StartButton = CreateButton("启动服务器");
            m_StartButton.Click += OnStartClicked;
            m_StopButton = CreateButton("停止服务器");
            m_StopButton.Click += OnStopClicked;
            m_CopyButton = CreateButton("复制连接信息");
            m_CopyButton.Click += OnCopyConnectionClicked;
            m_OpenLogButton = CreateButton("打开日志目录");
            m_OpenLogButton.Click += OnOpenLogDirectoryClicked;
            m_OpenDashboardButton = CreateButton("打开状态页");
            m_OpenDashboardButton.Click += OnOpenDashboardClicked;

            row.Controls.Add(m_SaveButton);
            row.Controls.Add(m_DefaultsButton);
            row.Controls.Add(m_StartButton);
            row.Controls.Add(m_StopButton);
            row.Controls.Add(m_CopyButton);
            row.Controls.Add(m_OpenLogButton);
            row.Controls.Add(m_OpenDashboardButton);
            return row;
        }

        /// <summary>状态行：进程状态 + 状态页读数。</summary>
        private Control BuildStatusRows()
        {
            m_StatusLabel = new Label
            {
                AutoSize = true,
                Text = "尚未启动服务器。",
                Font = new Font(Font, FontStyle.Bold),
            };

            m_DashboardLabel = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Text = "状态页：未连接（启动服务器后每 2 秒刷新一次）",
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0),
            };
            panel.Controls.Add(m_StatusLabel);
            panel.Controls.Add(m_DashboardLabel);
            return panel;
        }

        /// <summary>日志区：服务器写进 Logs/server.log，面板只读跟随。</summary>
        private Control BuildLogGroup()
        {
            m_LogBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(24, 26, 28),
                ForeColor = Color.Gainsboro,
                Height = LogBoxHeight,
            };

            var group = new GroupBox
            {
                Text = "服务器日志（Logs/server.log）",
                Dock = DockStyle.Fill,
                Padding = new Padding(Gap),
            };
            group.Controls.Add(m_LogBox);
            return group;
        }

    }
}
