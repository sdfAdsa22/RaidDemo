using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RaidDemo.Launcher.Theme
{
    /// <summary>
    /// 自绘进度条：圆角轨道 + 圆角进度 + 深墨描边。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用系统 ProgressBar：</b>系统控件的方块造型与主题完全冲突，
    /// 而且不能画描边（在浅色主视觉上会"糊"进背景）。</para>
    ///
    /// <para><b>为什么进度值用 float：</b>下载是 0.1% 级别地推进的，
    /// 整型百分比会让进度条一会儿不动、一会儿跳一格；内部按比例画即可。</para>
    /// </remarks>
    internal sealed class FlatProgressBar : Control
    {
        /// <summary>描边宽度。</summary>
        private const float OutlineWidth = 2f;

        private double m_Ratio;

        /// <summary>创建进度条。</summary>
        public FlatProgressBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            Height = 12;
        }

        /// <summary>进度（0~1）；超范围会被夹住。</summary>
        /// <remarks>用 <c>double</c> 是为了与更新会话的 <c>UpdateProgress.Ratio</c> 同类型，避免各处强转。</remarks>
        public double Ratio
        {
            get => m_Ratio;
            set
            {
                var clamped = System.Math.Max(0d, System.Math.Min(1d, value));
                if (System.Math.Abs(clamped - m_Ratio) < 0.0005d)
                {
                    return;
                }

                m_Ratio = clamped;
                Invalidate();
            }
        }

        /// <inheritdoc />
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var radius = Height / 2f;
            var track = new RectangleF(1f, 1f, Width - 2f, Height - 2f);

            using (var path = LauncherTheme.RoundedRect(track, radius))
            using (var fill = new SolidBrush(Color.FromArgb(150, 9, 26, 24)))
            {
                g.FillPath(fill, path);
                using var outline = new Pen(Color.FromArgb(120, LauncherTheme.Cream), OutlineWidth);
                g.DrawPath(outline, path);
            }

            var width = (float)(track.Width * m_Ratio);
            if (width < 2f)
            {
                return;
            }

            var fillRect = new RectangleF(track.Left, track.Top, width, track.Height);
            using var fillPath = LauncherTheme.RoundedRect(fillRect, radius);
            using var gradient = new LinearGradientBrush(
                fillRect, LauncherTheme.TealLight, LauncherTheme.Teal, LinearGradientMode.Horizontal);
            g.FillPath(gradient, fillPath);
        }
    }
}
