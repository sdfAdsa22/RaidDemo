using System;

namespace RaidDemo.Kernel.Server
{
    /// <summary>
    /// 服务器外部配置（<c>server.config.json</c>）的数据模型。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决的问题：</b>在此之前，服务器的全部配置都只能从命令行给
    /// （<c>-server -port 7777 -room …</c>）。对"双击一个 exe 就开服"的形态来说，
    /// 命令行不是玩家该碰的东西：记不住、容易敲错、出错时的报错也看不懂。
    /// 于是把配置挪进同目录的一个 JSON 文件——面板程序改它，服务器读它，
    /// 两套平台（Windows 面板 / Linux 脚本）共用同一份格式。</para>
    ///
    /// <para><b>优先级：命令行 &gt; 配置文件 &gt; 内置默认</b>（见 <c>LaunchOptions</c>）。
    /// 保留命令行优先，是为了让既有的验收脚本与云主机部署命令一行都不用改：
    /// 只要显式给了参数，配置文件里写了什么都不会影响它。</para>
    ///
    /// <para><b>数值字段用"哨兵值"表示未设置</b>（<see cref="UnsetInt"/> / <see cref="UnsetFloat"/>）：
    /// 手写配置文件时漏掉一个字段是常态，这时必须回退到内置默认值，而不是把 0 当成用户的选择
    /// （<c>raidDuration = 0</c>、<c>dashboardPort = 0</c> 都是合法且含义完全不同的取值）。
    /// 不用 <c>int?</c> / <c>float?</c> 的原因：Unity 的 <c>JsonUtility</c> 不支持可空值类型。</para>
    ///
    /// <para><b>为什么放在纯逻辑层：</b>启动器与服务器面板（<c>Tools/</c> 下的 .NET 程序）
    /// 通过链接文件直接编译这一份源码，因此"字段名与含义"只有一处定义。
    /// 这与更新清单（<c>UpdateManifest</c>）的做法一致。</para>
    ///
    /// <para><b>字段名一旦发布就不能再改</b>：它同时是文件名与 JSON 键，改了就相当于换了协议。</para>
    /// </remarks>
    [Serializable]
    public sealed class ServerConfigDocument
    {
        /// <summary>配置文件默认文件名（与服务器可执行文件同目录）。</summary>
        public const string FileName = "server.config.json";

        /// <summary>整数"未设置"哨兵值。</summary>
        public const int UnsetInt = -1;

        /// <summary>浮点"未设置"哨兵值。</summary>
        public const float UnsetFloat = -1f;

        /// <summary>默认监听端口。</summary>
        public const int DefaultPort = 7777;

        /// <summary>默认房间名。</summary>
        public const string DefaultRoomName = "默认房间";

        /// <summary>默认存档目录（相对服务器进程的工作目录）。</summary>
        public const string DefaultSaveDirectory = "server_saves";

        /// <summary>默认日志等级。</summary>
        public const string DefaultLogLevel = "info";

        /// <summary>默认战局时长上限（秒）；0 表示不做超时判定。</summary>
        public const float DefaultRaidDurationSeconds = 480f;

        /// <summary>默认"房间成立后自动开局"的等待秒数；0 表示关闭。</summary>
        public const float DefaultAutoStartSeconds = 0f;

        /// <summary>默认状态页端口；0 表示关闭状态页。</summary>
        public const int DefaultDashboardPort = 8080;

        /// <summary>默认局域网发现端口；0 表示关闭自动发现。</summary>
        public const int DefaultDiscoveryPort = 47777;

        /// <summary>默认掉线宽限时长（秒）。</summary>
        public const float DefaultReconnectGraceSeconds = 60f;

        /// <summary>默认传输层自愈判定时长（秒）；0 表示关闭看门狗。</summary>
        public const float DefaultTransportWatchdogSeconds = 2.5f;

        /// <summary>
        /// 默认要加载的地图场景名。
        /// </summary>
        /// <remarks>
        /// 服务器需要地图的碰撞与导航数据，所以"真实开服"必须带 <c>-map</c>；
        /// 空字符串表示"不额外加载"（用构建列表的第一个场景），那是自动化测试的用法。
        /// </remarks>
        public const string DefaultMapScene = "GreyboxRaid";

        /// <summary>监听端口（UDP）。未设置时为 <see cref="UnsetInt"/>。</summary>
        public int port = UnsetInt;

        /// <summary>房间名。未设置时为空字符串。</summary>
        public string room = string.Empty;

        /// <summary>存档目录（只接受相对路径）。未设置时为空字符串。</summary>
        public string saveDir = string.Empty;

        /// <summary>日志等级（verbose / info / warning / error）。未设置时为空字符串。</summary>
        public string logLevel = string.Empty;

        /// <summary>要加载的地图场景名。未设置时为空字符串。</summary>
        public string map = string.Empty;

        /// <summary>战局时长上限（秒）；0 表示不做超时判定。未设置时为 <see cref="UnsetFloat"/>。</summary>
        public float raidDuration = UnsetFloat;

        /// <summary>房间成立后自动开局的等待秒数；0 表示关闭。未设置时为 <see cref="UnsetFloat"/>。</summary>
        public float autoStart = UnsetFloat;

        /// <summary>状态页端口；0 表示关闭。未设置时为 <see cref="UnsetInt"/>。</summary>
        public int dashboardPort = UnsetInt;

        /// <summary>局域网发现端口；0 表示关闭。未设置时为 <see cref="UnsetInt"/>。</summary>
        public int discoveryPort = UnsetInt;

        /// <summary>掉线宽限时长（秒）。未设置时为 <see cref="UnsetFloat"/>。</summary>
        public float grace = UnsetFloat;

        /// <summary>传输层自愈判定时长（秒）；0 表示关闭。未设置时为 <see cref="UnsetFloat"/>。</summary>
        public float watchdog = UnsetFloat;
    }
}
