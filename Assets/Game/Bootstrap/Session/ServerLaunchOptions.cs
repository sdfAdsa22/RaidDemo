using System;
using System.Collections.Generic;
using RaidDemo.Kernel;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器启动参数：把命令行解析成结构化配置。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独做一层解析：</b>服务器要以无头方式启动，唯一的输入就是命令行。
    /// 把解析逻辑从 MonoBehaviour 里剥出来之后，它变成纯 C# 对象，可以在 EditMode 测试里
    /// 直接覆盖各种参数组合（含非法值），而不需要真的启动一个服务器进程。</para>
    ///
    /// <para><b>约束：</b>存档目录只接受**相对路径**，与本工程「源码与配置禁止绝对路径」的规则一致
    /// （见 00 文档第 3 章）。绝对路径会让服务器在别人的机器上无法启动。</para>
    ///
    /// <para>Unity 自带的参数（<c>-batchmode</c>、<c>-nographics</c>、<c>-logFile</c> 等）
    /// 本类只读取其中与本项目相关的部分，其余一律忽略，避免与引擎行为冲突。</para>
    /// </remarks>
    public sealed class ServerLaunchOptions
    {
        /// <summary>默认监听端口。与 <c>Docs/Modules/10_联机.md</c> 第 15.2 节的端口规划一致。</summary>
        public const int DefaultPort = 7777;

        /// <summary>默认房间名。房主创建房间时可以覆盖。</summary>
        public const string DefaultRoomName = "默认房间";

        /// <summary>默认存档目录（相对服务器进程的工作目录）。</summary>
        public const string DefaultSaveDirectory = "server_saves";

        /// <summary>房间名长度上限，防止超长字符串进入日志与界面。</summary>
        public const int MaxRoomNameLength = 24;

        private readonly List<string> m_Warnings = new List<string>();

        /// <summary>命令行里是否出现 <c>-server</c>。为 false 时本进程是普通客户端。</summary>
        public bool IsServerRequested { get; private set; }

        /// <summary>监听端口（UDP）。</summary>
        public int Port { get; private set; } = DefaultPort;

        /// <summary>房间名。</summary>
        public string RoomName { get; private set; } = DefaultRoomName;

        /// <summary>服务端存档目录（相对路径）。</summary>
        public string SaveDirectory { get; private set; } = DefaultSaveDirectory;

        /// <summary>最低日志等级。</summary>
        public LogLevel MinimumLogLevel { get; private set; } = LogLevel.Info;

        /// <summary>是否以无头方式启动（<c>-batchmode</c> 或 <c>-nographics</c>）。</summary>
        public bool IsHeadless { get; private set; }

        /// <summary>解析过程中的非致命提示（例如参数被忽略的原因），供启动日志打印。</summary>
        public IReadOnlyList<string> Warnings => m_Warnings;

        /// <summary>
        /// 解析命令行参数。
        /// </summary>
        /// <param name="args">命令行参数（通常是 <see cref="Environment.GetCommandLineArgs"/>）。</param>
        /// <param name="options">解析结果。解析失败时为 null。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>参数合法时返回 true。非服务器启动同样返回 true（只是 <see cref="IsServerRequested"/> 为 false）。</returns>
        /// <remarks>
        /// 只有「本类认识的参数取值非法」才算失败：端口不是数字、存档目录是绝对路径等。
        /// 完全不认识的参数一律忽略——引擎会往命令行里塞大量自有参数。
        /// </remarks>
        public static bool TryParse(string[] args, out ServerLaunchOptions options, out string error)
        {
            options = null;
            error = null;

            var result = new ServerLaunchOptions();
            var list = args ?? Array.Empty<string>();

            for (var i = 0; i < list.Length; i++)
            {
                var arg = list[i];
                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                switch (arg)
                {
                    case "-server":
                    case "--server":
                        result.IsServerRequested = true;
                        break;

                    case "-batchmode":
                    case "-nographics":
                        result.IsHeadless = true;
                        break;

                    case "-port":
                        if (!TryReadValue(list, ref i, arg, out var portText, out error))
                        {
                            return false;
                        }

                        if (!int.TryParse(portText, out var port) || port < 1 || port > 65535)
                        {
                            error = $"参数 {arg} 需要 1~65535 之间的端口号，实际收到「{portText}」。";
                            return false;
                        }

                        result.Port = port;
                        break;

                    case "-room":
                        if (!TryReadValue(list, ref i, arg, out var roomText, out error))
                        {
                            return false;
                        }

                        var room = roomText.Trim();
                        if (room.Length == 0)
                        {
                            error = $"参数 {arg} 不能为空。";
                            return false;
                        }

                        if (room.Length > MaxRoomNameLength)
                        {
                            error = $"参数 {arg} 最长 {MaxRoomNameLength} 个字符，实际 {room.Length} 个。";
                            return false;
                        }

                        result.RoomName = room;
                        break;

                    case "-saveDir":
                        if (!TryReadValue(list, ref i, arg, out var saveDir, out error))
                        {
                            return false;
                        }

                        if (!IsRelativeDirectory(saveDir))
                        {
                            error = $"参数 {arg} 只接受相对路径（不得以盘符、斜杠开头，也不得包含 .. ），实际收到「{saveDir}」。";
                            return false;
                        }

                        result.SaveDirectory = saveDir;
                        break;

                    case "-logLevel":
                        if (!TryReadValue(list, ref i, arg, out var levelText, out error))
                        {
                            return false;
                        }

                        if (!TryParseLogLevel(levelText, out var level))
                        {
                            error = $"参数 {arg} 需要 verbose / info / warning / error 之一，实际收到「{levelText}」。";
                            return false;
                        }

                        result.MinimumLogLevel = level;
                        break;
                }
            }

            if (!result.IsServerRequested && result.IsHeadless)
            {
                result.m_Warnings.Add("检测到无头启动参数，但缺少 -server：本进程仍按客户端模式初始化。");
            }

            options = result;
            return true;
        }

        /// <summary>把解析结果整理成一行摘要，供服务器启动日志打印。</summary>
        public string Describe()
        {
            var mode = IsServerRequested ? "专用服务器" : "客户端";
            return $"{mode} ｜ 端口 {Port} ｜ 房间「{RoomName}」｜ 存档目录 {SaveDirectory} ｜ 日志 {MinimumLogLevel} ｜ 无头 {IsHeadless}";
        }

        /// <summary>读取紧跟开关后面的取值。</summary>
        private static bool TryReadValue(
            string[] args,
            ref int index,
            string switchName,
            out string value,
            out string error)
        {
            error = null;
            value = null;

            if (index + 1 >= args.Length)
            {
                error = $"参数 {switchName} 缺少取值。";
                return false;
            }

            index++;
            value = args[index];
            return true;
        }

        /// <summary>
        /// 判断是否是不含盘符与上跳的相对目录。
        /// </summary>
        /// <remarks>
        /// 不直接用 <see cref="System.IO.Path.IsPathRooted"/>：在 Linux 上运行时，
        /// Windows 风格的盘符路径（盘符后紧跟斜杠）不会被判定为绝对路径。
        /// 这里显式拒绝盘符、前导斜杠与上跳段，
        /// 保证同一份配置在 Windows 与 Linux 服务器上含义一致。
        /// </remarks>
        private static bool IsRelativeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var trimmed = path.Trim();
            if (trimmed.StartsWith("/", StringComparison.Ordinal)
                || trimmed.StartsWith("\\", StringComparison.Ordinal)
                || trimmed.Contains(":"))
            {
                return false;
            }

            var segments = trimmed.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return false;
            }

            foreach (var segment in segments)
            {
                if (segment == "..")
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>解析日志等级，接受大小写混合写法。</summary>
        private static bool TryParseLogLevel(string text, out LogLevel level)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "verbose":
                    level = LogLevel.Verbose;
                    return true;
                case "info":
                    level = LogLevel.Info;
                    return true;
                case "warning":
                case "warn":
                    level = LogLevel.Warning;
                    return true;
                case "error":
                    level = LogLevel.Error;
                    return true;
                default:
                    level = LogLevel.Info;
                    return false;
            }
        }
    }
}
