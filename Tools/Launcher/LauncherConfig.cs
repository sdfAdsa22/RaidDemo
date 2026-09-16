using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RaidDemo.Launcher
{
    /// <summary>
    /// 一个"更新源档案"：更新源地址 + 该源对应的默认游戏服务器地址。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么更新源和游戏服务器地址放在一起：</b>典型用法是"本机开服快速迭代、云端开服对外分发"——
    /// 选了哪个更新源，多半就希望连哪台服务器。把它俩绑在一个档案里，
    /// 玩家只需在下拉框里选一次，不用分别填两个地址（ADR-007 第 4 条）。</para>
    ///
    /// <para><b>地址可以是两种形态：</b>HTTP(S) 地址（云主机 / 局域网服务器）或本地目录路径（本机演示）。
    /// 本地目录形态让"本机"更新源不需要起任何服务，验证链路时少一个会坏的环节。</para>
    /// </remarks>
    public sealed class UpdateSourceProfile
    {
        /// <summary>显示名（例：本机 / 云主机 / 自定义）。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>更新源根地址：<c>http(s)://…</c> 或本地目录路径。</summary>
        public string ManifestSource { get; set; } = string.Empty;

        /// <summary>该源对应的默认游戏服务器地址（传给游戏的 <c>-connect</c> 参数）。</summary>
        public string GameServer { get; set; } = string.Empty;

        /// <summary>是否为自定义源（界面上允许直接编辑地址）。</summary>
        public bool Editable { get; set; }

        /// <summary>显示用文本：名称 + 地址。</summary>
        public override string ToString()
        {
            return string.IsNullOrEmpty(ManifestSource) ? Name : $"{Name}（{ManifestSource}）";
        }
    }

    /// <summary>
    /// 启动器配置。
    /// </summary>
    /// <remarks>
    /// <para><b>两个文件、后者覆盖前者：</b><c>launcher.config.json</c> 入库（只有占位地址，公开仓库不能带真实 IP），
    /// <c>launcher.config.local.json</c> 被 .gitignore 忽略、放本机真实地址。
    /// 这样"仓库里能跑"与"本机连得上云主机"两件事同时成立。</para>
    ///
    /// <para><b>所有路径都相对启动器所在目录：</b>启动器可能被放在任何位置（安装根、U 盘、桌面），
    /// 写死绝对路径会让它在换台机器后就失效——这是本工程从一开始就坚持的纪律
    /// （与 Unity 侧 <c>PathRulesTests</c> 禁止绝对路径是同一条）。</para>
    /// </remarks>
    public sealed class LauncherConfig
    {
        /// <summary>入库的配置文件名。</summary>
        public const string DefaultFileName = "launcher.config.json";

        /// <summary>本机私有配置文件名（覆盖默认配置；被 .gitignore 忽略）。</summary>
        public const string LocalOverrideFileName = "launcher.config.local.json";

        /// <summary>
        /// 游戏安装根：本体文件（exe 与数据目录）直接放在这里面。
        /// </summary>
        /// <remarks>
        /// <para><b>两种写法都支持：</b>默认值 <c>Game</c> 是相对启动器所在目录的相对路径
        /// （开箱即用、不挑盘符）；玩家在界面上选了别的目录之后，这里会被改写成绝对路径。
        /// <see cref="Path.Combine(string, string)"/> 遇到绝对路径会**原样返回**，
        /// 因此两种形态在读取侧是同一段代码，不需要分支判断。</para>
        ///
        /// <para><b>绝对路径不会进仓库：</b>它只写进被 .gitignore 忽略的
        /// <c>launcher.config.local.json</c>，入库的 <c>launcher.config.json</c> 永远保持
        /// 相对路径 + 占位地址——否则别人克隆下来就带着一台陌生机器的目录。</para>
        /// </remarks>
        public string InstallRoot { get; set; } = "Game";

        /// <summary>游戏可执行文件名（相对安装根）。</summary>
        public string GameExecutable { get; set; } = "RaidDemo.exe";

        /// <summary>当前选中的更新源名称。</summary>
        public string SelectedSource { get; set; } = "本机";

        /// <summary>更新源档案列表。</summary>
        public List<UpdateSourceProfile> Sources { get; set; } = new List<UpdateSourceProfile>();

        /// <summary>启动游戏时附加的额外参数（空格分隔；留空表示不加）。</summary>
        public string ExtraGameArguments { get; set; } = string.Empty;

        /// <summary>本次配置实际来自哪个文件（用于界面与排障）。</summary>
        public string LoadedFromPath { get; set; } = string.Empty;

        /// <summary>
        /// 从启动器所在目录加载配置。
        /// </summary>
        /// <param name="baseDirectory">启动器所在目录。</param>
        /// <returns>配置对象；两个文件都不存在时返回带默认档案的配置。</returns>
        public static LauncherConfig Load(string baseDirectory)
        {
            var localPath = Path.Combine(baseDirectory, LocalOverrideFileName);
            var defaultPath = Path.Combine(baseDirectory, DefaultFileName);

            var path = File.Exists(localPath) ? localPath : (File.Exists(defaultPath) ? defaultPath : null);
            if (path == null)
            {
                var fallback = CreateDefault();
                fallback.LoadedFromPath = "(未找到配置文件，使用内置默认)";
                return fallback;
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, SerializerOptions) ?? CreateDefault();
            config.LoadedFromPath = path;

            if (config.Sources == null || config.Sources.Count == 0)
            {
                config.Sources = CreateDefault().Sources;
            }

            return config;
        }

        /// <summary>
        /// 保存到本机私有配置（永远不覆盖入库的那份）。
        /// </summary>
        /// <param name="baseDirectory">启动器所在目录。</param>
        public void Save(string baseDirectory)
        {
            var path = Path.Combine(baseDirectory, LocalOverrideFileName);
            File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
            LoadedFromPath = path;
        }

        /// <summary>取当前选中的更新源档案；未找到时退回第一个。</summary>
        public UpdateSourceProfile GetSelectedProfile()
        {
            foreach (var profile in Sources)
            {
                if (string.Equals(profile.Name, SelectedSource, StringComparison.Ordinal))
                {
                    return profile;
                }
            }

            return Sources.Count > 0 ? Sources[0] : null;
        }

        /// <summary>取游戏可执行文件的完整路径。</summary>
        public string GetGameExecutablePath(string baseDirectory)
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, InstallRoot, GameExecutable));
        }

        /// <summary>取安装根目录的完整路径。</summary>
        public string GetInstallRootPath(string baseDirectory)
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, InstallRoot));
        }

        /// <summary>
        /// 校验并切换安装根目录（界面上"安装目录"那一行的唯一入口）。
        /// </summary>
        /// <param name="rawInput">候选路径：可以是相对启动器目录的相对路径，也可以是绝对路径。</param>
        /// <param name="baseDirectory">启动器所在目录（相对路径的基准）。</param>
        /// <param name="resolvedPath">规范化之后的绝对路径；失败时为 <c>null</c>。</param>
        /// <param name="error">失败原因（给玩家看的中文说明）；成功时为 <c>null</c>。</param>
        /// <returns>是否切换成功。</returns>
        /// <remarks>
        /// <para><b>只在成功时写回属性：</b>界面在失败时会继续用旧目录工作，
        /// 所以这里绝不能"改一半"——要么整体生效，要么配置原样不动。
        /// 半个生效的配置（属性改了、但保存失败）会让"界面显示"与"实际更新位置"分家，
        /// 那是最难排查的一类故障。</para>
        ///
        /// <para><b>为什么要把末尾的分隔符去掉：</b>玩家从对话框选出来的路径通常不带尾斜杠，
        /// 但手工输入很容易带上（<c>D:\Games\RaidDemo\</c>）。两种写法指向同一个目录，
        /// 却会被当作"切换了目录"反复触发切换日志；规范化之后比较才有意义。
        /// 盘符根（<c>D:\</c>）是唯一不能裁剪的形态——裁成 <c>D:</c> 会变成"该盘的当前目录"，
        /// 完全是另一个位置。</para>
        ///
        /// <para><b>这里不碰文件系统：</b>本方法只做"路径语法与语义"层面的判断（空、非法字符、指向文件）。
        /// 目录是否可创建、是否可写由调用方在真正切换时验证——保持这个类可被纯逻辑测试覆盖。</para>
        /// </remarks>
        public bool TrySetInstallRoot(string rawInput, string baseDirectory, out string resolvedPath, out string error)
        {
            resolvedPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(rawInput))
            {
                error = "安装目录不能为空。";
                return false;
            }

            try
            {
                resolvedPath = Path.GetFullPath(Path.Combine(baseDirectory, rawInput.Trim()));
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = "路径格式不合法：" + exception.Message;
                return false;
            }

            var root = Path.GetPathRoot(resolvedPath);
            if (!string.Equals(resolvedPath, root, StringComparison.OrdinalIgnoreCase))
            {
                resolvedPath = resolvedPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }

            // 指向已存在的"文件"是最常见的误选（玩家把 exe 当成了目录）。
            // 此刻拦下来，比让更新流程在"创建目录"那一步抛 IO 异常要清楚得多。
            if (File.Exists(resolvedPath))
            {
                error = "该路径是一个文件，不能作为安装目录：" + resolvedPath;
                return false;
            }

            InstallRoot = resolvedPath;
            return true;
        }

        /// <summary>生成内置默认配置（占位地址，可直接被 local 覆盖）。</summary>
        private static LauncherConfig CreateDefault()
        {
            return new LauncherConfig
            {
                Sources = new List<UpdateSourceProfile>
                {
                    new UpdateSourceProfile
                    {
                        Name = "本机",
                        ManifestSource = "http://127.0.0.1:8090",
                        GameServer = "127.0.0.1",
                    },
                    new UpdateSourceProfile
                    {
                        Name = "云主机",
                        ManifestSource = "http://<云主机IP>:8090",
                        GameServer = "<云主机IP>",
                    },
                    new UpdateSourceProfile
                    {
                        Name = "自定义",
                        ManifestSource = string.Empty,
                        GameServer = string.Empty,
                        Editable = true,
                    },
                },
            };
        }

        /// <summary>JSON 序列化选项：缩进 + 不转义中文，便于人工查看与手工编辑。</summary>
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // 允许大小写不敏感：配置文件是人手写的（camelCase 更自然），
            // 而 C# 属性是 PascalCase；本机覆盖文件由本工具自己写出，两种都能读。
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
