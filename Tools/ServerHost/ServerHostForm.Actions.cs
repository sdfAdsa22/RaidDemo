using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using RaidDemo.Kernel.Server;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 面板的事件处理（按钮、定时器、进程退出、窗体关闭）。
    /// </summary>
    /// <remarks>
    /// <para><b>贯穿所有处理器的两条约束：</b></para>
    /// <list type="number">
    /// <item><b>失败要说人话，并且留在界面上。</b>服务器的启动失败原因（找不到 exe、端口被占、
    /// 配置非法）都会写进日志，但那要先知道去翻日志；面板把结论直接写在状态行上；</item>
    /// <item><b>后台线程只做一件事：把文本丢回 UI 线程。</b>日志跟随与进程退出来自别的线程，
    /// 任何 <c>Control</c> 属性在非 UI 线程上读写都可能抛异常或造成随机崩溃。</item>
    /// </list>
    /// </remarks>
    internal sealed partial class ServerHostForm
    {
        /// <summary>关闭窗体时是否跳过"服务器还在跑"的询问（停止后再关用的）。</summary>
        private bool m_SkipClosePrompt;

        /// <summary>浏览并切换服务器目录。</summary>
        private void OnBrowseClicked(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择包含 RaidDemoServer.exe 的目录",
                SelectedPath = m_Layout.Directory,
                ShowNewFolderButton = false,
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                LoadFromDirectory(dialog.SelectedPath);
            }
        }

        /// <summary>在资源管理器里打开服务器目录。</summary>
        private void OnOpenDirectoryClicked(object sender, EventArgs e)
        {
            OpenDirectory(m_Layout.Directory);
        }

        /// <summary>保存配置。</summary>
        private void OnSaveClicked(object sender, EventArgs e)
        {
            if (TrySaveConfig(out var message))
            {
                Report(message);
                return;
            }

            ShowProblem(message);
        }

        /// <summary>把界面上的值恢复成内置默认（不立即落盘）。</summary>
        private void OnRestoreDefaultsClicked(object sender, EventArgs e)
        {
            PopulateFromDocument(ServerConfigStore.CreateDefault());
            Report("已恢复默认值：点「保存配置」或直接启动即可写进文件。");
        }

        /// <summary>启动服务器（先自动保存配置）。</summary>
        private void OnStartClicked(object sender, EventArgs e)
        {
            if (m_Launcher.IsRunning)
            {
                Report("服务器已经在运行。");
                return;
            }

            if (!TrySaveConfig(out var message))
            {
                ShowProblem(message);
                return;
            }

            if (!m_Launcher.TryStart(m_Layout, out var startError))
            {
                ShowProblem(startError);
                return;
            }

            m_LogBox.Clear();
            m_Tailer?.Rewind();
            m_DashboardLabel.Text = "状态页：等待服务器就绪…";
            Report($"服务器已启动（进程号 {m_Launcher.ProcessId}），日志见 {ServerLayout.LogDirectoryName}"
                + $"/{ServerLayout.LogFileName}，{message}");
        }

        /// <summary>停止服务器。</summary>
        private async void OnStopClicked(object sender, EventArgs e)
        {
            await StopServerAsync().ConfigureAwait(true);
        }

        /// <summary>复制连接信息（本机 + 局域网地址）。</summary>
        private void OnCopyConnectionClicked(object sender, EventArgs e)
        {
            if (!TryReadDocument(out var document, out var error))
            {
                ShowProblem(error);
                return;
            }

            var port = ServerConfigStore.EffectiveInteger(document.port, ServerConfigDocument.DefaultPort);
            var text = BuildConnectionInfo(port);

            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception exception)
            {
                ShowProblem($"复制失败：{exception.Message}");
                return;
            }

            Report($"已复制连接信息：{text.Replace(Environment.NewLine, "  ")}");
        }

        /// <summary>打开日志目录（顺便把它建出来，空目录也是有用的信息）。</summary>
        private void OnOpenLogDirectoryClicked(object sender, EventArgs e)
        {
            if (!ServerLayout.EnsureDirectory(m_Layout.LogDirectory, out var error))
            {
                ShowProblem(error);
                return;
            }

            OpenDirectory(m_Layout.LogDirectory);
        }

        /// <summary>用浏览器打开状态页。</summary>
        private void OnOpenDashboardClicked(object sender, EventArgs e)
        {
            if (!ServerConfigValidation.TryParseInteger(m_DashboardPortBox.Text, out var port))
            {
                ShowProblem("状态页端口需要整数。");
                return;
            }

            var url = ServerLayout.DashboardUrl(port);
            if (url == null)
            {
                ShowProblem("状态页已关闭（状态页端口 = 0）。把它改成 1~65535 之间再启动服务器即可打开。");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                ShowProblem($"打开状态页失败：{exception.Message}");
            }
        }

        /// <summary>状态页轮询（每 2 秒一次）。</summary>
        private async void OnDashboardTick(object sender, EventArgs e)
        {
            if (m_Polling || !m_Interactive)
            {
                return;
            }

            if (!ServerConfigValidation.TryParseInteger(m_DashboardPortBox.Text, out var port))
            {
                m_DashboardLabel.Text = "状态页：端口不是整数，先修好配置。";
                return;
            }

            var url = ServerLayout.DashboardUrl(port);
            if (url == null)
            {
                m_DashboardLabel.Text = "状态页：已关闭（状态页端口 = 0）。";
                return;
            }

            m_Polling = true;
            try
            {
                var status = await DashboardClient.TryFetchStatusAsync(url).ConfigureAwait(true);
                UpdateDashboardLabel(status);
            }
            finally
            {
                m_Polling = false;
            }
        }

        /// <summary>服务器进程退出（后台线程 → UI 线程）。</summary>
        private void OnServerExited(object sender, EventArgs e)
        {
            RunOnInterfaceThread(() =>
            {
                m_LastMessage = "服务器进程已退出（原因见日志）；存档已按服务器自己的节奏落盘。";
                UpdateStatus();
                m_DashboardLabel.Text = "状态页：服务器已退出。";
            });
        }

        /// <summary>日志有新内容（后台线程 → UI 线程）。</summary>
        private void OnLogTextAppended(string text)
        {
            RunOnInterfaceThread(() => AppendLog(text));
        }

        /// <summary>关闭窗体：服务器还在跑就问一次。</summary>
        private async void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (m_SkipClosePrompt || !m_Launcher.IsRunning)
            {
                return;
            }

            var answer = MessageBox.Show(
                this,
                "服务器还在运行。停止它再关闭面板吗？",
                "关闭服务器面板",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == DialogResult.No)
            {
                return;
            }

            e.Cancel = true;
            await StopServerAsync().ConfigureAwait(true);
            m_SkipClosePrompt = true;
            Close();
        }

        /// <summary>保存配置并返回一句提示。</summary>
        private bool TrySaveConfig(out string message)
        {
            message = null;
            if (!TryReadDocument(out var document, out var error))
            {
                message = $"配置有误：{error}";
                return false;
            }

            if (!ServerConfigStore.TrySave(m_Layout.ConfigPath, document, out var saveError))
            {
                message = $"保存失败：{saveError}";
                return false;
            }

            m_Document = document;
            message = $"配置已写入 {ServerConfigDocument.FileName}。";
            return true;
        }

        /// <summary>更新状态页读数。</summary>
        private void UpdateDashboardLabel(DashboardStatus status)
        {
            if (status == null)
            {
                var hint = m_Launcher.IsRunning ? "服务器可能还在启动" : "服务器未运行";
                m_DashboardLabel.Text = $"状态页：读不到（{hint}）。";
                return;
            }

            m_DashboardLabel.Text = "状态页：" + status.ToSummary();
        }

        /// <summary>拼接连接信息（本机 + 所有局域网 IPv4）。</summary>
        private static string BuildConnectionInfo(int port)
        {
            var lines = new List<string> { $"本机：127.0.0.1:{port}" };

            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(address)
                        && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        lines.Add($"局域网：{address}:{port}");
                    }
                }
            }
            catch (Exception)
            {
                // 拿不到网卡地址不影响"本机"那条：局域网的人自己知道该用什么地址。
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>往日志框追加文本并裁剪历史。</summary>
        private void AppendLog(string text)
        {
            if (m_LogBox.TextLength > MaxLogCharacters)
            {
                var current = m_LogBox.Text;
                m_LogBox.Text = current.Substring(Math.Max(0, current.Length - TrimmedLogCharacters));
            }

            m_LogBox.AppendText(text);
            m_LogBox.SelectionStart = m_LogBox.TextLength;
            m_LogBox.ScrollToCaret();
        }

        /// <summary>在 UI 线程上跑一段代码（窗体已销毁则安静丢弃）。</summary>
        private void RunOnInterfaceThread(Action action)
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // 窗体正在关闭：日志与状态已经不重要了。
            }
        }

        /// <summary>用资源管理器打开一个目录。</summary>
        private void OpenDirectory(string directory)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true,
                });
            }
            catch (Exception exception)
            {
                ShowProblem($"打开目录失败：{exception.Message}");
            }
        }

        /// <summary>提示问题：状态行 + 弹窗（问题不写进弹窗就等于没提示）。</summary>
        private void ShowProblem(string message)
        {
            Report(message);
            MessageBox.Show(this, message, "服务器面板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>窗体销毁时释放跟随器与定时器。</summary>
        private void ReleaseResources()
        {
            m_DashboardTimer?.Stop();
            m_DashboardTimer?.Dispose();
            m_Tailer?.Dispose();
            m_Launcher.Dispose();
        }
    }
}
