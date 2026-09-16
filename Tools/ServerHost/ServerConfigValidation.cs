using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RaidDemo.Kernel.Server;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 面板侧的配置校验。
    /// </summary>
    /// <remarks>
    /// <para><b>权威定义在游戏侧：</b>下面每一条取值范围都抄自
    /// <c>LaunchOptions.Parsing.cs</c>（端口 1~65535、房间名 1~24 字、存档目录必须是相对路径……）。
    /// 面板提前校验的唯一目的，是让错误在点启动之前就出现。服务器在参数非法时会拒绝启动并打印
    /// 中文原因，但那时玩家已经离开面板、面对着一段日志。</para>
    ///
    /// <para><b>未设置的字段跳过校验：</b>手写的配置文件可以只写两三个字段（其余回退内置默认），
    /// 这是游戏明确支持的用法（见 <c>ServerConfigFileTests</c>）。若面板把"缺字段"也判成错误，
    /// 就会出现游戏能启动、面板却说配置有误的分裂。</para>
    /// </remarks>
    internal static class ServerConfigValidation
    {
        /// <summary>日志等级取值（与游戏解析器一致）。</summary>
        private static readonly string[] LogLevels = { "verbose", "info", "warning", "error" };

        /// <summary>房间名最大字符数（与 <c>LaunchOptions.MaxRoomNameLength</c> 一致）。</summary>
        public const int MaxRoomNameLength = 24;

        /// <summary>战局时长上限（秒）。</summary>
        public const float MaxRaidDurationSeconds = 7200f;

        /// <summary>自动开局等待上限（秒）。</summary>
        public const float MaxAutoStartSeconds = 600f;

        /// <summary>掉线宽限下限（秒）。</summary>
        public const float MinGraceSeconds = 5f;

        /// <summary>掉线宽限上限（秒）。</summary>
        public const float MaxGraceSeconds = 600f;

        /// <summary>自愈看门狗下限（秒，0 单独表示关闭）。</summary>
        public const float MinWatchdogSeconds = 1f;

        /// <summary>自愈看门狗上限（秒）。</summary>
        public const float MaxWatchdogSeconds = 30f;

        /// <summary>端口上限。</summary>
        public const int MaxPort = 65535;

        /// <summary>
        /// 校验一份配置，收集全部问题。
        /// </summary>
        /// <param name="document">配置内容。</param>
        /// <param name="errors">错误原因列表（一次列全，便于一次改完）。</param>
        /// <returns>是否合法。</returns>
        public static bool TryValidate(ServerConfigDocument document, out List<string> errors)
        {
            errors = new List<string>();

            if (document == null)
            {
                errors.Add("配置内容为空。");
                return false;
            }

            ValidatePort(errors, "端口", document.port, ServerConfigDocument.UnsetInt, 1);
            ValidatePort(errors, "状态页端口", document.dashboardPort, ServerConfigDocument.UnsetInt, 0);
            ValidatePort(errors, "局域网发现端口", document.discoveryPort, ServerConfigDocument.UnsetInt, 0);
            ValidateRoom(errors, document.room);
            ValidateSaveDirectory(errors, document.saveDir);
            ValidateLogLevel(errors, document.logLevel);
            ValidateRange(errors, "战局时长", document.raidDuration, 0f, MaxRaidDurationSeconds);
            ValidateRange(errors, "自动开局", document.autoStart, 0f, MaxAutoStartSeconds);
            ValidateRange(errors, "掉线宽限", document.grace, MinGraceSeconds, MaxGraceSeconds);
            ValidateWatchdog(errors, document.watchdog);

            return errors.Count == 0;
        }

        /// <summary>
        /// 校验配置并整理成一段可直接显示的原因（多条用分号连接）。
        /// </summary>
        /// <param name="document">配置内容。</param>
        /// <param name="error">失败原因；合法时为 null。</param>
        /// <returns>是否合法。</returns>
        public static bool TryValidateSummary(ServerConfigDocument document, out string error)
        {
            error = null;
            if (TryValidate(document, out var errors))
            {
                return true;
            }

            error = string.Join("；", errors);
            return false;
        }

        /// <summary>端口项：未设置（哨兵值）时跳过。</summary>
        private static void ValidatePort(List<string> errors, string label, int value, int unset, int minimum)
        {
            if (value == unset)
            {
                return;
            }

            if (value < minimum || value > MaxPort)
            {
                var hint = minimum == 0 ? "0~65535 之间的端口号（0 表示关闭）" : "1~65535 之间的端口号";
                errors.Add($"{label}需要 {hint}，实际收到「{value}」。");
            }
        }

        /// <summary>房间名：非空且不超过上限。</summary>
        private static void ValidateRoom(List<string> errors, string room)
        {
            if (string.IsNullOrWhiteSpace(room))
            {
                return;
            }

            var trimmed = room.Trim();
            if (trimmed.Length > MaxRoomNameLength)
            {
                errors.Add($"房间名最长 {MaxRoomNameLength} 个字符，实际 {trimmed.Length} 个。");
            }
        }

        /// <summary>存档目录：只接受相对路径。</summary>
        private static void ValidateSaveDirectory(List<string> errors, string saveDirectory)
        {
            if (string.IsNullOrWhiteSpace(saveDirectory))
            {
                return;
            }

            if (!IsRelativeDirectory(saveDirectory))
            {
                errors.Add($"存档目录只接受相对路径（不得以盘符、斜杠开头，也不得包含 ..），实际收到「{saveDirectory}」。");
            }
        }

        /// <summary>日志等级：四个取值之一。</summary>
        private static void ValidateLogLevel(List<string> errors, string logLevel)
        {
            if (string.IsNullOrWhiteSpace(logLevel))
            {
                return;
            }

            var trimmed = logLevel.Trim();
            foreach (var candidate in LogLevels)
            {
                if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            errors.Add($"日志等级需要 verbose / info / warning / error 之一，实际收到「{trimmed}」。");
        }

        /// <summary>浮点区间：未设置（哨兵值）时跳过；NaN 一律判错。</summary>
        private static void ValidateRange(List<string> errors, string label, float value, float minimum, float maximum)
        {
            if (value == ServerConfigDocument.UnsetFloat)
            {
                return;
            }

            if (float.IsNaN(value) || value < minimum || value > maximum)
            {
                errors.Add(
                    $"{label}需要 {ServerConfigStore.FormatNumber(minimum)}~{ServerConfigStore.FormatNumber(maximum)} 之间的秒数，"
                    + $"实际收到「{ServerConfigStore.FormatNumber(value)}」。");
            }
        }

        /// <summary>看门狗：0（关闭）或 1~30 秒。</summary>
        private static void ValidateWatchdog(List<string> errors, float value)
        {
            if (value == ServerConfigDocument.UnsetFloat)
            {
                return;
            }

            var valid = !float.IsNaN(value)
                && (value == 0f || (value >= MinWatchdogSeconds && value <= MaxWatchdogSeconds));
            if (!valid)
            {
                errors.Add(
                    $"自愈看门狗需要 0（关闭）或 {ServerConfigStore.FormatNumber(MinWatchdogSeconds)}~"
                    + $"{ServerConfigStore.FormatNumber(MaxWatchdogSeconds)} 秒，实际收到「{ServerConfigStore.FormatNumber(value)}」。");
            }
        }

        /// <summary>
        /// 是否是不逃出服务器目录的相对路径（与游戏侧 <c>IsRelativeDirectory</c> 同规则）。
        /// </summary>
        /// <param name="path">用户输入。</param>
        /// <returns>是否可用。</returns>
        public static bool IsRelativeDirectory(string path)
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

        /// <summary>
        /// 解析整数输入。
        /// </summary>
        /// <param name="text">界面文本。</param>
        /// <param name="value">解析结果。</param>
        /// <returns>是否成功。</returns>
        public static bool TryParseInteger(string text, out int value)
        {
            return int.TryParse(
                (text ?? string.Empty).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>
        /// 解析浮点输入。
        /// </summary>
        /// <param name="text">界面文本。</param>
        /// <param name="value">解析结果。</param>
        /// <returns>是否成功。</returns>
        /// <remarks>
        /// 先按不变文化解析（配置文件与命令行里写的是 <c>2.5</c>），失败再按当前区域解析一次：
        /// 某些区域的小数点是逗号，玩家敲 <c>2,5</c> 时也应当被接受。写回文件时永远用不变文化，
        /// 因此不会把逗号带进配置文件。
        /// </remarks>
        public static bool TryParseNumber(string text, out float value)
        {
            var trimmed = (text ?? string.Empty).Trim();
            return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>取文件所在目录（错误提示里要指明是哪个文件出的问题）。</summary>
        /// <param name="path">文件路径。</param>
        /// <returns>目录；没有目录部分时为空串。</returns>
        public static string DirectoryOf(string path)
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
    }
}
