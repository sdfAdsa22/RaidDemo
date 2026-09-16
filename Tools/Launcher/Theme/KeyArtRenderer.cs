using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RaidDemo.Launcher.Theme
{
    /// <summary>
    /// 主视觉（key art）绘制：一版卡通扁平风格的"集装箱仓库"场景。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用代码画而不是塞一张原图：</b>启动器窗口尺寸可能变化，
    /// 位图拉伸会糊；而矢量绘制随尺寸重画始终清晰，文件体积也可忽略（自包含单文件发布下，
    /// 每多一张几 MB 的原画，玩家下载启动器就要多等一会儿）。</para>
    ///
    /// <para><b>风格来源：</b>与游戏内一致的卡通扁平——大色块、深色描边、没有写实渐变。
    /// 画面元素刻意选"集装箱 + 草地 + 远山"，和游戏地图（下沉盆地工业区）是同一套视觉语言。</para>
    ///
    /// <para><b>为什么按"相对比例"布局：</b>所有坐标都按宽高的百分比换算，
    /// 于是同一段代码在 1000×620 与 1200×700 下得到的是同一个构图，
    /// 而不是"换个尺寸就露馅"的固定坐标。</para>
    /// </remarks>
    internal static class KeyArtRenderer
    {
        /// <summary>地平线高度（占画面高度的比例）。</summary>
        private const float HorizonRatio = 0.60f;

        /// <summary>集装箱的基准大小（占画面高度的比例）。</summary>
        private const float ContainerHeightRatio = 0.11f;

        /// <summary>
        /// 把主视觉画到指定画布。
        /// </summary>
        /// <param name="graphics">目标画布（已设置好抗锯齿）。</param>
        /// <param name="size">画布尺寸。</param>
        public static void Draw(Graphics graphics, Size size)
        {
            var width = size.Width;
            var height = size.Height;
            var horizon = height * HorizonRatio;

            DrawSky(graphics, width, height, horizon);
            DrawSun(graphics, width, height);
            DrawMountains(graphics, width, height, horizon);
            DrawGround(graphics, width, height, horizon);
            DrawContainers(graphics, width, height, horizon);
            DrawShade(graphics, width, height);
        }

        /// <summary>天空：上浅下深的竖直渐变。</summary>
        private static void DrawSky(Graphics graphics, int width, int height, float horizon)
        {
            var bounds = new Rectangle(0, 0, width, (int)horizon + 1);
            using var brush = new LinearGradientBrush(
                bounds, LauncherTheme.SkyTop, LauncherTheme.SkyBottom, LinearGradientMode.Vertical);
            graphics.FillRectangle(brush, bounds);
        }

        /// <summary>太阳：一个偏暖的圆，压在右上角。</summary>
        private static void DrawSun(Graphics graphics, int width, int height)
        {
            var radius = height * 0.105f;
            var center = new PointF(width * 0.80f, height * 0.21f);
            using var brush = new SolidBrush(Color.FromArgb(226, LauncherTheme.Sun));
            graphics.FillEllipse(
                brush,
                center.X - radius,
                center.Y - radius,
                radius * 2f,
                radius * 2f);
        }

        /// <summary>远山：两层三角折线，后层更淡，制造纵深。</summary>
        private static void DrawMountains(Graphics graphics, int width, int height, float horizon)
        {
            var far = new[]
            {
                new PointF(0f, horizon),
                new PointF(width * 0.15f, horizon - height * 0.16f),
                new PointF(width * 0.30f, horizon),
                new PointF(width * 0.44f, horizon - height * 0.19f),
                new PointF(width * 0.58f, horizon),
                new PointF(width * 0.72f, horizon - height * 0.15f),
                new PointF(width * 0.86f, horizon),
                new PointF(width, horizon - height * 0.12f),
                new PointF(width, horizon),
            };

            using (var brush = new SolidBrush(Color.FromArgb(150, LauncherTheme.Mountain)))
            {
                graphics.FillPolygon(brush, far);
            }
        }

        /// <summary>草地：远景亮、近景暗，并在近处点几个灌木球。</summary>
        private static void DrawGround(Graphics graphics, int width, int height, float horizon)
        {
            var bounds = new Rectangle(0, (int)horizon - 2, width, height - (int)horizon + 2);
            using (var brush = new LinearGradientBrush(
                       bounds, LauncherTheme.GroundTop, LauncherTheme.GroundBottom, LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(brush, bounds);
            }

            // 灌木只是"草地上有点东西"的暗示：透明度压低，免得在深色区域变成一坨黑斑。
            using var bush = new SolidBrush(Color.FromArgb(70, LauncherTheme.GroundBottom));
            graphics.FillEllipse(bush, width * 0.06f, height * 0.76f, height * 0.08f, height * 0.06f);
            graphics.FillEllipse(bush, width * 0.69f, height * 0.81f, height * 0.10f, height * 0.07f);
            graphics.FillEllipse(bush, width * 0.90f, height * 0.74f, height * 0.06f, height * 0.045f);
        }

        /// <summary>
        /// 集装箱堆：一排错落的货柜 + 一根路灯。
        /// </summary>
        /// <remarks>
        /// 每个货柜都是"圆角矩形 + 深色描边 + 一条顶面高光"，三个元素就够表达体积；
        /// 再多的细节在启动器这种尺寸下反而显脏。
        /// </remarks>
        private static void DrawContainers(Graphics graphics, int width, int height, float horizon)
        {
            var unit = height * ContainerHeightRatio;
            var outline = new Pen(LauncherTheme.ArtOutline, System.Math.Max(2f, height * 0.005f))
            {
                LineJoin = LineJoin.Round,
            };

            DrawContainer(graphics, outline, new RectangleF(width * 0.09f, horizon - unit * 0.85f, unit * 3.0f, unit), 0);
            DrawContainer(graphics, outline, new RectangleF(width * 0.31f, horizon - unit * 1.55f, unit * 2.6f, unit * 1.55f), 1);
            DrawContainer(graphics, outline, new RectangleF(width * 0.50f, horizon - unit * 0.70f, unit * 2.4f, unit * 0.70f), 2);
            DrawContainer(graphics, outline, new RectangleF(width * 0.66f, horizon - unit * 1.25f, unit * 1.9f, unit * 1.25f), 3);
            DrawContainer(graphics, outline, new RectangleF(width * 0.80f, horizon - unit * 0.60f, unit * 2.3f, unit * 0.60f), 4);

            // 前后两层货柜之间补一组"叠放"的小柜：堆场里最典型的样子就是两层堆叠，
            // 只摆一排会像货架而不是堆场。
            DrawContainer(graphics, outline, new RectangleF(width * 0.235f, horizon - unit * 0.95f, unit * 1.5f, unit * 0.95f), 2);
            DrawContainer(graphics, outline, new RectangleF(width * 0.605f, horizon - unit * 1.05f, unit * 1.2f, unit * 1.05f), 0);
        }

        /// <summary>画一个货柜（圆角矩形 + 描边 + 顶面高光）。</summary>
        private static void DrawContainer(
            Graphics graphics,
            Pen outline,
            RectangleF bounds,
            int colorIndex)
        {
            var color = LauncherTheme.ContainerColors[colorIndex % LauncherTheme.ContainerColors.Length];
            using var path = LauncherTheme.RoundedRect(bounds, System.Math.Min(8f, bounds.Height * 0.18f));
            using (var fill = new SolidBrush(color))
            {
                graphics.FillPath(fill, path);
            }

            graphics.DrawPath(outline, path);

            // 顶面高光：一条比底色更亮的横带，暗示光源在右上。
            var stripe = new RectangleF(
                bounds.Left + bounds.Width * 0.08f,
                bounds.Top + bounds.Height * 0.16f,
                bounds.Width * 0.84f,
                System.Math.Max(3f, bounds.Height * 0.10f));
            using var stripeBrush = new SolidBrush(ControlPaint.Light(color, 0.22f));
            using var stripePath = LauncherTheme.RoundedRect(stripe, stripe.Height / 2f);
            graphics.FillPath(stripeBrush, stripePath);
        }

        /// <summary>
        /// 压一层暗角：左下更暗，用来托住主按钮与文字。
        /// </summary>
        /// <remarks>
        /// 没有这层，白色标题压在浅绿草地上会读不清；有了它，主按钮也自然成为视觉落点——
        /// 这是"沉浸式主视觉"里最关键的一步，不是装饰。
        /// </remarks>
        private static void DrawShade(Graphics graphics, int width, int height)
        {
            var bounds = new Rectangle(0, 0, width, height);
            using var shade = new LinearGradientBrush(
                bounds,
                Color.FromArgb(150, 9, 22, 21),
                Color.FromArgb(0, 9, 22, 21),
                LinearGradientMode.Horizontal);
            graphics.FillRectangle(shade, bounds);

            using var bottom = new LinearGradientBrush(
                bounds,
                Color.FromArgb(0, 9, 22, 21),
                Color.FromArgb(120, 9, 22, 21),
                LinearGradientMode.Vertical);
            graphics.FillRectangle(bottom, bounds);
        }
    }
}
