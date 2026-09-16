using System;
using System.IO;

namespace RaidDemo.ServerHost
{
    /// <summary>面板的运行形态。</summary>
    internal enum HostMode
    {
        /// <summary>图形界面（玩家用法）。</summary>
        UserInterface,

        /// <summary>写出默认配置（打包脚本用法）。</summary>
        WriteDefaultConfig,

        /// <summary>校验并打印生效配置（验收脚本用法）。</summary>
        Check,

        /// <summary>把窗体离屏渲染成 PNG（界面自检用法）。</summary>
        Render,

        /// <summary>打印帮助。</summary>
        Help,
    }

    /// <summary>
    /// 命令行参数（只有五种形态，见 <see cref="HostMode"/>）。
    /// </summary>
    /// <remarks>
    /// 解析刻意保持"平铺、大小写不敏感、认不出就忽略"：本进程会被玩家、打包脚本与验收脚本
    /// 分别调用，唯独不需要处理复杂组合——复杂参数解析是启动器（支持更新源、安装根、
    /// 断点续传等）才需要的能力。
    /// </remarks>
    internal sealed class CommandLine
    {
        /// <summary>指定配置文件所在目录。</summary>
        public const string DirectorySwitch = "--dir";

        /// <summary>写出默认配置。</summary>
        public const string WriteDefaultConfigSwitch = "--write-default-config";

        /// <summary>校验配置。</summary>
        public const string CheckSwitch = "--check";

        /// <summary>渲染界面截图。</summary>
        public const string RenderSwitch = "--render";

        /// <summary>打印帮助。</summary>
        public const string HelpSwitch = "--help";

        /// <summary>运行形态。</summary>
        public HostMode Mode { get; private set; }

        /// <summary>服务器目录（绝对路径）；未指定时为面板所在目录。</summary>
        public string Directory { get; private set; }

        /// <summary>渲染目标 PNG 路径；非渲染模式为 null。</summary>
        public string RenderPath { get; private set; }

        /// <summary>参数错误原因；没有错误时为 null。</summary>
        public string Error { get; private set; }

        /// <summary>
        /// 解析参数。
        /// </summary>
        /// <param name="args">原始命令行。</param>
        /// <returns>解析结果；出现参数错误时 <see cref="Error"/> 非空。</returns>
        public static CommandLine Parse(string[] args)
        {
            var result = new CommandLine
            {
                Mode = HostMode.UserInterface,
                Directory = ServerLayout.PanelDirectory(),
            };

            var list = args ?? Array.Empty<string>();
            for (var index = 0; index < list.Length; index++)
            {
                var argument = list[index];
                switch (argument.ToLowerInvariant())
                {
                    case DirectorySwitch:
                        result.Directory = Resolve(NextValue(list, ref index));
                        break;

                    case WriteDefaultConfigSwitch:
                        result.Mode = HostMode.WriteDefaultConfig;
                        result.Directory = Resolve(NextValue(list, ref index));
                        break;

                    case CheckSwitch:
                        result.Mode = HostMode.Check;
                        result.Directory = Resolve(NextValue(list, ref index));
                        break;

                    case RenderSwitch:
                        result.Mode = HostMode.Render;
                        result.RenderPath = NextValue(list, ref index);
                        break;

                    case HelpSwitch:
                    case "-h":
                        result.Mode = HostMode.Help;
                        break;
                }
            }

            result.Validate();
            return result;
        }

        /// <summary>取下一个参数值（没有则为 null，交由 <see cref="Validate"/> 报错）。</summary>
        private static string NextValue(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                return null;
            }

            index++;
            return args[index];
        }

        /// <summary>把目录参数解析成绝对路径。</summary>
        /// <remarks>
        /// 相对路径按"当前工作目录"解析：脚本传相对路径时的直觉就是"我在这里敲的命令"，
        /// 与配置文件里 <c>-config</c> 按 exe 目录解析的规则不同——那里防的是双击启动时
        /// 工作目录漂移，这里防的是脚本作者以为自己写对了。
        /// </remarks>
        private static string Resolve(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(directory.Trim());
            }
            catch (Exception)
            {
                // 非法路径（含非法字符、过长等）留给 Validate 报一句能看懂的话。
                return directory.Trim();
            }
        }

        /// <summary>检查参数组合是否可用。</summary>
        private void Validate()
        {
            if (Mode == HostMode.WriteDefaultConfig && string.IsNullOrEmpty(Directory))
            {
                Error = $"{WriteDefaultConfigSwitch} 需要指定目标目录。";
                return;
            }

            if (Mode == HostMode.Check && string.IsNullOrEmpty(Directory))
            {
                Error = $"{CheckSwitch} 需要指定服务器目录。";
                return;
            }

            if (Mode == HostMode.Render && string.IsNullOrEmpty(RenderPath))
            {
                Error = $"{RenderSwitch} 需要指定输出 PNG 路径。";
            }
        }
    }
}
