using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RaidDemo.Launcher.Theme;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 启动器界面上的"安装目录"一行：文本框 + 浏览按钮。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一个类：</b>这一行的职责是"选目录 → 校验 → 落配置"，
    /// 与"选更新源、看版本、下载"三件事没有关系。留在主窗体里会让主窗体承担两件无关的职责，
    /// 也会把它顶过工程规范的单文件 400 行上限（规范第 5.2 节）。</para>
    ///
    /// <para><b>它刻意不管"日志跟着安装目录走"：</b>切换之后日志文件要换到新安装根下，
    /// 那是主窗体与日志器之间的事。<see cref="Changed"/> 事件把这件事交回主窗体，
    /// 本类只保证"配置已经改好、目录已经存在"这一半。</para>
    ///
    /// <para><b>日志时序（有意为之）：</b>本类写出的日志都是"切换过程中"的记录，
    /// 因此落在**旧目录**的日志文件里；新目录收到第一条日志是主窗体在
    /// <see cref="Changed"/> 之后写的"安装目录已切换"。两份日志各记一半，
    /// 合起来就是一条完整的轨迹——比只在其中一份里写"切过去了"更经得起事后核对。</para>
    /// </remarks>
    internal sealed class InstallRootRow
    {
        /// <summary>浏览按钮宽度（像素）。</summary>
        private const int ButtonWidth = 96;

        /// <summary>输入行高度（像素）：文本框与按钮必须一样高才对齐。</summary>
        private const int ControlHeight = 28;

        /// <summary>文本框与按钮之间的间隙（像素）。</summary>
        private const int ControlGap = 8;

        /// <summary>配置对象（安装根的唯一真源）。</summary>
        private readonly LauncherConfig m_Config;

        /// <summary>对话框与消息框的父窗口（保证弹窗居中在启动器上而不是屏幕角落）。</summary>
        private readonly IWin32Window m_Owner;

        /// <summary>日志回调（主窗体注入，写界面日志框 + 当日日志文件）。</summary>
        private readonly Action<string> m_Log;

        private readonly TextBox m_Text = new TextBox();
        private readonly FlatButton m_Browse = new FlatButton(FlatButtonStyle.Ghost);

        /// <summary>当前生效的安装根（绝对路径）。</summary>
        private string m_Current = string.Empty;

        /// <summary>切换成功后触发；参数是新的绝对路径。</summary>
        public event Action<string> Changed;

        /// <summary>
        /// 创建这一行的控件。
        /// </summary>
        /// <param name="config">启动器配置。</param>
        /// <param name="owner">父窗口。</param>
        /// <param name="log">日志回调。</param>
        /// <param name="width">这一行的可用宽度（像素）：文本框会自动占满按钮之外的部分。</param>
        /// <remarks>
        /// 只创建控件、不加入容器：加入的时机由主窗体决定（它还要保证控件都挂上去之后才做布局）。
        /// 宽度参数化，是为了让同一行既能放进主界面，也能放进更窄的设置抽屉。
        /// </remarks>
        public InstallRootRow(LauncherConfig config, IWin32Window owner, Action<string> log, int width)
        {
            m_Config = config;
            m_Owner = owner;
            m_Log = log;

            m_Text.Location = new Point(0, 0);
            m_Text.Size = new Size(Math.Max(80, width - ButtonWidth - ControlGap), ControlHeight);
            m_Text.BorderStyle = BorderStyle.FixedSingle;
            m_Text.BackColor = Color.White;
            m_Text.ForeColor = LauncherTheme.Ink;
            m_Text.Font = LauncherTheme.BodyFont;
            // 失焦时才校验：打字途中每敲一个字符就弹一次错没法用。
            m_Text.Leave += (_, __) => Apply(m_Text.Text);

            m_Browse.Text = "浏览…";
            m_Browse.Font = LauncherTheme.SecondaryButtonFont;
            m_Browse.Location = new Point(width - ButtonWidth, 0);
            m_Browse.Size = new Size(ButtonWidth, ControlHeight);
            m_Browse.Click += (_, __) => Browse();
        }

        /// <summary>把这一行的控件加入窗体。</summary>
        /// <param name="parent">目标容器。</param>
        public void AddTo(Control parent)
        {
            parent.Controls.AddRange(new Control[] { m_Text, m_Browse });
        }

        /// <summary>刷新文本框显示的当前安装根（不触发切换）。</summary>
        /// <param name="path">绝对路径。</param>
        public void ShowCurrent(string path)
        {
            m_Current = path;
            m_Text.Text = path;
        }

        /// <summary>切换忙碌状态（更新期间禁止改目录）。</summary>
        /// <param name="busy">是否忙碌。</param>
        public void SetBusy(bool busy)
        {
            m_Browse.Enabled = !busy;
            m_Text.ReadOnly = busy;
        }

        /// <summary>弹出文件夹选择框。</summary>
        /// <remarks>
        /// <para><b>起始位置用"当前安装根"：</b>想换目录的玩家多半只是换一个盘，
        /// 从当前位置出发比每次从"此电脑"顶层开始少点好几层。</para>
        ///
        /// <para><b>允许目录不存在：</b>对话框自带"新建文件夹"，玩家也可以先选一个空目录，
        /// 由应用时创建——"必须先手动建好目录"是多余的负担。</para>
        /// </remarks>
        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择游戏安装目录（游戏本体与更新文件都会放在该目录下）";
                dialog.ShowNewFolderButton = true;
                dialog.SelectedPath = Directory.Exists(m_Current) ? m_Current : AppContext.BaseDirectory;

                if (dialog.ShowDialog(m_Owner) == DialogResult.OK)
                {
                    Apply(dialog.SelectedPath);
                }
            }
        }

        /// <summary>
        /// 应用玩家选择（或输入）的安装目录。
        /// </summary>
        /// <param name="candidate">候选路径，来自文件夹选择框或文本框。</param>
        /// <remarks>
        /// <para><b>失败时把文本框弹回旧值：</b>让输入框留着一个"没被接受"的路径，
        /// 玩家随后点"更新并启动"时会以为更新进了那个目录，而实际写的是旧目录。
        /// 界面显示与实际行为不一致是排障成本最高的一类问题，所以宁可把输入退回去。</para>
        ///
        /// <para><b>选中的目录并不是安装目录：</b>玩家选的是"盘 / 父目录"，
        /// 真正的安装根是它下面的 <see cref="LauncherConfig.DefaultGameFolderName"/> 子目录
        /// （见 <see cref="LauncherConfig.EnsureGameFolder"/>）。文本框显示的是换算后的最终路径，
        /// 让人一眼看到"文件到底会落在哪"。</para>
        ///
        /// <para><b>为什么切换时就把目录建出来：</b>把"没有写权限"（例如误选
        /// <c>C:\Program Files</c>）这类错误提前到切换的这一刻报出来，
        /// 而不是让玩家等下载了几十兆之后才失败。空目录的创建是幂等的，没有副作用。</para>
        /// </remarks>
        private void Apply(string candidate)
        {
            if (m_Text.ReadOnly)
            {
                // 忙碌中（更新途中）：直接把文本退回当前值，避免"改了一半的安装根"混进下载流程。
                m_Text.Text = m_Current;
                return;
            }

            var gameDirectory = LauncherConfig.EnsureGameFolder(candidate);
            if (!m_Config.TrySetInstallRoot(gameDirectory, AppContext.BaseDirectory, out var resolved, out var error))
            {
                Report("安装目录无效", error);
                return;
            }

            if (string.Equals(resolved, m_Current, StringComparison.OrdinalIgnoreCase))
            {
                // 没变（含"手工把相对路径写成等价的绝对路径"）：只把文本刷成规范形态。
                m_Text.Text = m_Current;
                return;
            }

            if (!TryCreateDirectory(resolved, out var createError))
            {
                Report("安装目录不可用", "无法在该位置创建安装目录：" + createError);
                return;
            }

            m_Current = resolved;
            m_Text.Text = resolved;
            m_Log?.Invoke("安装目录已切换为：" + resolved);
            SaveConfig();
            Changed?.Invoke(resolved);
        }

        /// <summary>尝试创建目录。</summary>
        /// <param name="path">目标目录。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否成功。</returns>
        private static bool TryCreateDirectory(string path, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>把配置写回本机覆盖文件。</summary>
        /// <remarks>
        /// 保存失败不影响本次使用（内存里的配置已生效），但必须让玩家看见——
        /// 否则下次启动会"莫名回到旧目录"，而他手上没有任何线索。
        /// </remarks>
        private void SaveConfig()
        {
            try
            {
                m_Config.Save(AppContext.BaseDirectory);
                m_Log?.Invoke("安装目录已写入配置：" + m_Config.LoadedFromPath);
            }
            catch (Exception exception)
            {
                m_Log?.Invoke("保存配置失败（本次仍然生效，重启后会回到旧目录）：" + exception.Message);
            }
        }

        /// <summary>弹提示 + 记日志 + 把文本框退回当前值。</summary>
        /// <param name="title">提示标题。</param>
        /// <param name="message">提示内容。</param>
        private void Report(string title, string message)
        {
            m_Log?.Invoke(title + "：" + message);
            MessageBox.Show(m_Owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            m_Text.Text = m_Current;
        }
    }
}
