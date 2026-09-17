using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 本进程是否以服务器模式运行，以及它的启动参数。
    /// </summary>
    /// <remarks>
    /// <para>客户端装配根（<c>SceneBootstrap</c> / <c>SafeHouseBootstrap</c>）在 <c>Awake</c> 里查这个标记：
    /// 服务器进程不需要相机、输入、界面与玩家表现，装配它们既浪费资源，也会让日志里混进无关报错。</para>
    ///
    /// <para>用静态标记而不是「当前场景名」判断：服务器与客户端会加载同一张地图，
    /// 「谁在运行」是进程属性，不是场景属性。</para>
    /// </remarks>
    public static class ServerMode
    {
        /// <summary>是否以服务器模式启动。</summary>
        public static bool IsActive { get; private set; }

        /// <summary>
        /// 场景里的物品目录。
        /// </summary>
        /// <remarks>
        /// <para><b>服务器为什么需要它：</b>权威逻辑要用真实内容——武器参数、弹药口径、
        /// 备弹数量都来自物品定义。而服务器不装配客户端世界，那些序列化引用会随客户端装配根一起消失。</para>
        ///
        /// <para>做法：客户端装配根在服务器模式下销毁自己之前，先把目录交到这里。
        /// 场景只加载一次、交接只发生一次，因此用静态引用是安全的；
        /// 真正的所有权在会话（<see cref="ServerRuntime.Session"/>）手里。</para>
        /// </remarks>
        public static RaidDemo.Data.ItemCatalog SceneItemCatalog { get; private set; }

        /// <summary>场景里的表现层目录（武器模型、音效等）。服务器用不到，保留给调试工具。</summary>
        public static RaidDemo.Presentation.PresentationCatalog ScenePresentationCatalog { get; private set; }

        /// <summary>
        /// 安全屋场景里的玩家出生点（平面坐标）。
        /// </summary>
        /// <remarks>
        /// <para>由安全屋装配根在服务器模式下交接（与 <see cref="SceneItemCatalog"/> 同一机制）。
        /// 服务器要托管安全屋，就必须知道"把玩家放进屋里的哪个位置"，
        /// 而这个位置本来就写在场景里——交接一份数据胜过在服务器代码里抄一个坐标常量
        /// （抄了就会在下次改场景时对不上）。</para>
        ///
        /// <para>默认值与场景生成器里的出生点一致，供不加载安全屋的 EditMode 测试使用。</para>
        /// </remarks>
        public static RaidDemo.Shared.Vector2F SafeHouseSpawnPosition { get; private set; } =
            new RaidDemo.Shared.Vector2F(0f, -5f);

        /// <summary>由安全屋装配根交接出生点。</summary>
        internal static void AcceptSafeHouseSpawn(UnityEngine.Vector2 position)
        {
            SafeHouseSpawnPosition = new RaidDemo.Shared.Vector2F(position.x, position.y);
        }

        /// <summary>由客户端装配根在服务器模式下交接场景内容。</summary>
        internal static void AcceptSceneContent(
            RaidDemo.Data.ItemCatalog itemCatalog,
            RaidDemo.Presentation.PresentationCatalog presentationCatalog)
        {
            SceneItemCatalog = itemCatalog;
            ScenePresentationCatalog = presentationCatalog;
        }

        /// <summary>启动参数。仅当 <see cref="IsActive"/> 为 true 时有效。</summary>
        public static LaunchOptions Options { get; private set; }

        /// <summary>由启动入口激活服务器模式。</summary>
        internal static void Activate(LaunchOptions options)
        {
            IsActive = true;
            Options = options;
        }

        /// <summary>
        /// 专用服务器在有控制台的 Windows 上把控制台切到 UTF-8。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么必须做：</b>Unity 把日志按 UTF-8 写到 stdout，而 Windows 控制台默认代码页
        /// 是 GBK（936）——中文日志会全变成 `锛?` 这样的乱码（负责人反馈的"服务端启动后中文乱码"）。
        /// 把控制台输出代码页切到 65001 之后，同一份字节就能正确显示，日志文件不受影响。</para>
        ///
        /// <para><b>为什么包一层 try：</b>没有附加控制台时（双击启动、<c>-batchmode</c> 且输出被重定向到文件）
        /// 设置编码会抛异常；这不是错误，游戏照常运行。日志文件本身一直是 UTF-8。</para>
        /// </remarks>
        public static void UseUtf8Console()
        {
            try
            {
                // 必须直接调 Win32：只设 Console.OutputEncoding 在 Unity 播放器里不生效
                // （日志由播放器的原生写出口输出，实测仍然是乱码；而控制台里手动
                // 执行 `chcp 65001` 之后立刻正常——两相对照确定问题就在控制台代码页）。
                if (GetConsoleWindow() != IntPtr.Zero)
                {
                    SetConsoleOutputCP(Utf8CodePage);
                }

                Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch (Exception exception)
            {
                Debug.Log($"[启动] 控制台编码切换跳过（没有可用控制台）：{exception.GetType().Name}");
            }
        }

        /// <summary>控制台 UTF-8 代码页（65001）。</summary>
        private const uint Utf8CodePage = 65001;

        /// <summary>取当前进程挂着的控制台窗口；没有控制台时为 0。</summary>
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        /// <summary>设置控制台输出代码页。</summary>
        /// <param name="codePage">目标代码页。</param>
        /// <returns>成功返回 true。</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleOutputCP(uint codePage);
    }

    /// <summary>
    /// 本进程是否以联机客户端运行，以及要连接的地址。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="ServerMode"/> 对称：客户端也要在装配之前就知道自己的角色，
    /// 因为"要不要建立网络会话"会改变装配路径——联机客户端的移动由预测与快照驱动，
    /// 而单机是纯本地模拟。</para>
    ///
    /// <para><b>它是 P1~P3 的临时入口</b>：P4 的大厅界面会让玩家在游戏里选服务器，
    /// 那时本类退化为自动化测试与快速调试用的旁路。</para>
    /// </remarks>
    public static class ClientMode
    {
        /// <summary>是否以联机客户端启动。</summary>
        public static bool IsActive { get; private set; }

        /// <summary>要连接的服务器地址（主机[:端口]）。</summary>
        public static string Address { get; private set; }

        /// <summary>启动参数。仅当 <see cref="IsActive"/> 为 true 时有效。</summary>
        public static LaunchOptions Options { get; private set; }

        /// <summary>
        /// 是否正在执行"主动退出联机"流程（返回主菜单 / 退出桌面）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么需要它（跨场景的静态标记）：</b>主动退出会先发 LeaveRoom 并等确认；
        /// 服务器处理离房时若战局随之收尾，会广播"回屋"——客户端收到后立刻加载安全屋场景，
        /// 把等待中的退出协程连同宿主对象一起销毁，后续的"断开 + 回主菜单/退进程"永远不执行。
        /// 表现就是负责人反馈的"点返回主界面/返回桌面没有效果"。</para>
        ///
        /// <para>放在 <see cref="ClientMode"/> 而不是流程控制器：流程控制器是场景对象，
        /// 场景一变它就重建了，标记必须在整个退出窗口里都有效。</para>
        /// </remarks>
        public static bool IsExitTransitionActive { get; private set; }

        /// <summary>进入"主动退出联机"流程：期间忽略被动的回屋/切场景广播。</summary>
        public static void BeginExitTransition()
        {
            IsExitTransitionActive = true;
        }

        /// <summary>由启动入口激活联机客户端模式。</summary>
        /// <remarks>
        /// 公开给编辑器菜单使用：在编辑器里模拟一个联机客户端（<c>RaidDemo/M9/</c> 菜单），
        /// 这样调试联机时不必每次都出包。正常运行路径仍由 <see cref="ServerEntryPoint"/> 调用。
        /// </remarks>
        public static void Activate(LaunchOptions options)
        {
            IsActive = true;
            Address = options.ConnectAddress;
            Options = options;
        }

        /// <summary>
        /// 退出联机时调用：本进程不再以"联机客户端"身份运行。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么需要它：</b><see cref="IsActive"/> 从前是**进程级**的——只要启动时带了
        /// <c>-connect</c>，它就永远为真。于是"从暂停菜单返回主菜单"之后，
        /// 重新加载的安全屋仍然认为自己是联机进程：不显示主菜单、直接进入安全屋、
        /// 并等着一个已经不存在的服务器世界来装配玩家——表现是画面里站着人、但没有武器、
        /// 生命显示 <c>--/--</c>，而且完全不能动（负责人反馈的问题 9）。</para>
        ///
        /// <para><b>为什么不清理 <see cref="Options"/>：</b>日志等级这类设置来自它，
        /// 退出联机后继续沿用同一份运行参数是合理的；真正需要失效的只有"是不是联机客户端"。</para>
        /// </remarks>
        public static void Deactivate()
        {
            IsActive = false;
            Address = null;
            IsExitTransitionActive = false;
        }
    }

    /// <summary>
    /// 进程启动入口：判断这次运行是客户端还是专用服务器。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 而不是场景里的启动对象：</b>
    /// 服务器模式必须在任何场景装配之前就确定下来——一旦客户端的装配根先跑起来，
    /// 它就会创建相机、锁光标、订阅输入，再想撤销只能靠销毁一堆对象。
    /// <c>BeforeSceneLoad</c> 阶段做判断、<c>AfterSceneLoad</c> 阶段起服务器，顺序天然正确。</para>
    ///
    /// <para><b>与发布形态的关系：</b>客户端与服务器是同一个工程、同一份代码，
    /// 差别只在这里——谁带 <c>-server</c> 参数谁就是服务器。
    /// 这正是设计文档第 2 节「一套代码、两种运行方式」的落地位置。</para>
    /// </remarks>
    public static class ServerEntryPoint
    {
        /// <summary>专用服务器的帧率上限（Hz）。</summary>
        /// <remarks>
        /// 战局逻辑是 60Hz 固定步、20Hz 快照，60 帧足以覆盖全部节拍；
        /// 详见 <see cref="DetectServerMode"/> 里的限帧说明。
        /// </remarks>
        private const int ServerFrameRate = 60;

        /// <summary>
        /// 场景加载前：解析命令行，决定本进程的角色。
        /// </summary>
        /// <remarks>
        /// 参数非法时不会「降级成客户端继续跑」——那会让一次错误的启动看起来像成功启动，
        /// 直到有人发现连不上才暴露。这里直接报错并结束进程（编辑器内只报错，方便调试）。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DetectServerMode()
        {
            if (!LaunchOptions.TryParseWithServerConfig(
                    Environment.GetCommandLineArgs(),
                    ResolveExecutableDirectory(),
                    out var options,
                    out var error))
            {
                Debug.LogError($"[启动] 命令行参数解析失败：{error}");

                if (!Application.isEditor)
                {
                    Application.Quit(1);
                }

                return;
            }

            // 参数对三种运行形态都有效：更新源等参数在单机模式下也要能读到。
            LaunchOptions.Current = options;

            if (!options.IsServerRequested)
            {
                if (options.Mode == AppLaunchMode.Client)
                {
                    ClientMode.Activate(options);
                    Debug.Log($"[启动] 以联机客户端运行 ｜ 连接 {options.ConnectAddress}");
                }

                return;
            }

            ServerMode.Activate(options);

            // 无头服务器没有窗口，失去焦点是常态：停掉帧循环等于让整个服务器停摆。
            Application.runInBackground = true;

            // 帧率上限（U-98）：专用服务器没有渲染帧要画，而 -batchmode -nographics
            // 既没有垂直同步、也没有别的节流——不设上限时 Unity 主循环会以机器极限速度空转。
            // 云主机实测（0 人在线）：主线程常驻 95%+ 单核、Job worker 再吃 ~20%，
            // 2 核整机监控长期贴近 100%。把帧率钉在 60 之后，空转的那部分开销直接消失，
            // 而 60Hz 仿真、20Hz 快照、心跳与看门狗都不受任何影响。
            Application.targetFrameRate = ServerFrameRate;

            // 先切控制台编码再打第一行中文日志：晚一行就会先甩出一串乱码（负责人反馈过）。
            ServerMode.UseUtf8Console();

            Debug.Log($"[启动] 以服务器模式运行 ｜ {options.Describe()}");
        }

        /// <summary>
        /// 取"可执行文件所在目录"：服务器配置文件的默认查找位置，也是 <c>-config</c> 相对路径的基准。
        /// </summary>
        /// <remarks>
        /// <para>玩家进程里 <c>Application.dataPath</c> 指向 <c>&lt;游戏&gt;_Data</c>，
        /// 它的父目录才是 exe 所在目录；编辑器里它指向 <c>&lt;工程&gt;/Assets</c>，父目录是工程根——
        /// 两种情况都落在"程序旁边"，语义一致。</para>
        ///
        /// <para><b>为什么不用当前工作目录：</b>双击、快捷方式、计划任务、从别的盘符敲命令，
        /// 工作目录各不相同；跟着它走会让"我的配置文件到底被读到没有"变成猜谜。
        /// 以 exe 所在目录为基准，才有"配置和服务器放在一起"这条稳定规则。</para>
        /// </remarks>
        private static string ResolveExecutableDirectory()
        {
            var dataPath = Application.dataPath;
            var directory = string.IsNullOrEmpty(dataPath) ? null : Path.GetDirectoryName(dataPath);
            return string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
        }

        /// <summary>
        /// 场景加载后：建立服务器运行时。
        /// </summary>
        /// <remarks>
        /// 放在场景加载之后，是因为服务器进程同样会加载一个场景（构建列表的第一个场景）。
        /// 客户端的装配根会在 <c>Awake</c> 里看到 <see cref="ServerMode.IsActive"/> 而自行退出，
        /// 因此这里只需要负责把服务器侧的东西建起来。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartServerRuntime()
        {
            if (!ServerMode.IsActive)
            {
                return;
            }

            ServerRuntime.Create(ServerMode.Options);
        }

        /// <summary>自动化验收用的默认昵称（未传 <c>-nickname</c> 时）。</summary>
        private const string DefaultAutoNickname = "测试员";

        /// <summary>自动化验收用的默认口令（未传 <c>-passphrase</c> 时）。</summary>
        private const string DefaultAutoPassphrase = "123456";

        /// <summary>
        /// 场景加载后：联机客户端建立大厅会话并连接（<c>-connect</c> 路径）。
        /// </summary>
        /// <remarks>
        /// <para><b>P4 的行为变化：</b>客户端不再"按参数直接进地图"。它先建立会话、登录，
        /// 进房（若带 <c>-autoroom</c>），等服务器通知开局后才加载地图——
        /// 与手点大厅界面走的是同一条路径，只是把界面那一侧的输入换成了命令行参数。</para>
        ///
        /// <para><b>为什么要在这里建会话：</b>构建列表的第一个场景是安全屋，
        /// 大厅界面就叠在它上面；会话必须早于界面存在，界面才有东西可显示。</para>
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartClientRuntime()
        {
            if (!ClientMode.IsActive)
            {
                return;
            }

            var options = ClientMode.Options;
            var session = MultiplayerClientSession.Ensure();

            session.AutoRoom = options != null && options.AutoRoom;
            session.AutoRoomPassword = options != null ? options.RoomPassword : string.Empty;

            var nickname = options != null && !string.IsNullOrEmpty(options.Nickname)
                ? options.Nickname
                : DefaultAutoNickname;
            var passphrase = options != null && !string.IsNullOrEmpty(options.Passphrase)
                ? options.Passphrase
                : DefaultAutoPassphrase;

            if (options != null && string.IsNullOrEmpty(options.Nickname) && options.AutoRoom)
            {
                Debug.Log(
                    $"[启动] 未指定 -nickname，使用默认昵称「{nickname}」：" +
                    "同一台服务器上的多个客户端必须用不同的 -nickname，否则会被判为昵称冲突。");
            }

            if (!session.Connect(ClientMode.Address, nickname, passphrase))
            {
                Debug.LogError("[启动] 联机客户端连接失败：地址、昵称或口令不合法。");
            }
        }
    }
}
