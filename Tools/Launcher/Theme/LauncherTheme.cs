using System.Drawing;
using System.Drawing.Drawing2D;

namespace RaidDemo.Launcher.Theme
{
    /// <summary>
    /// 启动器主题：颜色、字体与圆角绘制工具。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一层：</b>配色和字体如果散落在各个控件的 OnPaint 里，
    /// 改一次风格就得满仓库找十六进制色值，而且很容易出现"这个按钮的青绿和那个差一点"。
    /// 集中在这里之后，"启动器长什么样"只有一个答案。</para>
    ///
    /// <para><b>取色来自游戏内界面规范</b>（<c>Docs/Modules/08_UI主题与界面规范.md</c>）：
    /// 奶油纸面 + 深墨描边 + 青绿主交互色。启动器是玩家见到的第一个界面，
    /// 它和游戏内不是一套配色的话，"这是同一个产品"的第一印象就没了。</para>
    /// </remarks>
    internal static class LauncherTheme
    {
        /// <summary>奶油纸面（面板底色）。</summary>
        public static readonly Color Cream = FromHex("#F6EFE3");

        /// <summary>深墨（描边与正文）。</summary>
        public static readonly Color Ink = FromHex("#2E2A26");

        /// <summary>青绿（主交互色）。</summary>
        public static readonly Color Teal = FromHex("#2FA8A0");

        /// <summary>青绿的亮色端（按钮渐变上沿）。</summary>
        public static readonly Color TealLight = FromHex("#49C4BB");

        /// <summary>青绿的暗色端（按钮渐变下沿，营造厚度）。</summary>
        public static readonly Color TealDeep = FromHex("#279089");

        /// <summary>主按钮文字色（深到在青绿上仍然清楚）。</summary>
        public static readonly Color OnTeal = FromHex("#062321");

        /// <summary>深绿松（退出/关闭一类动作、标题栏按钮悬停）。</summary>
        public static readonly Color Pine = FromHex("#1C3A35");

        /// <summary>天空上沿。</summary>
        public static readonly Color SkyTop = FromHex("#A8DDD4");

        /// <summary>天空下沿（靠近地平线）。</summary>
        public static readonly Color SkyBottom = FromHex("#63AFA9");

        /// <summary>草地近处。</summary>
        public static readonly Color GroundTop = FromHex("#7FC08D");

        /// <summary>草地远处（同样的绿色压暗，制造纵深）。</summary>
        public static readonly Color GroundBottom = FromHex("#488B69");

        /// <summary>远山。</summary>
        public static readonly Color Mountain = FromHex("#4C8C88");

        /// <summary>太阳。</summary>
        public static readonly Color Sun = FromHex("#F7E7B6");

        /// <summary>主视觉里集装箱的配色（橙 / 黄 / 蓝 / 军绿 / 砖红）。</summary>
        public static readonly Color[] ContainerColors =
        {
            FromHex("#E4794B"),
            FromHex("#F2B544"),
            FromHex("#5FA8D8"),
            FromHex("#B7C36A"),
            FromHex("#D96A6A"),
        };

        /// <summary>主视觉的描边色（比深墨更冷一点，压在绿色背景上更稳）。</summary>
        public static readonly Color ArtOutline = FromHex("#12332F");

        /// <summary>浅色面板上的次要文字。</summary>
        public static readonly Color MutedOnCream = FromHex("#7A7266");

        /// <summary>主视觉上的文字（半透明白，避免和背景打架）。</summary>
        public static readonly Color TextOnArt = FromHex("#F2FBF7");

        /// <summary>标题字体（主视觉上的大标题）。</summary>
        public static Font TitleFont { get; } = CreateFont(34f, FontStyle.Bold);

        /// <summary>小标题（字距放大的英文名）。</summary>
        public static Font BrandFont { get; } = CreateFont(12f, FontStyle.Bold);

        /// <summary>状态行与说明文字。</summary>
        public static Font BodyFont { get; } = CreateFont(13f, FontStyle.Regular);

        /// <summary>主按钮文字。</summary>
        public static Font ButtonFont { get; } = CreateFont(19f, FontStyle.Bold);

        /// <summary>次级按钮文字。</summary>
        public static Font SecondaryButtonFont { get; } = CreateFont(14f, FontStyle.Bold);

        /// <summary>版本号一类的极小文字。</summary>
        public static Font SmallFont { get; } = CreateFont(11.5f, FontStyle.Regular);

        /// <summary>抽屉里的字段名。</summary>
        public static Font LabelFont { get; } = CreateFont(12.5f, FontStyle.Regular);

        /// <summary>
        /// 生成圆角矩形路径。
        /// </summary>
        /// <param name="bounds">矩形范围。</param>
        /// <param name="radius">圆角半径（像素）。</param>
        /// <returns>可直接用于填充与描边的路径（调用方负责释放）。</returns>
        /// <remarks>
        /// GDI+ 没有现成的圆角矩形，必须自己拼弧线。半径超过短边一半时要夹住，
        /// 否则画出来的是"两头尖"的怪形状——这是手绘圆角最常见的翻车点。
        /// </remarks>
        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            var limit = System.Math.Min(bounds.Width, bounds.Height) / 2f;
            var r = System.Math.Max(0f, System.Math.Min(radius, limit));
            var path = new GraphicsPath();

            if (r <= 0.01f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var d = r * 2f;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180f, 90f);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270f, 90f);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        /// <summary>把 <c>#RRGGBB</c> 转成颜色（集中在这里，避免各处硬写 RGB）。</summary>
        private static Color FromHex(string hex)
        {
            return ColorTranslator.FromHtml(hex);
        }

        /// <summary>建字体；字体缺失时 GDI+ 会自动回退到系统默认族。</summary>
        private static Font CreateFont(float size, FontStyle style)
        {
            // 字号一律用**像素**而不是磅：本工程的界面会按显示器 DPI 等比缩放一次
            // （见 LauncherForm.ApplyDpiScale），而磅值本身就会随 DPI 放大——
            // 两者叠加会让文字被放大两次，长标签直接被顶出面板（负责人反馈的
            // "右侧字体显示不全"就是这么来的）。像素单位下"缩放一次"就是唯一的一次。
            // 磅 → 像素按 96 DPI 换算（1pt = 96/72 px），因此这里的取值与原来的视觉一致。
            return new Font("Microsoft YaHei UI", size * (96f / 72f), style, GraphicsUnit.Pixel);
        }
    }
}
