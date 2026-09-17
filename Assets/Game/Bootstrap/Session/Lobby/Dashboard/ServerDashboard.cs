using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 内置状态页：用浏览器查看服务器房间、玩家与最近日志，并在本机执行停止操作。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>专用服务器没有窗口。人不在那台机器前的时候，
    /// "服务器是不是还活着、房间里有谁、刚才报了什么错"这三个问题在 SSH 之外没有别的答案。
    /// 状态页把这三件事变成一次刷新。</para>
    ///
    /// <para><b>线程模型：</b>Unity 的对象只能在主线程碰，而 HTTP 请求随时可能到达。
    /// 因此收包在后台线程（只做网络 I/O），处理在主线程（取快照、执动作），
    /// 两边通过一个队列与一个信号量交接。<b>绝不在后台线程里读服务器状态</b>——
    /// 那正是快照类型存在的原因。</para>
    ///
    /// <para><b>失败不拖垮服务器：</b>端口被占用、没有权限绑定时，状态页只记录一条错误并停用自己。
    /// 一台起不来的服务器没法排查问题，而一个打不开的状态页只是少了个工具。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class ServerDashboard : MonoBehaviour
    {
        /// <summary>默认端口，与 <see cref="LaunchOptions.DefaultDashboardPort"/> 保持一致。</summary>
        public const int DefaultPort = 8080;

        /// <summary>请求行的最大长度（字节）。状态页只有 GET，且参数极短。</summary>
        private const int MaxRequestBytes = 4096;

        /// <summary>socket 读写超时（毫秒）。慢客户端不能把服务器拖住。</summary>
        private const int SocketTimeoutMilliseconds = 3000;

        /// <summary>主线程每帧最多处理多少条请求，避免突然涌入时卡帧。</summary>
        private const int MaxRequestsPerFrame = 8;

        /// <summary>后台线程等待主线程处理结果的时限（毫秒）。</summary>
        private const int HandoffTimeoutMilliseconds = 2500;

        /// <summary>一条等待主线程处理的请求。</summary>
        private sealed class PendingRequest
        {
            /// <summary>请求行（形如 <c>GET / HTTP/1.1</c>）。</summary>
            public string RequestLine;

            /// <summary>发起方地址（取自 socket）。</summary>
            public string RemoteAddress;

            /// <summary>主线程写回的响应。</summary>
            public DashboardResponse Response;

            /// <summary>请求头字段（管理口令从这里读）。</summary>
            public Dictionary<string, string> Headers;

            /// <summary>主线程处理完毕的信号。</summary>
            public readonly ManualResetEventSlim Completed = new ManualResetEventSlim(false);
        }

        private readonly ConcurrentQueue<PendingRequest> m_Inbox = new ConcurrentQueue<PendingRequest>();

        private TcpListener m_Listener;
        private Thread m_AcceptThread;
        private Func<ServerStatusSnapshot> m_SnapshotProvider;
        private Func<string, string, string> m_ActionHandler;
        private string m_AdminToken = string.Empty;
        private volatile bool m_StopRequested;

        /// <summary>状态页是否正在监听。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>实际使用的端口；未启动时为 0。</summary>
        public int Port { get; private set; }

        /// <summary>启动失败的原因；正常时为 null。</summary>
        public string LastError { get; private set; }

        /// <summary>已处理的请求数（运维与测试观察用）。</summary>
        public int RequestsHandled { get; private set; }

        /// <summary>
        /// 启动状态页。
        /// </summary>
        /// <param name="port">监听端口；0 表示关闭状态页。</param>
        /// <param name="snapshotProvider">取当前状态快照（在主线程执行）。</param>
        /// <param name="actionHandler">执行运维动作（动作名、参数）；返回 null 或空串表示成功。</param>
        /// <param name="adminToken">
        /// 远程写操作所需的管理口令；空表示写操作仅限服务器本机。
        /// </param>
        public void Initialize(
            int port,
            Func<ServerStatusSnapshot> snapshotProvider,
            Func<string, string, string> actionHandler,
            string adminToken = null)
        {
            if (IsRunning)
            {
                Shutdown();
            }

            // 允许"停止后再启动"（编辑器里反复进入播放模式就是这种用法）。
            m_StopRequested = false;
            m_SnapshotProvider = snapshotProvider;
            m_ActionHandler = actionHandler;
            m_AdminToken = adminToken ?? string.Empty;

            if (port <= 0)
            {
                Debug.Log("[服务器] 状态页已关闭（-dashboardPort 0）。");
                return;
            }

            LastError = null;

            try
            {
                m_Listener = new TcpListener(IPAddress.Any, port);
                m_Listener.Start();
            }
            catch (Exception exception)
            {
                LastError = $"{exception.GetType().Name} — {exception.Message}";
                Debug.LogWarning(
                    $"[服务器] 状态页监听 {port} 失败，已跳过状态页（不影响联机）：{LastError}");
                m_Listener = null;
                return;
            }

            Port = port;
            IsRunning = true;

            m_AcceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "RaidDemoDashboard",
            };
            m_AcceptThread.Start();

            Debug.Log(string.IsNullOrEmpty(m_AdminToken)
                ? $"[服务器] 状态页已就绪：http://127.0.0.1:{port}/ （写操作仅限本机；配置 adminToken 后可远程管理）"
                : $"[服务器] 状态页已就绪：http://127.0.0.1:{port}/ （写操作需管理口令，可远程管理）");
        }

        /// <summary>停止监听并等待后台线程退出。可重复调用。</summary>
        public void Shutdown()
        {
            m_StopRequested = true;

            try
            {
                m_Listener?.Stop();
            }
            catch (Exception)
            {
                // 关闭监听失败不影响退出流程：线程是后台线程，进程结束时会被回收。
            }

            if (m_AcceptThread != null && m_AcceptThread.IsAlive)
            {
                m_AcceptThread.Join(500);
            }

            m_AcceptThread = null;
            m_Listener = null;
            IsRunning = false;
            Port = 0;
        }

        /// <summary>
        /// 每帧处理排队中的请求。
        /// </summary>
        /// <remarks>
        /// 请求在后台线程读出来、在这里回答：快照与动作都必须发生在主线程。
        /// </remarks>
        private void Update()
        {
            if (!IsRunning)
            {
                return;
            }

            var handled = 0;
            while (handled < MaxRequestsPerFrame && m_Inbox.TryDequeue(out var pending))
            {
                handled++;
                HandleOnMainThread(pending);
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        /// <summary>主线程：跑路由并唤醒等待中的后台线程。</summary>
        private void HandleOnMainThread(PendingRequest pending)
        {
            try
            {
                pending.Response = DashboardRouter.Handle(
                    pending.RequestLine,
                    pending.Headers,
                    pending.RemoteAddress,
                    m_SnapshotProvider,
                    m_ActionHandler,
                    m_AdminToken);
                RequestsHandled++;

                if (!string.IsNullOrEmpty(pending.Response.ActionTaken))
                {
                    Debug.Log($"[服务器] 状态页执行了操作：{pending.Response.ActionTaken}。");
                }
            }
            catch (Exception exception)
            {
                // 状态页永远不能让服务器崩：任何异常都变成一条 500。
                Debug.LogError($"[服务器] 状态页处理请求失败：{exception}");
                pending.Response = new DashboardResponse(500, DashboardRenderer.TextContentType, "服务器内部错误。");
            }
            finally
            {
                pending.Completed.Set();
            }
        }

    }
}
