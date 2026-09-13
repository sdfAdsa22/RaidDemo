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
    public sealed class ServerRuntime : MonoBehaviour
    {
        /// <summary>
        /// 服务器仿真频率（Hz）。
        /// </summary>
        /// <remarks>
        /// 取 60 与设计文档第 7.9 节的固定 Tick 一致：移动模拟在 60 Hz 下步进，
        /// 客户端预测才能用同样的步长复现服务器结果。
        /// </remarks>
        private const uint ServerTickRate = 60;

        /// <summary>心跳日志间隔（秒）。长时间没有输出会让人怀疑服务器是不是死了。</summary>
        private const float HeartbeatSeconds = 30f;

        /// <summary>监听全部网卡。只绑定回环的话，局域网与云主机都连不上。</summary>
        private const string ListenAddress = "0.0.0.0";

        private SessionScope m_Session;
        private NetworkManager m_Network;
        private UnityTransport m_Transport;
        private ServerLaunchOptions m_Options;
        private float m_NextHeartbeatTime;
        private bool m_ShutdownRequested;

        /// <summary>服务器侧会话作用域，供后续批次装配战局世界。</summary>
        public SessionScope Session => m_Session;

        /// <summary>网络管理器。P1 起用它注册可同步对象。</summary>
        public NetworkManager Network => m_Network;

        /// <summary>启动参数。</summary>
        public ServerLaunchOptions Options => m_Options;

        /// <summary>是否正在监听。</summary>
        public bool IsListening => m_Network != null && m_Network.IsListening;

        /// <summary>
        /// 创建并启动一个服务器运行时。
        /// </summary>
        /// <remarks>
        /// 返回的宿主对象标记为跨场景存活：服务器是常驻进程，不该因为场景切换而消失。
        /// </remarks>
        public static ServerRuntime Create(ServerLaunchOptions options)
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
        private void Initialize(ServerLaunchOptions options)
        {
            m_Options = options;
            m_Session = SessionScope.CreateDedicatedServer(options.MinimumLogLevel);

            var log = m_Session.Log;
            log.Info($"[服务器] 启动参数：{options.Describe()}");

            foreach (var warning in options.Warnings)
            {
                log.Warning($"[服务器] {warning}");
            }

            m_Network = gameObject.AddComponent<NetworkManager>();
            m_Transport = gameObject.AddComponent<UnityTransport>();

            // SetConnectionData(远端地址, 端口, 监听地址)：
            // 服务器只用得到「监听地址」这一侧；远端地址填回环即可，它只对客户端连接有意义。
            m_Transport.SetConnectionData(ListenAddress, (ushort)options.Port, ListenAddress);

            m_Network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = m_Transport,
                TickRate = ServerTickRate,

                // P0 不做入房审批：密码校验与账号登录排在 P4。
                // 人数上限（2~4 人）届时由审批回调拒绝超额连接——NGO 2.13 的 NetworkConfig 没有独立的人数字段。
                ConnectionApproval = false,

                EnableSceneManagement = false,
            };

            if (!m_Network.StartServer())
            {
                log.Error($"[服务器] 监听失败：端口 {options.Port} 可能已被占用，或被系统防火墙拦截。");
                return;
            }

            log.Info($"[服务器] 房间「{options.RoomName}」已就绪，端口 {options.Port}。");
            log.Info(ServerAddressReporter.BuildReport(options.Port));
            log.Info($"[服务器] 存档目录：{options.SaveDirectory}（P5 起用于按账号隔离的进度落库）");

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

            m_Session?.Dispose();
            m_Session = null;
            m_Network = null;
            m_Transport = null;
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
