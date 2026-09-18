using System;
using System.Drawing;
using System.Windows.Forms;
using RaidDemo.Launcher.Theme;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 主界面的布局：主视觉背景、左下按钮区、右上图标行、右侧设置抽屉。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么全部用代码摆控件：</b>本工程从 M0 起就不使用设计器文件——
    /// 设计器生成的代码既难读，也不方便把"颜色 / 位置"这类常量集中管理。</para>
    ///
    /// <para><b>坐标都写成常量或按窗口尺寸推导：</b>窗口是固定尺寸的无边框窗口，
    /// 唯一会变的是 DPI 缩放（WinForms 会按比例缩放控件坐标），因此不需要动态布局引擎。</para>
    /// </remarks>
    public sealed partial class LauncherForm
    {
        /// <summary>左下按钮区：距左边缘。</summary>
        private const int PanelLeft = 52;

        /// <summary>左下按钮区：主按钮上沿距窗口底部。</summary>
        private const int PanelBottom = 42;

        /// <summary>主按钮尺寸。</summary>
        private static readonly Size PlayButtonSize = new Size(252, 56);

        /// <summary>次级按钮尺寸。</summary>
        private static readonly Size SecondaryButtonSize = new Size(118, 56);

        /// <summary>进度条尺寸。</summary>
        private static readonly Size ProgressBarSize = new Size(504, 12);

        /// <summary>右上角图标按钮尺寸。</summary>
        private const int IconButtonSize = 38;

        /// <summary>右上角图标按钮之间的间隔。</summary>
        private const int IconButtonGap = 10;

        /// <summary>右上角图标按钮距边缘。</summary>
        private const int IconButtonMargin = 18;

        /// <summary>抽屉宽度。</summary>
        private const int DrawerWidth = 404;

        /// <summary>抽屉内容距左右边缘。</summary>
        private const int DrawerPadding = 26;

        /// <summary>抽屉里的字段块高度（标签 + 输入行）。</summary>
        private const int DrawerFieldHeight = 58;

        /// <summary>抽屉里的"安装目录"字段宿主（供 <see cref="InstallRootRow"/> 挂控件）。</summary>
        private Panel m_DrawerFieldHost;

        /// <summary>搭主界面。</summary>
        private void BuildLayout()
        {
            Text = "RaidDemo 启动器";
            FormBorderStyle = FormBorderStyle.None;      // 自绘标题栏（主视觉铺满）
            StartPosition = FormStartPosition.CenterScreen;

            // 缩放自己算（见 ApplyDpiScale）：WinForms 的 AutoScaleMode.Dpi 在本工程
            // 这套"全代码摆控件 + 自绘"的结构里会把窗口反向缩到 0.67 倍（实测），
            // 与其和框架的自动缩放拉扯，不如在缩放基准上只做一次显式的等比缩放。
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(WindowWidth, WindowHeight);
            BackColor = LauncherTheme.Pine;               // 主视觉绘好之前的兜底色
            DoubleBuffered = true;
            KeyPreview = true;
        }

        /// <summary>窗体显示后补上圆角、主视觉与不能提前创建的部分。</summary>
        /// <remarks>
        /// 圆角依赖窗口尺寸与句柄，必须在句柄创建之后做；主视觉则依赖 <c>ClientSize</c>。
        /// 放在 <c>Shown</c> 里可以避免"先闪一下方角再变圆角"。
        /// </remarks>
        private void OnShownLayout()
        {
            ApplyDpiScale();

            // 缩放完成之后再锁定窗口尺寸：Min/Max 若在 BuildLayout 里写死设计尺寸，
            // 会把按 DPI 放大后的窗口夹回去，等于把自动缩放抵消掉。
            MinimumSize = Size;
            MaximumSize = Size;

            WindowChrome.ApplyRoundedCorners(this, ScaledRadius(WindowCornerRadius));
            RebuildKeyArt();
        }

        /// <summary>
        /// 句柄创建后立刻按 DPI 缩放一次。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么放在这里而不是构造函数里：</b>窗口的显示器 DPI 要等句柄创建之后才确定；
        /// 构造函数阶段 <c>DeviceDpi</c> 还是 96，据此算出来的缩放系数是 1（等于没缩放）。</para>
        ///
        /// <para><b>为什么还要在 Shown 里再来一次：</b>跨显示器拖动或系统缩放变化时，
        /// 句柄创建阶段拿到的可能仍是上一块屏的 DPI；<see cref="ApplyDpiScale"/> 自己带幂等判断，
        /// 重复调用不会把界面越缩越小。</para>
        /// </remarks>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDpiScale();
        }

        /// <summary>主视觉背景：预渲染到一张位图上，绘制时直接贴。</summary>
        /// <param name="e">绘制参数。</param>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (m_KeyArt == null)
            {
                base.OnPaintBackground(e);
                return;
            }

            e.Graphics.DrawImageUnscaled(m_KeyArt, 0, 0);
        }

        /// <summary>窗口尺寸变化时重建主视觉。</summary>
        /// <param name="e">事件参数。</param>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RebuildKeyArt();
            WindowChrome.ApplyRoundedCorners(this, ScaledRadius(WindowCornerRadius));
        }

        /// <summary>把设计像素换算成当前 DPI 下的物理像素。</summary>
        /// <param name="designPixels">按 96 DPI 写的设计值。</param>
        /// <remarks>只用于"交给系统 API 的数值"（圆角半径等）；控件坐标由 WinForms 自动缩放。</remarks>
        private int ScaledRadius(int designPixels)
        {
            return (int)Math.Round(designPixels * (DeviceDpi / 96f));
        }

        /// <summary>
        /// 按显示器 DPI 把整套界面等比放大一次。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么需要：</b>窗口在 150% 缩放的屏幕上如果不对 DPI 作声明，
        /// Windows 会把整幅画面当位图拉伸——中文与描边全部发糊（负责人反馈的"启动器不清晰"）。</para>
        ///
        /// <para><b>为什么要自己缩放：</b>本工程的布局是"全代码写的固定设计尺寸 + 自绘主视觉"，
        /// 交给 <c>AutoScaleMode.Dpi</c> 时实测得到的是 0.67 倍（方向相反）。
        /// 显式调用 <c>Scale()</c> 走的是设计器同一条缩放路径：子控件位置、尺寸与字体一起放大，
        /// 主视觉本来就按 <c>ClientSize</c> 重画，因此自动跟随。</para>
        /// </remarks>
        private void ApplyDpiScale()
        {
            var scale = DeviceDpi / 96f;
            if (Math.Abs(scale - 1f) < 0.01f || m_DpiScaled)
            {
                return;
            }

            m_DpiScaled = true;
            // 子控件交给 Scale 放大，窗口本身要显式改成放大后的客户区——
            // Control.Scale 只按比例调整子控件，不会改宿主窗口的大小（实测：只 Scale 会让内容溢出）。
            Scale(new SizeF(scale, scale));
            ClientSize = new Size(
                (int)Math.Round(WindowWidth * scale),
                (int)Math.Round(WindowHeight * scale));

            // 立刻把窗口尺寸锁死：抽屉里的控件在 LoadConfiguration 之后会按自己的内容
            // 把窗口顶高（实测 930 → 986，正好是安装目录行的高度），而这块界面本来就是
            // 按固定尺寸设计的，多出来的高度只会让主视觉下沿露出底色。
            MinimumSize = Size;
            MaximumSize = Size;

            // 抽屉底部的两个按钮按"缩放后的抽屉尺寸"重新贴一次右下角：
            // 它们原本是按设计高度算的固定坐标，缩放后容易落到面板外（实测被窗口右缘切掉半截）。
            RepositionDrawerButtons(scale);
        }

        /// <summary>把抽屉底部的两个按钮重新贴到缩放后的面板底部。</summary>
        private void RepositionDrawerButtons(float scale)
        {
            if (m_Drawer == null || m_RestoreButton == null || m_SaveButton == null)
            {
                return;
            }

            var padding = (int)Math.Round(DrawerPadding * scale);
            var bottom = m_Drawer.ClientSize.Height - (int)Math.Round(22 * scale) - m_RestoreButton.Height;
            m_RestoreButton.Location = new Point(padding, bottom);
            m_SaveButton.Location = new Point(
                m_Drawer.ClientSize.Width - padding - m_SaveButton.Width, bottom);
        }

        /// <summary>抽屉底部的"恢复默认"按钮（缩放后需要重新定位）。</summary>
        private FlatButton m_RestoreButton;

        /// <summary>抽屉底部的"保存设置"按钮（缩放后需要重新定位）。</summary>
        private FlatButton m_SaveButton;

        /// <summary>本次运行是否已经按 DPI 缩放过（幂等保护）。</summary>
        private bool m_DpiScaled;

        /// <summary>按当前客户区尺寸重画主视觉。</summary>
        private void RebuildKeyArt()
        {
            var width = Math.Max(1, ClientSize.Width);
            var height = Math.Max(1, ClientSize.Height);
            if (m_KeyArt != null && m_KeyArt.Width == width && m_KeyArt.Height == height)
            {
                return;
            }

            var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                KeyArtRenderer.Draw(graphics, bitmap.Size);
            }

            m_KeyArt?.Dispose();
            m_KeyArt = bitmap;
            Invalidate();
        }

        /// <summary>搭出所有子控件（在构造函数的 <c>BuildLayout</c> 之后调用）。</summary>
        private void BuildChildren()
        {
            BuildTopIcons();
            BuildLeftPanel();
            BuildDrawer();
            WireWindowChrome();
        }

        /// <summary>右上角：日志、设置、最小化、关闭。</summary>
        private void BuildTopIcons()
        {
            m_CloseButton = CreateIconButton(FlatIconGlyph.Close, "关闭", isClose: true);
            m_MinimizeButton = CreateIconButton(FlatIconGlyph.Minimize, "最小化", isClose: false);
            m_SettingsButton = CreateIconButton(FlatIconGlyph.Gear, "设置", isClose: false);
            m_LogButton = CreateIconButton(FlatIconGlyph.Document, "日志", isClose: false);

            var order = new[] { m_LogButton, m_SettingsButton, m_MinimizeButton, m_CloseButton };
            for (var i = 0; i < order.Length; i++)
            {
                var right = IconButtonMargin + i * (IconButtonSize + IconButtonGap);
                order[i].Location = new Point(ClientSize.Width - right - IconButtonSize, IconButtonMargin);
                Controls.Add(order[i]);
            }

            m_CloseButton.Click += (_, __) => Close();
            m_MinimizeButton.Click += (_, __) => WindowState = FormWindowState.Minimized;
            m_SettingsButton.Click += (_, __) => ToggleDrawer();
            m_LogButton.Click += (_, __) => OpenLogWindow();
        }

        /// <summary>建一个右上角图标按钮。</summary>
        private FlatButton CreateIconButton(FlatIconGlyph glyph, string tooltip, bool isClose)
        {
            var button = new FlatButton(FlatButtonStyle.Icon)
            {
                Glyph = glyph,
                IsCloseAction = isClose,
                Size = new Size(IconButtonSize, IconButtonSize),
                // 图标按钮不绘制 Text（PaintIcon 只画几何图形），但 WinForms 的
                // 无障碍名称会回退到 Text：M13-18 实测 AccessibleName 在 UIA 桥里不生效，
                // 因此把说明写进 Text，让屏幕阅读器能看到"关闭/最小化/设置/日志"。
                Text = tooltip,
                AccessibleName = tooltip,
            };
            m_ToolTip.SetToolTip(button, tooltip);
            return button;
        }

        /// <summary>左下按钮区：产品名、大标题、状态行、进度条、三个按钮。</summary>
        private void BuildLeftPanel()
        {
            var bottom = ClientSize.Height - PanelBottom;

            m_CheckButton = CreateSecondaryButton("检查更新", new Size(122, SecondaryButtonSize.Height));
            m_DownloadButton = CreateSecondaryButton("下载", SecondaryButtonSize);
            m_PlayButton = new FlatButton(FlatButtonStyle.Primary)
            {
                Text = "开始游戏",
                Font = LauncherTheme.ButtonFont,
                Size = PlayButtonSize,
            };

            var buttonBottom = bottom;
            m_CheckButton.Location = new Point(PanelLeft + PlayButtonSize.Width + 12 + SecondaryButtonSize.Width + 12, buttonBottom - SecondaryButtonSize.Height);
            m_DownloadButton.Location = new Point(PanelLeft + PlayButtonSize.Width + 12, buttonBottom - SecondaryButtonSize.Height);
            m_PlayButton.Location = new Point(PanelLeft, buttonBottom - PlayButtonSize.Height);

            m_PlayButton.Click += async (_, __) => await RunAsync(applyChanges: true, launchAfterUpdate: true);
            m_DownloadButton.Click += async (_, __) => await RunAsync(applyChanges: true, launchAfterUpdate: false);
            m_CheckButton.Click += async (_, __) => await RunAsync(applyChanges: false, launchAfterUpdate: false);

            // 垂直节奏自下而上推导：按钮 → 进度条 → 状态行 → 大标题 → 产品名。
            // 标题用 AutoSize（随字号变高），所以必须**先建出来再按它的实际高度**往上排——
            // 写死偏移量的话，一改字号标题就会压住状态行（实测踩过）。
            var progressTop = m_PlayButton.Top - 24;
            var statusTop = progressTop - 26;

            m_ProgressBar = new FlatProgressBar
            {
                Size = ProgressBarSize,
                Location = new Point(PanelLeft, progressTop),
                Visible = false,
            };

            m_HeadlineLabel = CreateArtLabel("准备进入战局", LauncherTheme.TitleFont,
                new Point(PanelLeft - 3, statusTop));
            m_BrandLabel = CreateArtLabel("RAIDDEMO · 搜打撤", LauncherTheme.BrandFont,
                new Point(PanelLeft, 0));

            m_StatusLabel = CreateArtLabel("就绪", LauncherTheme.BodyFont,
                new Point(PanelLeft, statusTop));
            // 状态与体积说明并排、**互不重叠**：WinForms 的透明控件不参与兄弟控件的合成，
            // 后画的透明标签会把先画的文字擦掉（实测踩过：状态行只剩半截影子）。
            m_StatusLabel.AutoSize = false;
            // 宽度取到"下载速度标签"的左缘为止（PanelLeft + 312）：再宽就会盖住右边那列。
            m_StatusLabel.Size = new Size(312, 20);
            m_StatusLabel.TextAlign = ContentAlignment.MiddleLeft;

            m_SpeedLabel = CreateArtLabel(string.Empty, LauncherTheme.BodyFont, Point.Empty);
            m_SpeedLabel.AutoSize = false;
            m_SpeedLabel.TextAlign = ContentAlignment.MiddleRight;
            m_SpeedLabel.Size = new Size(ProgressBarSize.Width - 312, 20);
            m_SpeedLabel.Location = new Point(PanelLeft + 312, statusTop);

            m_VersionLabel = CreateArtLabel("未安装", LauncherTheme.SmallFont, Point.Empty);
            m_VersionLabel.AutoSize = false;
            m_VersionLabel.TextAlign = ContentAlignment.MiddleRight;
            m_VersionLabel.Size = new Size(360, 20);
            m_VersionLabel.Location = new Point(ClientSize.Width - 380, ClientSize.Height - 36);

            Controls.AddRange(new Control[]
            {
                m_BrandLabel, m_HeadlineLabel, m_StatusLabel, m_SpeedLabel,
                m_ProgressBar, m_PlayButton, m_DownloadButton, m_CheckButton, m_VersionLabel,
            });

            // 标题与产品名用 AutoSize，高度要等控件真正挂到窗体上、跑过一次布局才准。
            // 因此这里先 AddRange + PerformLayout，再按实测高度往上排——
            // 在 AddRange 之前读 Height 会拿到一个偏小的值，结果是标题压住状态行（实测踩过）。
            PerformLayout();
            m_HeadlineLabel.Location = new Point(PanelLeft - 3, statusTop - m_HeadlineLabel.Height - 6);
            m_BrandLabel.Location = new Point(PanelLeft, m_HeadlineLabel.Top - m_BrandLabel.Height - 2);

            // 文字区域也要能拖动窗口：无边框窗口里，玩家的第一反应是"抓住画面上任意空白拖"
            // （标题往往正是他按下的地方）。只让窗体本体可拖会让人觉得"拖不动"。
            foreach (var label in new[] { m_BrandLabel, m_HeadlineLabel, m_StatusLabel, m_SpeedLabel, m_VersionLabel })
            {
                WindowChrome.EnableDrag(label);
            }
        }

        /// <summary>建一个叠在主视觉上的浅色文字标签。</summary>
        private static Label CreateArtLabel(string text, Font font, Point location)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = LauncherTheme.TextOnArt,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = location,
            };
        }

        /// <summary>建一个次级按钮。</summary>
        private static FlatButton CreateSecondaryButton(string text, Size size)
        {
            return new FlatButton(FlatButtonStyle.Ghost)
            {
                Text = text,
                Font = LauncherTheme.SecondaryButtonFont,
                Size = size,
            };
        }

        /// <summary>把标题栏拖动接上（顶栏空白区域可拖动窗口）。</summary>
        private void WireWindowChrome()
        {
            WindowChrome.EnableDrag(this);
        }
    }
}
