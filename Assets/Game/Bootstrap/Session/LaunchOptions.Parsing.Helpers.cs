using System;
using RaidDemo.Kernel;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 命令行解析的辅助方法：取值、目录校验、日志等级解析。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件（P-51 拆分的）：</b>解析主体（<c>LaunchOptions.Parsing.cs</c>）是一条
    /// 长长的 switch，每个 case 都调用这里的取值与校验；把辅助方法挪出来之后，
    /// 新增参数只需要读"开关 → 取值 → 校验 → 落字段"这四步，而不用先翻过四十行工具函数。</para>
    /// </remarks>
    public sealed partial class LaunchOptions
    {
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
