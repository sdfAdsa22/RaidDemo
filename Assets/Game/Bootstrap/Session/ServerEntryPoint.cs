using System;
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
            if (!LaunchOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out var error))
            {
                Debug.LogError($"[启动] 命令行参数解析失败：{error}");

                if (!Application.isEditor)
                {
                    Application.Quit(1);
                }

                return;
            }

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

            Debug.Log($"[启动] 以服务器模式运行 ｜ {options.Describe()}");
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
