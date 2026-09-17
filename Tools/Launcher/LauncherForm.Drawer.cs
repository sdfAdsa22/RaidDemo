using System;
using System.Drawing;
using System.Windows.Forms;
using RaidDemo.Launcher.Theme;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 右侧设置抽屉：更新源、安装目录、游戏服务器，以及恢复默认 / 保存。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把设置收进抽屉：</b>这些字段改完一次基本就不再动，
    /// 常驻在主界面上会把"开始游戏"挤到角落。收起来之后，主界面只留"现在是什么状态、下一步做什么"。</para>
    ///
    /// <para><b>为什么抽屉用滑动而不是直接显示：</b>直接显示会让人分不清"这是新的一页还是盖在上面"；
    /// 滑动把两者的空间关系说清楚了，也让误点之后容易收回。</para>
    /// </remarks>
    public sealed partial class LauncherForm
    {
        /// <summary>抽屉每个动画步长（像素）。</summary>
        private const int DrawerSlideStep = 46;

        /// <summary>抽屉动画间隔（毫秒）。</summary>
        private const int DrawerSlideIntervalMilliseconds = 12;

        /// <summary>抽屉是否已展开。</summary>
        private bool m_DrawerOpen;

        /// <summary>抽屉滑动定时器（惰性创建：不用抽屉就没有这笔开销）。</summary>
        private Timer m_DrawerTimer;

        /// <summary>搭出抽屉（含其中的字段与底部按钮）。</summary>
        private void BuildDrawer()
        {
            m_Drawer = new Panel
            {
                Size = new Size(DrawerWidth, ClientSize.Height),
                Location = new Point(ClientSize.Width, 0),   // 初始在窗口外
                BackColor = LauncherTheme.Cream,
            };
            m_Drawer.Paint += (_, e) =>
            {
                // 左侧一条深墨竖线：抽屉与主视觉之间需要一个明确的分界。
                using var pen = new Pen(LauncherTheme.Ink, 3f);
                e.Graphics.DrawLine(pen, 1.5f, 0f, 1.5f, m_Drawer.Height);
            };

            var title = new Label
            {
                Text = "设置",
                Font = LauncherTheme.SecondaryButtonFont,
                ForeColor = LauncherTheme.Ink,
                AutoSize = true,
                Location = new Point(DrawerPadding, 24),
            };

            var close = CreateSecondaryButton("收起", new Size(84, 32));
            close.Location = new Point(DrawerWidth - DrawerPadding - 84, 20);
            close.Click += (_, __) => ToggleDrawer();

            var sourceLabel = CreateFieldLabel("更新源", 76);
            m_SourceCombo = new ComboBox
            {
                Location = new Point(DrawerPadding, 100),
                Size = new Size(DrawerWidth - DrawerPadding * 2, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = LauncherTheme.Ink,
                Font = LauncherTheme.BodyFont,
            };
            m_SourceCombo.SelectedIndexChanged += (_, __) => OnProfileChanged();

            var addressLabel = CreateFieldLabel("更新源地址（自定义源可编辑）", 140);
            m_SourceText = new TextBox
            {
                Location = new Point(DrawerPadding, 164),
                Size = new Size(DrawerWidth - DrawerPadding * 2, 28),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = LauncherTheme.Ink,
                Font = LauncherTheme.BodyFont,
            };
            m_SourceText.Leave += (_, __) => PersistEditableSource();

            var installLabel = CreateFieldLabel("安装目录（自动创建 RaidDemo 子文件夹）", 206);
            m_DrawerFieldHost = new Panel
            {
                Location = new Point(DrawerPadding, 230),
                Size = new Size(DrawerWidth - DrawerPadding * 2, DrawerFieldHeight),
                BackColor = Color.Transparent,
            };

            // 安装目录那一行的宿主面板占 230~288（高度 DrawerFieldHeight = 58）。
            // 服务器标签原来放在 274，正好落在面板的下半部分里，被面板盖掉上沿，
            // 只剩下半截字（负责人反馈的"右侧字体显示不全"）。挪到面板下方即可。
            var serverLabel = CreateFieldLabel("游戏服务器（启动游戏时自动填入）", 298);
            m_ServerValueLabel = new Label
            {
                Text = "(未设置)",
                Font = LauncherTheme.BodyFont,
                ForeColor = LauncherTheme.Ink,
                AutoSize = false,
                Size = new Size(DrawerWidth - DrawerPadding * 2, 24),
                Location = new Point(DrawerPadding, 322),
            };

            var restore = CreateSecondaryButton("恢复默认", new Size(110, 36));
            restore.Location = new Point(DrawerPadding, ClientSize.Height - 58);
            restore.Click += (_, __) => OnRestoreDefaults();
            m_RestoreButton = restore;

            var save = new FlatButton(FlatButtonStyle.Primary)
            {
                Text = "保存设置",
                Font = LauncherTheme.SecondaryButtonFont,
                Size = new Size(126, 36),
                Location = new Point(DrawerWidth - DrawerPadding - 126, ClientSize.Height - 58),
            };
            save.Click += (_, __) => OnSaveSettings();
            m_SaveButton = save;

            m_Drawer.Controls.AddRange(new Control[]
            {
                title, close, sourceLabel, m_SourceCombo, addressLabel, m_SourceText,
                installLabel, m_DrawerFieldHost, serverLabel, m_ServerValueLabel, restore, save,
            });

            WindowChrome.EnableDrag(title);
            Controls.Add(m_Drawer);

            // 抽屉必须最前：后加入的控件 z 序更靠后，不 BringToFront 的话
            // 抽屉滑出来会被主界面上的标签与按钮盖住（实测踩过）。
            m_Drawer.BringToFront();
        }

        /// <summary>建一个抽屉字段标签。</summary>
        private static Label CreateFieldLabel(string text, int top)
        {
            return new Label
            {
                Text = text,
                Font = LauncherTheme.LabelFont,
                ForeColor = LauncherTheme.MutedOnCream,
                AutoSize = true,
                Location = new Point(DrawerPadding, top),
            };
        }

        /// <summary>展开 / 收起抽屉。</summary>
        private void ToggleDrawer()
        {
            m_DrawerOpen = !m_DrawerOpen;
            SetMainChromeVisible(!m_DrawerOpen);
            m_DrawerTimer ??= CreateDrawerTimer();
            m_DrawerTimer.Start();
        }

        /// <summary>
        /// 抽屉展开时收起主界面右上角的图标与右下角版本号。
        /// </summary>
        /// <param name="visible">是否显示。</param>
        /// <remarks>
        /// 这些控件在 z 序上位于抽屉**前面**（它们先加入窗体），抽屉滑上来之后会与它们重叠；
        /// 与其和 z 序较劲，不如在展开时把它们收起来——设置面板打开时本来也不需要它们。
        /// </remarks>
        private void SetMainChromeVisible(bool visible)
        {
            m_LogButton.Visible = visible;
            m_SettingsButton.Visible = visible;
            m_MinimizeButton.Visible = visible;
            m_CloseButton.Visible = visible;
            m_VersionLabel.Visible = visible;
        }

        /// <summary>建抽屉滑动定时器。</summary>
        private Timer CreateDrawerTimer()
        {
            var timer = new Timer { Interval = DrawerSlideIntervalMilliseconds };
            timer.Tick += (_, __) => StepDrawer();
            return timer;
        }

        /// <summary>推进一步抽屉动画；到位后停表。</summary>
        private void StepDrawer()
        {
            // 落点用**面板当前宽度**而不是设计常量：高 DPI 下整套界面被等比放大，
            // 面板宽度已不是 DrawerWidth（404）而是 1.5 倍的 606。仍按常量算落点，
            // 会让面板右侧约 200 像素留在窗口外——抽屉底部的"保存设置"被窗口边缘切掉
            // （负责人反馈的"启动器字体显示不全"里最靠右的那个按钮）。
            var target = m_DrawerOpen ? ClientSize.Width - m_Drawer.Width : ClientSize.Width;
            var delta = target - m_Drawer.Left;
            var step = Math.Max(1, (int)Math.Round(DrawerSlideStep * (DeviceDpi / 96f)));

            if (Math.Abs(delta) <= step)
            {
                m_Drawer.Left = target;
                m_DrawerTimer.Stop();
                return;
            }

            m_Drawer.Left += Math.Sign(delta) * step;
        }

        /// <summary>保存抽屉里的设置。</summary>
        private void OnSaveSettings()
        {
            if (TrySaveSettings(out var message))
            {
                m_StatusLabel.Text = message;
                return;
            }

            MessageBox.Show(this, message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>把界面上的值写回配置文件。</summary>
        /// <param name="message">给用户看的结果说明。</param>
        /// <returns>是否成功。</returns>
        private bool TrySaveSettings(out string message)
        {
            message = "设置已保存";
            try
            {
                m_Config.Save(AppContext.BaseDirectory);
                AppendLog("已保存设置：" + m_Config.LoadedFromPath);
                return true;
            }
            catch (Exception exception)
            {
                message = "保存配置失败：" + exception.Message;
                AppendLog(message);
                return false;
            }
        }

        /// <summary>恢复默认值（只改界面，点保存或开始更新时才落盘）。</summary>
        private void OnRestoreDefaults()
        {
            var defaults = LauncherConfig.Load(string.Empty);
            m_SourceCombo.Items.Clear();
            foreach (var profile in defaults.Sources)
            {
                m_SourceCombo.Items.Add(profile.Name);
            }

            var index = m_SourceCombo.Items.IndexOf(defaults.SelectedSource);
            m_SourceCombo.SelectedIndex = index >= 0 ? index : 0;
            m_InstallRow?.ShowCurrent(m_Config.GetInstallRootPath(AppContext.BaseDirectory));
            // 文案要短到放得下：状态行可视宽度 312 像素、13pt 正文约每字 17 像素，
            // 原来那句 18 个字（≈310px）必然被裁到最后一个字。
            m_StatusLabel.Text = "已恢复默认值（点「保存设置」）";
        }
    }
}
