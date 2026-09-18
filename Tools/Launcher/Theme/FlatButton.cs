using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RaidDemo.Launcher.Theme
{
    /// <summary>按钮外观：主按钮 / 次级按钮 / 圆形图标按钮。</summary>
    internal enum FlatButtonStyle
    {
        /// <summary>主按钮：青绿渐变 + 深墨描边 + 底部投影（"开始游戏"）。</summary>
        Primary,

        /// <summary>次级按钮：半透明深底 + 浅描边（"下载" / "检查更新"）。</summary>
        Ghost,

        /// <summary>图标按钮：右上角的日志 / 设置 / 最小化 / 关闭。</summary>
        Icon,
    }

    /// <summary>图标按钮画什么（不用 emoji：不同系统字体下大小与配色都不受控）。</summary>
    internal enum FlatIconGlyph
    {
        None,
        Document,
        Gear,
        Minimize,
        Close,
    }

    /// <summary>
    /// 自绘按钮：圆角、描边、悬停与按下态。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要自己画：</b>WinForms 原生按钮是系统主题绘制的，圆角、渐变、描边一个都改不了。
    /// "参考米哈游启动器"这种要求，本质上就是要求自绘。</para>
    ///
    /// <para><b>为什么不用 emoji 当图标：</b>emoji 的字形与配色由系统字体决定
    /// （Segoe UI Emoji 是彩色字体），在不同机器上大小、颜色都不一样，
    /// 甚至可能被渲染成方块。这里用几何图形画——所见即所得。</para>
    /// </remarks>
    internal sealed class FlatButton : Control
    {
        /// <summary>圆角半径（主/次级按钮）。</summary>
        private const int CornerRadius = 12;

        /// <summary>图标按钮的圆角半径。</summary>
        private const int IconCornerRadius = 9;

        /// <summary>主按钮底部投影的高度（像素，制造"可按下去"的厚度）。</summary>
        private const int PrimaryShadowHeight = 5;

        /// <summary>描边宽度。</summary>
        private const float OutlineWidth = 2.5f;

        private bool m_Hover;
        private bool m_Pressed;

        /// <summary>创建按钮。</summary>
        /// <param name="style">外观样式。</param>
        public FlatButton(FlatButtonStyle style)
        {
            Style = style;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        /// <summary>外观样式。</summary>
        public FlatButtonStyle Style { get; }

        /// <summary>图标按钮要画的图形。</summary>
        public FlatIconGlyph Glyph { get; set; } = FlatIconGlyph.None;

        /// <summary>图标按钮是否为"危险操作"（关闭：悬停时变砖红）。</summary>
        public bool IsCloseAction { get; set; }

        /// <inheritdoc />
        protected override void OnMouseEnter(EventArgs e)
        {
            m_Hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        /// <inheritdoc />
        protected override void OnMouseLeave(EventArgs e)
        {
            m_Hover = false;
            m_Pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        /// <inheritdoc />
        protected override void OnMouseDown(MouseEventArgs e)
        {
            m_Pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        /// <inheritdoc />
        protected override void OnMouseUp(MouseEventArgs e)
        {
            m_Pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        /// <inheritdoc />
        protected override void OnEnabledChanged(EventArgs e)
        {
            // 忙碌时按钮会被禁用：禁用态必须看得出来，否则玩家会一直点。
            m_Hover = false;
            m_Pressed = false;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        /// <inheritdoc />
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (Style == FlatButtonStyle.Icon)
            {
                PaintIcon(g);
                return;
            }

            PaintPill(g);
        }

        /// <summary>画主按钮 / 次级按钮。</summary>
        private void PaintPill(Graphics g)
        {
            // 主按钮按下时整体下移，让"厚度"消失——这是最省事也最直观的按压反馈。
            var offset = m_Pressed && Style == FlatButtonStyle.Primary ? PrimaryShadowHeight : 0;
            var shadow = Style == FlatButtonStyle.Primary ? PrimaryShadowHeight - offset : 0;
            var body = new RectangleF(
                1f,
                1f,
                Width - 2f,
                Height - 2f - (Style == FlatButtonStyle.Primary ? PrimaryShadowHeight : 0));

            if (shadow > 0)
            {
                using var shadowPath = LauncherTheme.RoundedRect(
                    new RectangleF(body.X, body.Y + PrimaryShadowHeight, body.Width, body.Height),
                    CornerRadius);
                using var shadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
                g.FillPath(shadowBrush, shadowPath);
            }

            body.Offset(0f, offset);
            using var path = LauncherTheme.RoundedRect(body, CornerRadius);

            if (Style == FlatButtonStyle.Primary)
            {
                // M13-10：忙碌时按钮功能已禁用，但主按钮仍是满色高亮，玩家看不出来。
                // 禁用态整体向灰白靠：亮部提浅、暗部提亮，文字同时降透明度。
                var disabled = !Enabled;
                using var fill = new LinearGradientBrush(
                    body,
                    disabled
                        ? ControlPaint.Light(LauncherTheme.TealLight, 0.6f)
                        : (m_Hover ? ControlPaint.Light(LauncherTheme.TealLight, 0.08f) : LauncherTheme.TealLight),
                    disabled
                        ? ControlPaint.Light(LauncherTheme.TealDeep, 0.5f)
                        : (m_Hover ? LauncherTheme.Teal : LauncherTheme.TealDeep),
                    LinearGradientMode.Vertical);
                g.FillPath(fill, path);
            }
            else
            {
                var alpha = !Enabled ? 55 : (m_Hover ? 150 : 105);
                using var fill = new SolidBrush(Color.FromArgb(alpha, LauncherTheme.Pine));
                g.FillPath(fill, path);
            }

            using var outline = new Pen(
                Enabled
                    ? (Style == FlatButtonStyle.Primary ? LauncherTheme.ArtOutline : Color.FromArgb(190, LauncherTheme.Cream))
                    : Color.FromArgb(110, LauncherTheme.Cream),
                OutlineWidth);
            g.DrawPath(outline, path);

            var color = Style == FlatButtonStyle.Primary
                ? (Enabled ? LauncherTheme.OnTeal : Color.FromArgb(140, LauncherTheme.OnTeal))
                : (Enabled ? LauncherTheme.TextOnArt : Color.FromArgb(120, LauncherTheme.TextOnArt));

            using var textBrush = new SolidBrush(color);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(Text, Font ?? LauncherTheme.SecondaryButtonFont, textBrush, body, format);
        }

        /// <summary>画右上角的图标按钮。</summary>
        private void PaintIcon(Graphics g)
        {
            var bounds = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            using var path = LauncherTheme.RoundedRect(bounds, IconCornerRadius);

            var disabled = !Enabled;
            var background = disabled
                ? Color.FromArgb(55, LauncherTheme.Pine)
                : (m_Hover
                    ? (IsCloseAction ? Color.FromArgb(215, 190, 74, 66) : Color.FromArgb(205, LauncherTheme.Pine))
                    : Color.FromArgb(120, LauncherTheme.Pine));
            using (var fill = new SolidBrush(background))
            {
                g.FillPath(fill, path);
            }

            using (var outline = new Pen(Color.FromArgb(disabled ? 70 : 150, LauncherTheme.Cream), 1.6f))
            {
                g.DrawPath(outline, path);
            }

            PaintGlyph(g, bounds, disabled ? 110 : 255);
        }

        /// <summary>画图标本体（几何图形，跟随按钮尺寸缩放）。</summary>
        private void PaintGlyph(Graphics g, RectangleF bounds, int alpha)
        {
            var ink = Color.FromArgb(alpha, LauncherTheme.Cream);
            using var pen = new Pen(ink, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var brush = new SolidBrush(ink);

            var cx = bounds.Left + bounds.Width / 2f;
            var cy = bounds.Top + bounds.Height / 2f;
            var r = System.Math.Min(bounds.Width, bounds.Height) * 0.26f;

            switch (Glyph)
            {
                case FlatIconGlyph.Document:
                    var page = new RectangleF(cx - r * 0.78f, cy - r, r * 1.56f, r * 2f);
                    g.DrawRectangle(pen, page.X, page.Y, page.Width, page.Height);
                    g.DrawLine(pen, page.Left + r * 0.34f, cy - r * 0.35f, page.Right - r * 0.34f, cy - r * 0.35f);
                    g.DrawLine(pen, page.Left + r * 0.34f, cy + r * 0.30f, page.Right - r * 0.34f, cy + r * 0.30f);
                    break;

                case FlatIconGlyph.Gear:
                    g.DrawEllipse(pen, cx - r * 0.62f, cy - r * 0.62f, r * 1.24f, r * 1.24f);
                    for (var i = 0; i < 6; i++)
                    {
                        var angle = i * System.Math.PI / 3.0;
                        var from = new PointF(
                            cx + (float)System.Math.Cos(angle) * r * 0.72f,
                            cy + (float)System.Math.Sin(angle) * r * 0.72f);
                        var to = new PointF(
                            cx + (float)System.Math.Cos(angle) * r * 1.12f,
                            cy + (float)System.Math.Sin(angle) * r * 1.12f);
                        g.DrawLine(pen, from, to);
                    }

                    break;

                case FlatIconGlyph.Minimize:
                    g.DrawLine(pen, cx - r, cy + r * 0.35f, cx + r, cy + r * 0.35f);
                    break;

                case FlatIconGlyph.Close:
                    g.DrawLine(pen, cx - r * 0.78f, cy - r * 0.78f, cx + r * 0.78f, cy + r * 0.78f);
                    g.DrawLine(pen, cx + r * 0.78f, cy - r * 0.78f, cx - r * 0.78f, cy + r * 0.78f);
                    break;
            }
        }
    }
}
