using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using RaidDemo.Launcher.Theme;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 日志窗口：主界面不再显示日志，需要排查时才打开它。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把日志从主界面挪走：</b>日志框占了主界面一半面积，
    /// 而玩家 99% 的时间不需要看它——它把"开始游戏"这件正事挤到了角落。
    /// 排障时再看，而且可以看得更大、更专注。</para>
    ///
    /// <para><b>为什么读文件而不是内存缓冲：</b>启动器的日志本来就落盘
    /// （<c>&lt;安装根&gt;/.raiddemo/launcher.log</c>），直接读它就意味着
    /// "窗口里看到的"与"事后能拿到的"永远是同一份内容，不存在两套日志。</para>
    ///
    /// <para><b>为什么定时刷新而不是订阅：</b>日志由后台线程写入，窗口只做只读展示；
    /// 每秒读一次文件尾部的成本可以忽略，却省掉了一整套跨线程通知。</para>
    /// </remarks>
    internal sealed class LogWindowForm : Form
    {
        /// <summary>显示的最大行数（再多的历史对排障没有帮助，只会让窗口变卡）。</summary>
        private const int MaxLines = 500;

        /// <summary>刷新间隔（毫秒）。</summary>
        private const int RefreshIntervalMilliseconds = 1000;

        private const int CornerRadius = 12;

        private readonly string m_LogFilePath;
        private readonly TextBox m_TextBox = new TextBox();
        private readonly Timer m_Timer = new Timer();

        /// <summary>创建日志窗口。</summary>
        /// <param name="logFilePath">日志文件路径。</param>
        public LogWindowForm(string logFilePath)
        {
            m_LogFilePath = logFilePath;

            Text = "RaidDemo 启动器 · 日志";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 460);
            BackColor = LauncherTheme.Pine;
            ShowInTaskbar = false;
            KeyPreview = true;

            BuildLayout();

            Load += (_, __) =>
            {
                WindowChrome.ApplyRoundedCorners(this, CornerRadius);
                RefreshLog();
                m_Timer.Interval = RefreshIntervalMilliseconds;
                m_Timer.Tick += (__, ___) => RefreshLog();
                m_Timer.Start();
            };
            FormClosed += (_, __) => m_Timer.Stop();
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };
        }

        /// <summary>搭界面：标题行 + 日志正文 + 底部操作。</summary>
        private void BuildLayout()
        {
            var title = new Label
            {
                Text = "启动器日志",
                ForeColor = LauncherTheme.TextOnArt,
                Font = LauncherTheme.SecondaryButtonFont,
                AutoSize = true,
                Location = new Point(22, 18),
                BackColor = Color.Transparent,
            };

            var close = new FlatButton(FlatButtonStyle.Icon)
            {
                Glyph = FlatIconGlyph.Close,
                IsCloseAction = true,
                Size = new Size(34, 34),
                Location = new Point(Width - 52, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            close.Click += (_, __) => Close();

            BuildLogTextBox();

            var open = new FlatButton(FlatButtonStyle.Ghost)
            {
                Text = "在资源管理器中打开",
                Font = LauncherTheme.BodyFont,
                Size = new Size(184, 34),
                Location = new Point(22, Height - 52),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            };
            open.Click += (_, __) => OpenLogFile();

            var path = new Label
            {
                Text = m_LogFilePath,
                ForeColor = Color.FromArgb(180, LauncherTheme.Cream),
                Font = LauncherTheme.SmallFont,
                AutoSize = true,
                Location = new Point(220, Height - 42),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                BackColor = Color.Transparent,
            };

            Controls.AddRange(new Control[] { title, close, m_TextBox, open, path });
            WindowChrome.EnableDrag(title);
        }

        /// <summary>日志正文：深色只读文本框，铺满中间区域。</summary>
        private void BuildLogTextBox()
        {
            m_TextBox.Multiline = true;
            m_TextBox.ReadOnly = true;
            m_TextBox.ScrollBars = ScrollBars.Vertical;
            m_TextBox.WordWrap = false;
            m_TextBox.BorderStyle = BorderStyle.None;
            m_TextBox.BackColor = Color.FromArgb(24, 20, 18);
            m_TextBox.ForeColor = Color.Gainsboro;
            m_TextBox.Font = new Font("Consolas", 9.5f);
            m_TextBox.Location = new Point(22, 52);
            m_TextBox.Size = new Size(Width - 44, Height - 118);
            m_TextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        }

        /// <summary>读日志尾部并刷新文本框（内容不变时不重设，避免滚动位置被重置）。</summary>
        private void RefreshLog()
        {
            var text = ReadTail();
            if (string.Equals(m_TextBox.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            m_TextBox.Text = text;
            m_TextBox.SelectionStart = m_TextBox.TextLength;
            m_TextBox.ScrollToCaret();
        }

        /// <summary>读文件尾部若干行；文件还不存在时给出提示而不是空白。</summary>
        private string ReadTail()
        {
            try
            {
                if (!File.Exists(m_LogFilePath))
                {
                    return "（还没有日志；更新开始后这里会出现内容）";
                }

                var lines = File.ReadAllLines(m_LogFilePath, Encoding.UTF8);
                var start = Math.Max(0, lines.Length - MaxLines);
                var builder = new StringBuilder();
                for (var i = start; i < lines.Length; i++)
                {
                    builder.AppendLine(lines[i]);
                }

                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "（读日志失败：" + exception.Message + "）";
            }
        }

        /// <summary>在资源管理器里定位日志文件。</summary>
        private void OpenLogFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(m_LogFilePath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{m_LogFilePath}\"")
                    {
                        UseShellExecute = true,
                    });
                    return;
                }

                MessageBox.Show(this, "日志目录还不存在：" + directory, "找不到日志", MessageBoxButtons.OK);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "打开失败：" + exception.Message, "找不到日志", MessageBoxButtons.OK);
            }
        }
    }
}
