using System;
using UnityEngine;

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

        /// <summary>服务器启动参数。仅当 <see cref="IsActive"/> 为 true 时有效。</summary>
        public static ServerLaunchOptions Options { get; private set; }

        /// <summary>由启动入口激活服务器模式。</summary>
        internal static void Activate(ServerLaunchOptions options)
        {
            IsActive = true;
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
            if (!ServerLaunchOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out var error))
            {
                Debug.LogError($"[启动] 服务器参数解析失败：{error}");

                if (!Application.isEditor)
                {
                    Application.Quit(1);
                }

                return;
            }

            if (!options.IsServerRequested)
            {
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
    }
}
