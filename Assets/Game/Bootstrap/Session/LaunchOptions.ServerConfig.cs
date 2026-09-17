using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RaidDemo.Kernel.Server;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器配置文件（<c>server.config.json</c>）的读取与合并。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有这一层：</b>"双击一个 exe 就开服"要求配置能落在文件里，
    /// 而不是只存在于命令行。配置文件让 Windows 面板（<c>Tools/ServerHost</c>）
    /// 与 Linux 启动脚本共用同一份格式，也让人能手改。</para>
    ///
    /// <para><b>合并规则（本类唯一需要记住的事情）：</b></para>
    /// <list type="number">
    /// <item>把配置文件转成"等效命令行"（<c>-port 7777 -room 房间 …</c>）；</item>
    /// <item>把它放在真实命令行**前面**，一起交给同一个解析器；</item>
    /// <item>解析器是"后者覆盖前者"，于是天然得到
    /// <b>命令行 &gt; 配置文件 &gt; 内置默认</b>。</item>
    /// </list>
    ///
    /// <para><b>为什么用"等效命令行"而不是逐字段赋值：</b>逐字段赋值等于把端口范围、房间名长度、
    /// 存档目录必须是相对路径这些规则再抄一遍，抄漏一条就是"命令行拒绝、配置文件放过"的漏洞。
    /// 转成参数后，两条路径共用同一批校验与同一批中文报错。</para>
    ///
    /// <para><b>只有服务器模式才读配置：</b>否则一台机器上放着的服务器配置会悄悄影响客户端
    /// （例如把客户端的日志等级改掉）。显式给了 <c>-config</c> 时例外——那是调用方的明确意图。</para>
    /// </remarks>
    public sealed partial class LaunchOptions
    {
        /// <summary>指定配置文件的参数名。</summary>
        private const string ConfigSwitch = "-config";

        /// <summary>
        /// 解析命令行，并（在服务器模式或显式指定时）合并服务器配置文件。
        /// </summary>
        /// <param name="args">原始命令行参数。</param>
        /// <param name="baseDirectory">
        /// 可执行文件所在目录：既是默认配置文件的查找位置，
        /// 也是 <c>-config</c> 用相对路径时的解析基准。
        /// </param>
        /// <param name="options">解析结果。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否解析成功。</returns>
        /// <remarks>
        /// <para><b>为什么相对路径按"可执行文件所在目录"解析，而不是当前工作目录：</b>
        /// 玩家是双击 exe 或通过面板启动的，工作目录可能是任意位置（快捷方式、计划任务、
        /// 从别的盘符启动）。以 exe 所在目录为基准，"配置就在服务器旁边"这条直觉才成立，
        /// 也才能让面板用一句固定的相对路径把它传进来。</para>
        ///
        /// <para><b>找不到默认配置文件不是错误：</b>没写过配置就该按内置默认启动。
        /// 但显式传了 <c>-config</c> 却找不到文件是**错误**——那说明调用方以为自己配置过，
        /// 静默按默认值启动会让"我明明改了端口却还是 7777"这类问题极难查。</para>
        /// </remarks>
        public static bool TryParseWithServerConfig(
            string[] args,
            string baseDirectory,
            out LaunchOptions options,
            out string error)
        {
            options = null;
            error = null;

            var list = args ?? Array.Empty<string>();
            var hasServerSwitch = ContainsArgument(list, "-server") || ContainsArgument(list, "--server");
            var hasConfigSwitch = TryReadConfigPath(list, out var configArgument, out error);
            if (error != null)
            {
                return false;
            }

            if (!hasServerSwitch && !hasConfigSwitch)
            {
                // 普通客户端 / 单机：配置文件与本进程无关，保持与历史完全一致的行为。
                return TryParseFlat(list, out options, out error);
            }

            var configPath = ResolveConfigPath(configArgument, baseDirectory);
            if (!File.Exists(configPath))
            {
                if (hasConfigSwitch)
                {
                    error = $"找不到配置文件：{configPath}";
                    return false;
                }

                return TryParseFlat(list, out options, out error);
            }

            if (!TryLoadConfigDocument(configPath, out var document, out error))
            {
                return false;
            }

            var configArguments = new List<string>();
            CollectConfigArguments(document, configArguments);

            // 先单独校验配置：这一步失败时，报错要指向"配置文件"，而不是让玩家以为是自己命令行写错了。
            var configOnly = configArguments.ToArray();
            if (!TryParseFlat(configOnly, out _, out var configError))
            {
                error = $"配置文件 {Path.GetFileName(configPath)} 有误：{configError}";
                return false;
            }

            var merged = new List<string>(configArguments.Count + list.Length);
            merged.AddRange(configArguments);
            merged.AddRange(list);

            if (!TryParseFlat(merged.ToArray(), out options, out error))
            {
                return false;
            }

            options.m_Warnings.Add($"已应用服务器配置文件：{configPath}");
            return true;
        }

        /// <summary>数组里是否出现了某个开关（大小写不敏感）。</summary>
        private static bool ContainsArgument(string[] args, string name)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读取 <c>-config</c> 的取值。
        /// </summary>
        /// <param name="args">命令行。</param>
        /// <param name="path">取到的路径；没有该开关时为 null。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否出现了该开关。</returns>
        private static bool TryReadConfigPath(string[] args, out string path, out string error)
        {
            path = null;
            error = null;

            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], ConfigSwitch, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    error = $"参数 {ConfigSwitch} 缺少取值。";
                    return false;
                }

                path = args[i + 1].Trim();
                return true;
            }

            return false;
        }

        /// <summary>把配置里的路径解析成绝对路径。</summary>
        private static string ResolveConfigPath(string argument, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return Path.Combine(baseDirectory ?? string.Empty, ServerConfigDocument.FileName);
            }

            return Path.IsPathRooted(argument)
                ? Path.GetFullPath(argument)
                : Path.GetFullPath(Path.Combine(baseDirectory ?? string.Empty, argument));
        }

        /// <summary>
        /// 读取并反序列化配置文件。
        /// </summary>
        /// <param name="path">配置文件绝对路径。</param>
        /// <param name="document">解析结果。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否成功。</returns>
        /// <remarks>
        /// 解析失败**不降级**：一份写坏的配置应该让服务器起不来并明确报错，
        /// 而不是"安静地用默认值跑起来"——后者会让操作者以为配置生效了。
        /// </remarks>
        private static bool TryLoadConfigDocument(
            string path,
            out ServerConfigDocument document,
            out string error)
        {
            document = null;
            error = null;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                error = $"读取配置文件失败：{path}（{exception.Message}）";
                return false;
            }

            try
            {
                document = JsonUtility.FromJson<ServerConfigDocument>(json) ?? new ServerConfigDocument();
                return true;
            }
            catch (Exception exception)
            {
                error = $"配置文件不是合法 JSON：{path}（{exception.Message}）";
                return false;
            }
        }

        /// <summary>
        /// 把配置文档转成"等效命令行"。
        /// </summary>
        /// <param name="document">配置内容。</param>
        /// <param name="target">参数收集目标。</param>
        /// <remarks>
        /// <para><b>判据是"是否设置"而不是"是否合法"：</b>只要字段不是哨兵值（<c>-1</c> / 空串）就转成参数，
        /// 哪怕取值超范围也照样交给解析器——那样得到的是一句明确的中文报错
        /// （"需要 1~65535 之间的端口号"），而不是"我写的 0 被悄悄忽略了"。</para>
        ///
        /// <para><b>数字一律用不随地区变化的格式：</b>某些地区的小数点是逗号，
        /// 按默认格式写出 <c>2,5</c> 会被解析器当成非法值——一个纯粹由"运行在哪台机器上"决定的故障。</para>
        /// </remarks>
        private static void CollectConfigArguments(ServerConfigDocument document, List<string> target)
        {
            if (document == null)
            {
                return;
            }

            AddInteger(target, "-port", document.port);
            AddText(target, "-room", document.room);
            AddText(target, "-saveDir", document.saveDir);
            AddText(target, "-logLevel", document.logLevel);
            AddText(target, "-map", document.map);
            AddNumber(target, "-raidDuration", document.raidDuration);
            AddNumber(target, "-autostart", document.autoStart);
            AddInteger(target, "-dashboardPort", document.dashboardPort);
            AddText(target, "-adminToken", document.adminToken);
            AddInteger(target, "-discoveryPort", document.discoveryPort);
            AddNumber(target, "-grace", document.grace);
            AddNumber(target, "-watchdog", document.watchdog);
        }

        /// <summary>整数项：不是哨兵值就转成参数。</summary>
        private static void AddInteger(List<string> target, string name, int value)
        {
            if (value == ServerConfigDocument.UnsetInt)
            {
                return;
            }

            target.Add(name);
            target.Add(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>浮点项：不是哨兵值就转成参数。</summary>
        private static void AddNumber(List<string> target, string name, float value)
        {
            if (value == ServerConfigDocument.UnsetFloat)
            {
                return;
            }

            target.Add(name);
            target.Add(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>文本项：非空白才转成参数（空字符串等价于"没写"）。</summary>
        private static void AddText(List<string> target, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            target.Add(name);
            target.Add(value.Trim());
        }
    }
}
