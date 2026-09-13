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

        /// <summary>
        /// 场景加载后：联机客户端按参数切到地图场景。
        /// </summary>
        /// <remarks>
        /// <para><b>P1~P3 的临时行为：</b>客户端从命令行直接进入战局地图，跳过安全屋。
        /// P4 的大厅会让玩家在安全屋里点"出击"再进战局，届时这条路径只服务于自动化测试。</para>
        ///
        /// <para>之所以要在这里切场景，是因为联机客户端的装配发生在战局场景的启动类里：
        /// 构建列表的第一个场景是安全屋，那里没有联机装配。</para>
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartClientRuntime()
        {
            if (!ClientMode.IsActive)
            {
                return;
            }

            var mapScene = ClientMode.Options != null ? ClientMode.Options.MapSceneName : null;
            if (string.IsNullOrEmpty(mapScene) || SceneManager.GetActiveScene().name == mapScene)
            {
                return;
            }

            Debug.Log($"[启动] 联机客户端加载地图：{mapScene}");
            SceneManager.LoadScene(mapScene);
        }
    }
}
