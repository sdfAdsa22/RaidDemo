using System;
using System.Collections.Generic;
using RaidDemo.Kernel;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 本进程的运行角色。
    /// </summary>
    /// <remarks>
    /// 角色由命令行决定，而不是由构建产物决定：客户端与服务器是同一份可执行文件，
    /// 差别只在启动参数。这样"两边逻辑不一致"这类问题在结构上就不存在。
    /// </remarks>
    public enum AppLaunchMode
    {
        /// <summary>单机：进程内跑权威逻辑，不需要网络。</summary>
        SinglePlayer = 0,

        /// <summary>联机客户端：连接远端或本机服务器，本地只做预测与表现。</summary>
        Client = 1,

        /// <summary>专用服务器：不装配客户端世界，只跑权威逻辑。</summary>
        Server = 2,
    }

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
    public sealed partial class LaunchOptions
    {
        /// <summary>默认监听端口。与 <c>Docs/Modules/10_联机.md</c> 第 15.2 节的端口规划一致。</summary>
        public const int DefaultPort = 7777;

        /// <summary>默认房间名。房主创建房间时可以覆盖。</summary>
        public const string DefaultRoomName = "默认房间";

        /// <summary>默认存档目录（相对服务器进程的工作目录）。</summary>
        public const string DefaultSaveDirectory = "server_saves";

        /// <summary>
        /// 默认更新源地址（本机更新源）。
        /// </summary>
        /// <remarks>
        /// 与启动器配置里的"本机"档案保持同一个默认值：直接双击游戏时也能连上本机更新源；
        /// 启动器启动时会用 <c>-updatesource</c> 覆盖它（ADR-007 的"更新源可切换"）。
        /// </remarks>
        public const string DefaultUpdateSource = "http://127.0.0.1:8090";

        /// <summary>
        /// 本进程启动时解析出的参数（单机 / 联机 / 服务器通用），由启动入口在场景加载前写入。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不能只从 <c>ClientMode.Options</c> 取：</b>那个静态属性只在**联机客户端**
        /// 激活时才有值，而"更新源"这类参数在单机模式下同样有意义（启动器拉起单机游戏也会传）。
        /// 早期实现只读 <c>ClientMode</c>，于是单机启动时 <c>-updatesource</c> 被静默忽略、
        /// 退回默认地址——实机日志里表现为"更新源是本机默认值而不是传入值"。</para>
        /// </remarks>
        public static LaunchOptions Current { get; internal set; }

        /// <summary>服务器状态页（Dashboard）的默认端口。0 表示关闭。</summary>
        public const int DefaultDashboardPort = 8080;

        /// <summary>
        /// 掉线宽限的默认时长（秒）。
        /// </summary>
        /// <remarks>与 <c>ServerRuntime.DefaultReconnectGraceSeconds</c> 保持一致（那里是权威默认值）。</remarks>
        public const float DefaultReconnectGraceSeconds = 60f;

        /// <summary>宽限时长的下界（秒）。低于它等于没有宽限，重连必然失败。</summary>
        public const float MinReconnectGraceSeconds = 5f;

        /// <summary>宽限时长的上界（秒）。太长会让队友长时间少一个人。</summary>
        public const float MaxReconnectGraceSeconds = 600f;

        /// <summary>
        /// 传输层自愈的"全员静默"判定时长默认值（秒）。
        /// </summary>
        /// <remarks>与 <c>TransportWatchdog.DefaultAllSilentSeconds</c> 保持一致（那里是权威默认值）。</remarks>
        public const float DefaultTransportWatchdogSeconds = 2.5f;

        /// <summary>看门狗判定时长的下界（秒）；0 单独表示"关闭看门狗"。</summary>
        public const float MinTransportWatchdogSeconds = 1f;

        /// <summary>看门狗判定时长的上界（秒）。高于它，服务器会先撞上 NGO 的协议超时。</summary>
        public const float MaxTransportWatchdogSeconds = 30f;

        /// <summary>局域网发现（UDP 广播）的默认端口。0 表示关闭。</summary>
        public const int DefaultDiscoveryPort = LanDiscoveryConstants.DefaultPort;

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
        /// <summary>是否让客户端自动行动（<c>-autowalk</c>）：无头验收的脚本化输入。</summary>
        public bool AutoWalk { get; private set; }

        /// <summary>
        /// 是否让客户端在联机安全屋里自动做一串交易自检（<c>-autotrade</c>）。
        /// </summary>
        /// <remarks>
        /// <para>验收辅助（P5.5）：房间就绪后按固定节拍执行"购买一件 → 卖出刚买的那件 →
        /// 接取第一个任务"。没有它，验证"商店在联机下可用"就必须有人手动操作鼠标，
        /// 而无人值守脚本恰恰是这条链路最需要的回归方式。</para>
        ///
        /// <para>它只派发与玩家点击完全相同的命令，不走任何后门——
        /// 验收的是真实链路，不是"绕过 UI 的特例"。</para>
        /// </remarks>
        public bool AutoTrade { get; private set; }

        /// <summary>验收模式：出生在第一个撤离点里（只改出生位置，不改规则）。</summary>
        public bool SpawnAtExtraction { get; private set; }
        /// <summary>验收模式：开局把玩家 1 打到 0（验收倒地与救援）。</summary>
        public bool DownTest { get; private set; }
        /// <summary>验收模式：客户端原地待命、只做救援（验收扶起队友）。</summary>
        public bool RescueOnly { get; private set; }

        /// <summary>要连接的服务器地址（<c>主机[:端口]</c>）；为 null 表示不是联机客户端。</summary>
        /// <remarks>P1~P3 的临时加入路径；P4 的大厅上线后保留给自动化测试与快速调试。</remarks>
        public string ConnectAddress { get; private set; }

        /// <summary>
        /// 更新源地址（<c>-updatesource</c>）；为 null 表示使用默认值。
        /// </summary>
        /// <remarks>
        /// <para>由启动器在拉起游戏时传入（ADR-007 第 4 条：更新源与默认服务器地址同源于启动器档案）。
        /// 直接双击游戏时该参数缺席，此时退回 <see cref="DefaultUpdateSource"/>。</para>
        ///
        /// <para>允许 <c>http(s)://</c> 与本地目录两种形态：本地目录是"不依赖任何服务"的本机演示方式。</para>
        /// </remarks>
        public string UpdateSource { get; private set; }

        /// <summary>
        /// 联机客户端的昵称（<c>-nickname</c>）；为 null 表示由界面或自动流程取名。
        /// </summary>
        /// <remarks>自动化验收需要两个不同昵称的客户端同时在线，因此昵称必须能从命令行指定。</remarks>
        public string Nickname { get; private set; }

        /// <summary>联机客户端的账号口令（<c>-passphrase</c>）；仅验收与快速调试使用。</summary>
        public string Passphrase { get; private set; }

        /// <summary>联机客户端是否自动登录并创建 / 加入房间（<c>-autoroom</c>，验收辅助）。</summary>
        public bool AutoRoom { get; private set; }

        /// <summary>自动加入房间时使用的密码（<c>-roompass</c>，验收辅助）。</summary>
        public string RoomPassword { get; private set; } = string.Empty;

        /// <summary>
        /// 服务器在房间成立后自动开局的等待秒数（<c>-autostart</c>，验收辅助）。
        /// </summary>
        /// <remarks>
        /// <para>0 表示关闭（默认）。它的存在只为让"服务器 + 若干客户端"的自动化脚本
        /// 不必真的用鼠标点一次「开始战局」——那一步在人工验收里手动走。</para>
        ///
        /// <para>它只改"谁来触发开局"，开局本身走的是与房主点击完全相同的服务器路径。</para>
        /// </remarks>
        public float AutoStartSeconds { get; private set; }

        /// <summary>服务器状态页端口（<c>-dashboardPort</c>）；0 表示关闭状态页。</summary>
        public int DashboardPort { get; private set; } = DefaultDashboardPort;

        /// <summary>局域网发现端口（<c>-discoveryPort</c>）；0 表示关闭自动发现。</summary>
        public int DiscoveryPort { get; private set; } = DefaultDiscoveryPort;

        /// <summary>
        /// 掉线宽限时长（<c>-grace</c>，秒）；默认 <see cref="DefaultReconnectGraceSeconds"/>。
        /// </summary>
        /// <remarks>
        /// <para>连接断开后，服务器保留该玩家的房间席位、位置与背包多久。期间同一账号可以重连回局。</para>
        ///
        /// <para>做成启动参数是为了验收：自动化脚本需要把 60 秒压到十几秒来跑"断线 → 宽限到期 → 清场"
        /// 这条完整链路；线上则保留默认值。</para>
        /// </remarks>
        public float ReconnectGraceSeconds { get; private set; } = DefaultReconnectGraceSeconds;

        /// <summary>
        /// 传输层自愈的"全员静默"判定时长（<c>-watchdog</c>，秒）；0 表示关闭；默认 2.5。
        /// </summary>
        /// <remarks>
        /// <para>服务器连续这么久收不到任何在线客户端的上行、且权威世界里有人时，
        /// 判定上行接收路径整体失效（P-51），重建传输层并等客户端自动重连。</para>
        ///
        /// <para>做成启动参数有两个用途：验收脚本把判定压到更短以缩短实验时间；
        /// 以及出问题时用 <c>-watchdog 0</c> 一键退回"不做自愈"的旧行为，便于对照排查。</para>
        /// </remarks>
        public float TransportWatchdogSeconds { get; private set; } = DefaultTransportWatchdogSeconds;

        /// <summary>
        /// 服务器启动后要加载的场景名；为 null 表示不额外加载（用构建列表的第一个场景）。
        /// </summary>
        /// <remarks>
        /// 服务器需要地图的碰撞与将来的 AI 导航数据，因此真实运行时由启动脚本传 <c>-map GreyboxRaid</c>；
        /// 自动化测试不传，避免为了验证网络而加载整张地图。
        /// </remarks>
        public string MapSceneName { get; private set; }

        /// <summary>本进程的角色：单机 / 联机客户端 / 专用服务器。</summary>
        public AppLaunchMode Mode
        {
            get
            {
                if (IsServerRequested)
                {
                    return AppLaunchMode.Server;
                }

                return string.IsNullOrEmpty(ConnectAddress) ? AppLaunchMode.SinglePlayer : AppLaunchMode.Client;
            }
        }

        /// <summary>解析过程中的非致命提示（例如参数被忽略的原因），供启动日志打印。</summary>
        public IReadOnlyList<string> Warnings => m_Warnings;
        /// <summary>把解析结果整理成一行摘要，供启动日志打印。</summary>
        public string Describe()
        {
            var description =
                $"{DescribeMode()} ｜ 端口 {Port} ｜ 房间「{RoomName}」｜ 存档目录 {SaveDirectory} ｜ " +
                $"日志 {MinimumLogLevel} ｜ 无头 {IsHeadless}";

            if (!string.IsNullOrEmpty(ConnectAddress))
            {
                description += $" ｜ 连接 {ConnectAddress}";
            }

            if (!string.IsNullOrEmpty(MapSceneName))
            {
                description += $" ｜ 地图 {MapSceneName}";
            }

            if (!string.IsNullOrEmpty(Nickname))
            {
                description += $" ｜ 昵称 {Nickname}";
            }

            if (AutoRoom)
            {
                description += " ｜ 自动进房";
            }

            if (AutoStartSeconds > 0f)
            {
                description += $" ｜ 自动开局 {AutoStartSeconds:F0} 秒";
            }

            if (IsServerRequested)
            {
                description += $" ｜ 状态页 {(DashboardPort == 0 ? "关闭" : DashboardPort.ToString())}";
                description += $" ｜ 发现 {(DiscoveryPort == 0 ? "关闭" : DiscoveryPort.ToString())}";
                description += $" ｜ 掉线宽限 {ReconnectGraceSeconds:F0} 秒";
                description += $" ｜ 自愈看门狗 {(TransportWatchdogSeconds <= 0f ? "关闭" : $"{TransportWatchdogSeconds:F1} 秒")}";
            }

            return description;
        }

        /// <summary>角色的中文名，用于日志与界面。</summary>
        public string DescribeMode()
        {
            switch (Mode)
            {
                case AppLaunchMode.Server:
                    return "专用服务器";
                case AppLaunchMode.Client:
                    return "联机客户端";
                default:
                    return "单机";
            }
        }
    }
}
