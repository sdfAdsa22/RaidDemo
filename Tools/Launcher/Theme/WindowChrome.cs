using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RaidDemo.Launcher.Theme
{
    /// <summary>
    /// 无边框窗口需要自己补的两件事：拖动与圆角。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么改成无边框：</b>"参考米哈游启动器"的观感有一半来自"没有系统标题栏"——
    /// 一整张主视觉铺满窗口，而不是顶着一根灰色标题条。代价是标题栏原本提供的功能
    /// （拖动、最小化、关闭）都得自己实现，这里就是那部分。</para>
    ///
    /// <para><b>拖动为什么用系统消息而不是自己算坐标：</b>发 <c>WM_NCLBUTTONDOWN</c> 之后由系统接管拖动，
    /// 窗口贴边吸附、跨显示器缩放、拖动时的重绘节奏全都与原生窗口一致；
    /// 自己按鼠标位移改 Location 会在高 DPI 与多显示器下出现漂移。</para>
    /// </remarks>
    internal static class WindowChrome
    {
        /// <summary>非客户区左键按下。</summary>
        private const int WmNcLButtonDown = 0x00A1;

        /// <summary>命中测试：标题栏。</summary>
        private const int HitCaption = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern System.IntPtr SendMessage(
            System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);

        /// <summary>
        /// 让某个控件区域可以拖动整个窗口（用于自绘的标题栏区域）。
        /// </summary>
        /// <param name="control">可拖动区域。</param>
        public static void EnableDrag(Control control)
        {
            control.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                var form = control.FindForm();
                if (form == null)
                {
                    return;
                }

                ReleaseCapture();
                SendMessage(form.Handle, WmNcLButtonDown, (System.IntPtr)HitCaption, System.IntPtr.Zero);
            };
        }

        /// <summary>
        /// 把窗口裁成圆角。
        /// </summary>
        /// <param name="form">目标窗口。</param>
        /// <param name="radius">圆角半径（像素）。</param>
        /// <remarks>
        /// 用 <see cref="Form.Region"/> 裁剪是硬边缘（没有抗锯齿），
        /// 但配上系统投影（<c>CS_DROPSHADOW</c>）后观感足够，而且不依赖任何第三方库。
        /// </remarks>
        public static void ApplyRoundedCorners(Form form, int radius)
        {
            using var path = LauncherTheme.RoundedRect(
                new RectangleF(0f, 0f, form.Width, form.Height), radius);
            form.Region = new Region(path);
        }
    }
}
