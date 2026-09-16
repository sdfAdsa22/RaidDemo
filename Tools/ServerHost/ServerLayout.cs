using System;
using System.IO;
using RaidDemo.Kernel.Server;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 服务器目录里几个固定位置的约定（可执行文件、配置文件、日志、状态页地址）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要单独一个类型：</b>"配置在哪、日志在哪、连什么地址"这三件事
    /// 会同时被界面、启动器与验收脚本使用。散落成各处拼接的字符串时，改动一处（例如日志目录
    /// 从 <c>Logs</c> 改成 <c>logs</c>）会让三处各自漂移，而 Windows 的文件系统不区分大小写，
    /// 这类漂移在开发机上永远看不出来。</para>
    ///
    /// <para>命名与 Linux 侧 <c>deploy/server.sh</c> 完全一致：同一个包在两种系统上
    /// 用同样的相对位置，排查问题时"看日志"这条指令不需要分平台。</para>
    /// </remarks>
    internal sealed class ServerLayout
    {
        /// <summary>Windows 专用服务器可执行文件名（与 ServerBuildMenu 一致）。</summary>
        public const string ExecutableName = "RaidDemoServer.exe";

        /// <summary>日志目录名。</summary>
        public const string LogDirectoryName = "Logs";

        /// <summary>日志文件名。</summary>
        public const string LogFileName = "server.log";

        /// <summary>服务器目录（绝对路径，末尾无分隔符）。</summary>
        public string Directory { get; private set; }

        /// <summary>服务器可执行文件绝对路径。</summary>
        public string ExecutablePath
        {
            get { return Path.Combine(Directory, ExecutableName); }
        }

        /// <summary>配置文件绝对路径。</summary>
        public string ConfigPath
        {
            get { return Path.Combine(Directory, ServerConfigDocument.FileName); }
        }

        /// <summary>日志目录绝对路径。</summary>
        public string LogDirectory
        {
            get { return Path.Combine(Directory, LogDirectoryName); }
        }

        /// <summary>日志文件绝对路径。</summary>
        public string LogFilePath
        {
            get { return Path.Combine(LogDirectory, LogFileName); }
        }

        /// <summary>服务器程序是否存在。</summary>
        public bool HasExecutable
        {
            get { return File.Exists(ExecutablePath); }
        }

        /// <summary>配置文件是否存在。</summary>
        public bool HasConfig
        {
            get { return File.Exists(ConfigPath); }
        }

        /// <summary>
        /// 用目录构造布局。
        /// </summary>
        /// <param name="directory">服务器目录（可为相对路径）。</param>
        /// <returns>布局对象。</returns>
        public static ServerLayout FromDirectory(string directory)
        {
            var resolved = string.IsNullOrWhiteSpace(directory)
                ? PanelDirectory()
                : Path.GetFullPath(directory.Trim());

            return new ServerLayout { Directory = resolved.TrimEnd(Path.DirectorySeparatorChar) };
        }

        /// <summary>
        /// 面板自身所在目录。
        /// </summary>
        /// <returns>绝对路径。</returns>
        /// <remarks>
        /// 打包后面板与服务器程序同目录，因此"没带参数 = 管好我旁边这台服务器"是最常见用法；
        /// 用当前工作目录会让双击启动（工作目录可能是 C:\Windows\System32）指向别处。
        /// </remarks>
        public static string PanelDirectory()
        {
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }

        /// <summary>状态页根地址；未配置状态页时为 null。</summary>
        /// <param name="dashboardPort">状态页端口（0 表示关闭）。</param>
        /// <returns>形如 <c>http://127.0.0.1:8080/</c> 的地址。</returns>
        public static string DashboardUrl(int dashboardPort)
        {
            return dashboardPort <= 0 ? null : $"http://127.0.0.1:{dashboardPort}/";
        }

        /// <summary>拷贝配置与日志时用到的目录是否可用（不存在则创建）。</summary>
        /// <param name="directory">目标目录。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否可用。</returns>
        public static bool EnsureDirectory(string directory, out string error)
        {
            error = null;

            try
            {
                System.IO.Directory.CreateDirectory(directory);
                return true;
            }
            catch (Exception exception)
            {
                error = $"无法创建目录：{directory}（{exception.Message}）";
                return false;
            }
        }
    }
}
