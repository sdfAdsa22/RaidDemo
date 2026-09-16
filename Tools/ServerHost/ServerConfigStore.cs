using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RaidDemo.Kernel.Server;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// <c>server.config.json</c> 的读写与"生效值"展示。
    /// </summary>
    /// <remarks>
    /// <para><b>与游戏侧的边界：</b>游戏读这份文件（<c>LaunchOptions.TryParseWithServerConfig</c>）
    /// 并把每一条合并进启动参数；面板只负责"写得让游戏读得懂"。因此这里的默认值一律取自
    /// <see cref="ServerConfigDocument"/> 的常量，不另抄一份——抄一份就必然出现
    /// "面板显示 7777、服务器实际监听 8080"这种看不见的错位。</para>
    ///
    /// <para><b>为什么写文件用"临时文件 + 替换"：</b>玩家按保存的瞬间如果断电或磁盘满，
    /// 直接覆盖会留下一份被截断的 JSON，而服务器对损坏配置的处理是拒绝启动（这是刻意的，
    /// 见 LaunchOptions 的注释）。用临时文件写完再替换，再加上一份 <c>.bak</c>，
    /// 才能保证任何时刻磁盘上都有一份完整可用的配置。</para>
    /// </remarks>
    internal static class ServerConfigStore
    {
        /// <summary>备份文件后缀（保存前把旧文件挪到这里）。</summary>
        public const string BackupSuffix = ".bak";

        /// <summary>JSON 序列化选项。</summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><c>IncludeFields</c>：模型用的是公开字段（Unity 的 <c>JsonUtility</c> 只认字段），
        /// 默认设置只序列化属性，会写出一个空的 <c>{}</c>；</item>
        /// <item><c>UnsafeRelaxedJsonEscaping</c>：中文房间名要按原样写进文件——这份文件是给人手改的，
        /// 转义成 <c>\uXXXX</c> 就没法用来核对了；</item>
        /// <item><c>WriteIndented</c>：字段一行一个，diff 与手改都清楚。</item>
        /// </list>
        /// </remarks>
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// 生成一份"全部字段都写实"的默认配置。
        /// </summary>
        /// <returns>默认配置文档。</returns>
        /// <remarks>
        /// 刻意不用哨兵值：面板生成的文件是给人看与给人改的，
        /// 满篇 <c>-1</c> 会让人以为配置坏了；服务器对"等于默认值"与"未设置"的处理完全一致，
        /// 因此写实不改变任何行为。
        /// </remarks>
        public static ServerConfigDocument CreateDefault()
        {
            return new ServerConfigDocument
            {
                port = ServerConfigDocument.DefaultPort,
                room = ServerConfigDocument.DefaultRoomName,
                saveDir = ServerConfigDocument.DefaultSaveDirectory,
                logLevel = ServerConfigDocument.DefaultLogLevel,
                map = ServerConfigDocument.DefaultMapScene,
                raidDuration = ServerConfigDocument.DefaultRaidDurationSeconds,
                autoStart = ServerConfigDocument.DefaultAutoStartSeconds,
                dashboardPort = ServerConfigDocument.DefaultDashboardPort,
                discoveryPort = ServerConfigDocument.DefaultDiscoveryPort,
                grace = ServerConfigDocument.DefaultReconnectGraceSeconds,
                watchdog = ServerConfigDocument.DefaultTransportWatchdogSeconds,
            };
        }

        /// <summary>
        /// 读取配置。
        /// </summary>
        /// <param name="path">配置文件绝对路径。</param>
        /// <param name="document">解析结果；文件不存在时为默认配置。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否成功。</returns>
        /// <remarks>
        /// "文件不存在"不是错误（服务器在同样情况下也按内置默认值启动），
        /// 但"文件存在却读不懂"是错误：静默回退默认值会让玩家以为自己的修改生效了。
        /// </remarks>
        public static bool TryLoad(string path, out ServerConfigDocument document, out string error)
        {
            document = CreateDefault();
            error = null;

            if (!File.Exists(path))
            {
                return true;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                error = $"读取失败：{exception.Message}";
                return false;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<ServerConfigDocument>(json, SerializerOptions);
                if (loaded != null)
                {
                    document = loaded;
                }

                return true;
            }
            catch (JsonException exception)
            {
                error = $"不是合法 JSON：{exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// 写配置（先写临时文件，再替换，并留下 <c>.bak</c>）。
        /// </summary>
        /// <param name="path">配置文件绝对路径。</param>
        /// <param name="document">要写入的配置。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否成功。</returns>
        public static bool TrySave(string path, ServerConfigDocument document, out string error)
        {
            error = null;
            var temporary = path + ".tmp";

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // UTF-8 无 BOM：Unity 的 JsonUtility 与大多数编辑器都认，而 BOM 在某些
                // 命令行工具里会被当成 JSON 的第一个字符，导致"文件看着没错却解析失败"。
                File.WriteAllText(
                    temporary,
                    JsonSerializer.Serialize(document, SerializerOptions),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, path + BackupSuffix, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, path);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"写入失败：{exception.Message}";
                TryDelete(temporary);
                return false;
            }
        }

        /// <summary>
        /// 在指定目录里写出默认配置（打包脚本用的 write-default-config 模式）。
        /// </summary>
        /// <param name="directory">目标目录，不存在则创建。</param>
        /// <param name="path">写出的文件绝对路径。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否成功。</returns>
        public static bool TryWriteDefault(string directory, out string path, out string error)
        {
            path = Path.Combine(directory, ServerConfigDocument.FileName);

            if (!ServerLayout.EnsureDirectory(directory, out error))
            {
                return false;
            }

            return TrySave(path, CreateDefault(), out error);
        }

        /// <summary>
        /// 把配置整理成"生效值"清单（校验模式的输出，也是界面状态栏的来源）。
        /// </summary>
        /// <param name="layout">服务器目录布局。</param>
        /// <param name="document">配置内容。</param>
        /// <returns>逐行文本。</returns>
        /// <remarks>
        /// 未设置的字段要显示默认值而不是哨兵值：玩家看的是"服务器实际会用哪套值"，
        /// 把 <c>-1</c> 打出来只会让人以为哪里错了。
        /// </remarks>
        public static string Describe(ServerLayout layout, ServerConfigDocument document)
        {
            var lines = new List<string>
            {
                $"服务器目录：{layout.Directory}",
                $"配置文件：{layout.ConfigPath}（{(layout.HasConfig ? "已存在" : "不存在，按内置默认值")}）",
                layout.HasExecutable
                    ? $"服务器程序：存在（{layout.ExecutablePath}）"
                    : $"服务器程序：{ServerLayout.ExecutableName} 缺失（{layout.ExecutablePath}）",
                $"端口：{EffectiveInteger(document.port, ServerConfigDocument.DefaultPort)}",
                $"房间：{EffectiveText(document.room, ServerConfigDocument.DefaultRoomName)}",
                $"存档目录：{EffectiveText(document.saveDir, ServerConfigDocument.DefaultSaveDirectory)}",
                $"地图：{EffectiveText(document.map, ServerConfigDocument.DefaultMapScene)}",
                $"日志等级：{EffectiveText(document.logLevel, ServerConfigDocument.DefaultLogLevel)}",
                $"战局时长上限：{FormatNumber(EffectiveNumber(document.raidDuration, ServerConfigDocument.DefaultRaidDurationSeconds))} 秒",
                $"自动开局：{FormatNumber(EffectiveNumber(document.autoStart, ServerConfigDocument.DefaultAutoStartSeconds))} 秒",
                $"状态页端口：{EffectiveInteger(document.dashboardPort, ServerConfigDocument.DefaultDashboardPort)}",
                $"局域网发现端口：{EffectiveInteger(document.discoveryPort, ServerConfigDocument.DefaultDiscoveryPort)}",
                $"掉线宽限：{FormatNumber(EffectiveNumber(document.grace, ServerConfigDocument.DefaultReconnectGraceSeconds))} 秒",
                $"自愈看门狗：{FormatNumber(EffectiveNumber(document.watchdog, ServerConfigDocument.DefaultTransportWatchdogSeconds))} 秒",
            };

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>整数：哨兵值换成默认值。</summary>
        public static int EffectiveInteger(int value, int fallback)
        {
            return value == ServerConfigDocument.UnsetInt ? fallback : value;
        }

        /// <summary>浮点：哨兵值换成默认值。</summary>
        public static float EffectiveNumber(float value, float fallback)
        {
            return value == ServerConfigDocument.UnsetFloat ? fallback : value;
        }

        /// <summary>文本：空白换成默认值（与游戏"空串等价于没写"的规则一致）。</summary>
        public static string EffectiveText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary>数字转文本（不变文化，去掉多余小数：480 而不是 480.0）。</summary>
        public static string FormatNumber(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>删除文件，忽略失败。</summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 清理临时文件失败不影响结论：调用方拿到的是"保存失败"这个事实。
            }
        }
    }
}
