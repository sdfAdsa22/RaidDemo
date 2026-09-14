using RaidDemo.Kernel;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 专用服务器运行时：在没有场景、没有表现层的进程里让权威逻辑跑起来。
    /// </summary>
    /// <remarks>
    /// <para><b>它负责什么：</b>建立会话作用域、配置并启动 Netcode for GameObjects 的监听、
    /// 把「别人该连哪里」打印给房主看、以及在退出时干净地收摊。</para>
    ///
    /// <para><b>它不负责什么：</b>P0 阶段服务器只到「能起来、能监听、能报地址」为止——
    /// 战局世界（地图、AI、容器）的装配排在 P2 / P3，届时由服务器侧的会话装配补上。
    /// 这样安排是为了让第一批改动可验证：网络地基没跑通之前就往上堆内容，
    /// 出问题时无法判断是地基还是内容。</para>
    ///
    /// <para><b>为什么关闭场景管理：</b><c>EnableSceneManagement = false</c> 让服务器不通过网络
    /// 推送场景加载事件。本项目的地图由构建器脚本生成、两端各自加载同一张图，
    /// 不需要引擎帮我们同步场景；关掉它能少一整类「加载时序不一致」的故障。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class ServerRuntime : MonoBehaviour
    {
        /// <summary>心跳日志间隔（秒）。长时间没有输出会让人怀疑服务器是不是死了。</summary>
        private const float HeartbeatSeconds = 30f;

        /// <summary>监听全部网卡。只绑定回环的话，局域网与云主机都连不上。</summary>
        private const string ListenAddress = "0.0.0.0";

        private SessionScope m_Session;
        private NetworkManager m_Network;
        private UnityTransport m_Transport;
        private LaunchOptions m_Options;
        private float m_NextHeartbeatTime;
        private bool m_ShutdownRequested;

        /// <summary>服务器侧会话作用域，供后续批次装配战局世界。</summary>
        public SessionScope Session => m_Session;

        /// <summary>网络管理器。P1 起用它注册可同步对象。</summary>
        public NetworkManager Network => m_Network;

        /// <summary>启动参数。</summary>
        public LaunchOptions Options => m_Options;

        /// <summary>是否正在监听。</summary>
        public bool IsListening => m_Network != null && m_Network.IsListening;

        /// <summary>
        /// 当前进程内的服务器运行时（单例）。
        /// </summary>
        /// <remarks>
        /// 网络回调由框架触发，拿不到构造参数，因此只能通过静态引用转交给权威世界。
        /// 一个进程只会有一个服务器实例，这个假设是成立的。
        /// </remarks>
        public static ServerRuntime Active { get; private set; }

        /// <summary>
        /// 创建并启动一个服务器运行时。
        /// </summary>
        /// <remarks>
        /// 返回的宿主对象标记为跨场景存活：服务器是常驻进程，不该因为场景切换而消失。
        /// </remarks>
        public static ServerRuntime Create(LaunchOptions options)
        {
            var host = new GameObject("DedicatedServer");

            // 跨场景存活只在播放模式下有意义：EditMode 测试同样会调用本方法，
            // 而在编辑模式里调用 DontDestroyOnLoad 会直接抛异常。
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(host);
            }

            var runtime = host.AddComponent<ServerRuntime>();
            runtime.Initialize(options);
            return runtime;
        }

        /// <summary>初始化会话与网络监听。</summary>
        private void Initialize(LaunchOptions options)
        {
            m_Options = options;
            Active = this;
            m_Session = SessionScope.CreateDedicatedServer(options.MinimumLogLevel);

            var log = m_Session.Log;
            log.Info($"[服务器] 启动参数：{options.Describe()}");

            foreach (var warning in options.Warnings)
            {
                log.Warning($"[服务器] {warning}");
            }

            m_Network = gameObject.AddComponent<NetworkManager>();
            m_Transport = gameObject.AddComponent<UnityTransport>();

            // 以 -logLevel verbose 启动时打开 NGO 的开发者日志：
            // 连接建立失败时，只有框架自己的日志能说明卡在哪一步（握手、审批还是传输层）。
            m_Network.LogLevel = options.MinimumLogLevel == RaidDemo.Kernel.LogLevel.Verbose
                ? Unity.Netcode.LogLevel.Developer
                : Unity.Netcode.LogLevel.Normal;

            // SetConnectionData(远端地址, 端口, 监听地址)：
            // 服务器只用得到「监听地址」这一侧；远端地址填回环即可，它只对客户端连接有意义。
            m_Transport.SetConnectionData(ListenAddress, (ushort)options.Port, ListenAddress);

            // 网络配置与客户端共用同一份工厂：两端不一致时 NGO 会在握手阶段直接断开，
            // 且只在开发者日志里留一条极难发现的警告（M9-P-11）。
            m_Network.NetworkConfig = NetworkConfigFactory.Create(m_Transport);

            if (!m_Network.StartServer())
            {
                log.Error($"[服务器] 监听失败：端口 {options.Port} 可能已被占用，或被系统防火墙拦截。");
                return;
            }

            log.Info($"[服务器] 房间「{options.RoomName}」已就绪，端口 {options.Port}。");
            log.Info(ServerAddressReporter.BuildReport(options.Port));
            log.Info($"[服务器] 存档目录：{options.SaveDirectory}（P5 起用于按账号隔离的进度落库）");

            // 大厅必须先于移动世界建立：连接事件的第一订阅者是大厅——
            // "客户端接入"在大厅里只是登记登录态，真正进入地图发生在开局（P4）。
            InitializeLobby();

            InitializeMovement();

            // P4.5-b：服务器先托管共享安全屋——联机首站永远是安全屋，
            // 战局地图只在房主确认出击（或 -autostart 到点）时才加载。
            // 本帧活动场景就是安全屋（构建列表第 0 个），因此这次切换会立刻完成。
            EnterSafeHouseWorld();

            m_NextHeartbeatTime = Time.unscaledTime + HeartbeatSeconds;
        }

        /// <summary>
        /// 心跳：定期报告在线人数，让运维侧能从日志看出服务器是否还活着。
        /// </summary>
        private void Update()
        {
            if (m_Network == null || m_Session == null || !m_Network.IsListening)
            {
                return;
            }

            // 权威世界的推进独立于心跳：每帧都要走，心跳只是周期性日志。
            TickWorld();
            TickLobby();
            TickServerIntegrations();
            TickNavigation();
            TickContainers();
            TickMovement(Time.deltaTime);
            TickAi(Time.deltaTime);
            TickRaid();

            if (Time.unscaledTime < m_NextHeartbeatTime)
            {
                return;
            }

            m_NextHeartbeatTime = Time.unscaledTime + HeartbeatSeconds;
            var online = m_Network.ConnectedClientsIds.Count;
            m_Session.Log.Info($"[服务器] 心跳：房间「{m_Options.RoomName}」在线 {online} 人。");
        }

        /// <summary>
        /// 停止监听并释放会话。可重复调用。
        /// </summary>
        /// <remarks>
        /// 先停网络再释放会话：断开连接会触发 NGO 的回调，
        /// 那些回调仍需要会话服务（事件总线、日志）在线，顺序反了会在退出时报空引用。
        /// </remarks>
        public void Shutdown()
        {
            if (m_ShutdownRequested)
            {
                return;
            }

            m_ShutdownRequested = true;

            if (m_Network != null && m_Network.IsListening)
            {
                m_Session?.Log.Info("[服务器] 停止监听。");
                m_Network.Shutdown();
            }

            ShutdownLobby();
            ShutdownMovement();

            m_Session?.Dispose();
            m_Session = null;
            m_Network = null;
            m_Transport = null;

            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
