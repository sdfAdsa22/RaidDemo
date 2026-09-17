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

        /// <summary>解析 <c>-grace</c>：掉线宽限时长。</summary>
        /// <param name="args">参数数组。</param>
        /// <param name="index">当前下标（会前进到取值）。</param>
        /// <param name="switchName">开关名（用于报错）。</param>
        /// <param name="target">解析结果。</param>
        /// <param name="error">失败原因。</param>
        /// <remarks>从解析主体里抽出来（文件 400 行上限）：这类"取值 → 校验 → 落字段"的参数块
        /// 放进辅助文件后，主 switch 里新增参数只剩三行。</remarks>
        private static bool TryApplyReconnectGrace(
            string[] args,
            ref int index,
            string switchName,
            LaunchOptions target,
            out string error)
        {
            error = null;
            if (!TryReadValue(args, ref index, switchName, out var text, out error))
            {
                return false;
            }

            if (!float.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds)
                || seconds < MinReconnectGraceSeconds
                || seconds > MaxReconnectGraceSeconds)
            {
                error = $"参数 {switchName} 需要 {MinReconnectGraceSeconds:F0}~"
                        + $"{MaxReconnectGraceSeconds:F0} 秒之间的时长，实际收到「{text}」。";
                return false;
            }

            target.ReconnectGraceSeconds = seconds;
            return true;
        }

        /// <summary>解析 <c>-watchdog</c>：传输层自愈判定时长（0 表示关闭）。</summary>
        private static bool TryApplyTransportWatchdog(
            string[] args,
            ref int index,
            string switchName,
            LaunchOptions target,
            out string error)
        {
            error = null;
            if (!TryReadValue(args, ref index, switchName, out var text, out error))
            {
                return false;
            }

            if (!float.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds)
                || (seconds != 0f
                    && (seconds < MinTransportWatchdogSeconds
                        || seconds > MaxTransportWatchdogSeconds)))
            {
                error = $"参数 {switchName} 需要 0（关闭）或 {MinTransportWatchdogSeconds:F0}~"
                        + $"{MaxTransportWatchdogSeconds:F0} 秒之间的时长，实际收到「{text}」。";
                return false;
            }

            target.TransportWatchdogSeconds = seconds;
            return true;
        }

        /// <summary>读取一个"不能为空"的字符串参数；空白串按缺值处理。</summary>
        /// <param name="args">参数数组。</param>
        /// <param name="index">当前下标（会前进到取值）。</param>
        /// <param name="switchName">开关名（用于报错）。</param>
        /// <param name="value">去掉首尾空白后的取值；失败时为 null。</param>
        /// <param name="error">失败原因。</param>
        /// <remarks>连接地址与预填地址都要求"非空"：空串会让界面显示一个没法用的输入框，
        /// 或让 <c>-connect</c> 静默退化成"不连接却以为连上了"，都在这里一次拦掉。</remarks>
        private static bool TryReadNonEmptyValue(
            string[] args,
            ref int index,
            string switchName,
            out string value,
            out string error)
        {
            value = null;
            if (!TryReadValue(args, ref index, switchName, out var raw, out error))
            {
                return false;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                error = $"参数 {switchName} 不能为空。";
                return false;
            }

            value = trimmed;
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
