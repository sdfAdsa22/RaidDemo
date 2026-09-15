using System;
using System.IO;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 启动器日志：同时写控制台（如果有）与安装目录下的滚动日志文件。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要落文件：</b>更新失败几乎都发生在"玩家的机器上"，
    /// 而玩家能提供给你的信息只有一句"更新失败"。日志文件让排障从"猜"变成"看"。</para>
    ///
    /// <para><b>为什么不无限增长：</b>超过阈值就整体轮转一次（.log → .log.1），
    /// 只保留一代。演示与自用场景不需要更复杂的策略，而"日志把磁盘写满"是真实存在的故障。</para>
    /// </remarks>
    public sealed class LauncherLog
    {
        /// <summary>单个日志文件的体积上限（2 MB）。</summary>
        private const long MaxBytes = 2 * 1024 * 1024;

        private readonly string m_Path;
        private readonly object m_Gate = new object();

        /// <summary>创建日志器。</summary>
        /// <param name="logFilePath">日志文件路径。</param>
        public LauncherLog(string logFilePath)
        {
            m_Path = logFilePath;
        }

        /// <summary>追加一行日志。</summary>
        /// <param name="message">内容。</param>
        public void Write(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            lock (m_Gate)
            {
                try
                {
                    var directory = Path.GetDirectoryName(m_Path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    RotateIfNeeded();
                    File.AppendAllText(m_Path, line + Environment.NewLine);
                }
                catch (Exception)
                {
                    // 日志写不进去不能影响更新本身（例如磁盘只读），静默忽略。
                }
            }
        }

        /// <summary>超过上限时轮转一次。</summary>
        private void RotateIfNeeded()
        {
            if (!File.Exists(m_Path) || new FileInfo(m_Path).Length < MaxBytes)
            {
                return;
            }

            var backupPath = m_Path + ".1";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(m_Path, backupPath);
        }
    }
}
