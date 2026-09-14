using System;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机会话的"自动重连"部分（排障手册 P-51 的客户端半边）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么客户端必须自动重连：</b>服务器侧的兜底是"全员静默时重建传输层"
    /// （见 <c>ServerRuntime.TransportWatchdog</c>）——重建会把所有人干净地断开，
    /// 在局玩家的位置与背包进入宽限。如果客户端断开后只是回到主菜单等人来点，
    /// 那这次自愈就白做了；只有客户端自己安静地连回去，玩家体验才是"卡了几秒又恢复"。</para>
    ///
    /// <para><b>什么时候不重连：</b>玩家主动断开（点「返回主菜单」/「退出联机」）时不重连；
    /// 从没进入过会话的连接（第一次连接就失败）也不重连——那种失败要给明确的错误提示，
    /// 而不是无限重试。</para>
    ///
    /// <para><b>与服务器宽限期的配合：</b>服务器默认保留 60 秒（<c>-grace</c>），
    /// 客户端的重连窗口取 90 秒：必须比服务器宽限更长——差的那 30 秒正是"最后一次尝试
    /// 正好赶上宽限尾巴"的时间。重连回来的登录走的是原口令或本地 token，
    /// 服务器按昵称在宽限名册里找到旧记录并换绑（<c>TryResumeGracedSession</c>）。</para>
    /// </remarks>
    public sealed partial class MultiplayerClientSession
    {
        /// <summary>
        /// 客户端自动重连的总窗口（秒）。
        /// </summary>
        /// <remarks>
        /// 比服务器默认宽限（60 秒）长一截：多出来的时间用来覆盖
        /// "最后一次尝试刚好在宽限末尾"的边界，以及"服务器重建完成比预期慢"的情况。
        /// </remarks>
        private const float ReconnectWindowSeconds = 90f;

        /// <summary>断开后第一次尝试的延迟（秒）。留一小段给服务器重建传输层。</summary>
        private const float ReconnectFirstDelaySeconds = 1.5f;

        /// <summary>两次尝试之间的间隔（秒）。</summary>
        private const float ReconnectRetryIntervalSeconds = 2f;

        /// <summary>单次重连握手的最长等待（秒）。比首次连接的 8 秒短：失败要尽快让位给下一次。</summary>
        private const float ReconnectAttemptTimeoutSeconds = 3f;

        /// <summary>重连之后等待登录结果的最长时间（秒）。超时按一次失败处理，重新来。</summary>
        private const float ReconnectLoginTimeoutSeconds = 5f;

        /// <summary>
        /// 已从一次断线中重连回来（登录成功）。
        /// </summary>
        /// <remarks>
        /// 场景装配层（<c>RaidFlowController</c>）订阅它：断线期间 NGO 把命名消息处理器全部清空了，
        /// 重连成功时需要一个时机把当前场景重新装起来。事件不带参数——
        /// "当前在哪个场景"由订阅方自己读，这正是它要重载的那一个。
        /// </remarks>
        public event Action ResumedFromReconnect;

        /// <summary>当前是否处于自动重连过程中。</summary>
        public bool IsReconnecting => m_Reconnecting;

        /// <summary>本次断线以来已经尝试过几次重连（界面提示用）。</summary>
        public int ReconnectAttempts => m_ReconnectAttempts;

        /// <summary>是否处于自动重连流程中。</summary>
        private bool m_Reconnecting;

        /// <summary>
        /// 玩家主动断开（<see cref="Disconnect"/>）时为 true，用于区分"意外掉线"。
        /// </summary>
        /// <remarks>下一次发起连接时清零，因此它只对"这一次断开"负责。</remarks>
        private bool m_IntentionalDisconnect;

        /// <summary>重连窗口的截止时刻；&lt;= 0 表示没有进行中的重连。</summary>
        private float m_ReconnectDeadline = -1f;

        /// <summary>下一次尝试的时刻；&lt;= 0 表示没有安排。</summary>
        private float m_NextReconnectAt = -1f;

        /// <summary>等待登录结果的截止时刻（连接已建立但登录还没回）；&lt;= 0 表示不在等。</summary>
        private float m_ReconnectLoginDeadline = -1f;

        /// <summary>本次断线以来的尝试次数。</summary>
        private int m_ReconnectAttempts;

        /// <summary>
        /// 尝试进入自动重连流程（由断线回调在"非主动断开且曾进入会话"时调用）。
        /// </summary>
        /// <returns>已进入重连流程返回 true；条件不满足返回 false（调用方按普通断开处理）。</returns>
        internal bool TryBeginAutoReconnect()
        {
            if (m_Reconnecting || m_IntentionalDisconnect)
            {
                return false;
            }

            if (string.IsNullOrEmpty(Address) || string.IsNullOrEmpty(Nickname))
            {
                // 没有可用的地址或昵称：这不是"掉线"，是"还没成功建立过会话"，无从重连。
                return false;
            }

            m_Reconnecting = true;
            m_ReconnectAttempts = 0;
            m_ReconnectDeadline = Time.realtimeSinceStartup + ReconnectWindowSeconds;
            m_ReconnectLoginDeadline = -1f;
            ScheduleNextAttempt(ReconnectFirstDelaySeconds);

            SetPhase(MultiplayerClientPhase.Reconnecting);
            StatusText = "连接断开，正在重连…";
            RaiseChanged();

            Debug.Log(
                $"[联机] 连接断开：{ReconnectFirstDelaySeconds:F1} 秒后开始自动重连，"
                + $"最多持续 {ReconnectWindowSeconds:F0} 秒（服务器宽限期内）。");
            return true;
        }

        /// <summary>每帧推进重连流程（由 <c>Update</c> 调用）。</summary>
        private void TickReconnect()
        {
            if (!m_Reconnecting)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;

            if (Phase == MultiplayerClientPhase.LoggingIn)
            {
                // 连接建好了但登录结果还没回（服务器可能正在重建、把这条连接又断了）。
                if (now >= m_ReconnectDeadline)
                {
                    AbortReconnect($"自动重连超时（{ReconnectWindowSeconds:F0} 秒）：{Address}。");
                    return;
                }

                if (m_ReconnectLoginDeadline > 0f && now >= m_ReconnectLoginDeadline)
                {
                    m_ReconnectLoginDeadline = -1f;
                    Debug.LogWarning("[联机] 重连后登录结果超时未达，重新尝试。");
                    ShutdownNetworkQuietly();
                    OnReconnectAttemptFailed();
                }

                return;
            }

            if (now >= m_ReconnectDeadline)
            {
                AbortReconnect($"自动重连超时（{ReconnectWindowSeconds:F0} 秒）：{Address}。");
                return;
            }

            if (Phase == MultiplayerClientPhase.Connecting)
            {
                // 一次连接尝试正在进行；它的超时由 TickConnectTimeout 处理（那里会回到本流程）。
                return;
            }

            if (m_NextReconnectAt > 0f && now >= m_NextReconnectAt)
            {
                AttemptReconnect();
            }
        }

        /// <summary>发起一次重连尝试（复用与首次连接完全相同的入口）。</summary>
        private void AttemptReconnect()
        {
            m_ReconnectAttempts++;
            m_NextReconnectAt = -1f;

            StatusText = $"连接断开，正在重连（第 {m_ReconnectAttempts} 次）…";
            RaiseChanged();

            if (!BeginConnect(Address, Nickname, m_PendingPassphrase, isReconnect: true))
            {
                ScheduleNextAttempt(ReconnectRetryIntervalSeconds);
                return;
            }

            // 重连的握手超时更短：服务器重建一般一两秒完成，3 秒连不上就让下一次尝试再来。
            m_ConnectDeadline = Time.realtimeSinceStartup + ReconnectAttemptTimeoutSeconds;
            Debug.Log($"[联机] 自动重连第 {m_ReconnectAttempts} 次：{Address}（昵称「{Nickname}」）。");
        }

        /// <summary>安排下一次尝试。</summary>
        /// <param name="delay">延后多少秒。</param>
        private void ScheduleNextAttempt(float delay)
        {
            m_NextReconnectAt = Time.realtimeSinceStartup + delay;
        }

        /// <summary>
        /// 一次重连尝试失败（握手失败、被断开、登录超时都会走到这里）。
        /// </summary>
        /// <remarks>
        /// 失败不结束流程：状态回到"重连中"并安排下一次尝试，直到成功或超出总窗口。
        /// 这里不写 <c>LastError</c>，避免界面在重试期间反复闪错误红字——
        /// 真正失败到不了才由 <see cref="AbortReconnect"/> 给出终局提示。
        /// </remarks>
        internal void OnReconnectAttemptFailed()
        {
            if (!m_Reconnecting)
            {
                return;
            }

            m_ConnectDeadline = -1f;
            m_ReconnectLoginDeadline = -1f;
            Phase = MultiplayerClientPhase.Reconnecting;
            ScheduleNextAttempt(ReconnectRetryIntervalSeconds);
            RaiseChanged();
        }

        /// <summary>
        /// 登录成功后的收尾：结束重连状态并通知装配层恢复场景。
        /// </summary>
        /// <remarks>由大厅登录结果处理器在成功分支调用（普通登录时本方法直接返回）。</remarks>
        internal void OnLoginSucceededAfterReconnect()
        {
            if (!m_Reconnecting)
            {
                return;
            }

            m_Reconnecting = false;
            m_ReconnectLoginDeadline = -1f;
            m_NextReconnectAt = -1f;
            var attempts = m_ReconnectAttempts;
            m_ReconnectAttempts = 0;

            Debug.Log($"[联机] 已重连回服务器（共尝试 {attempts} 次），开始恢复场景装载。");
            ResumedFromReconnect?.Invoke();
        }

        /// <summary>标记"这一次断开是玩家主动的"，由 <see cref="Disconnect"/> 调用。</summary>
        internal void MarkIntentionalDisconnect()
        {
            m_IntentionalDisconnect = true;
            m_Reconnecting = false;
            m_ReconnectDeadline = -1f;
            m_NextReconnectAt = -1f;
            m_ReconnectLoginDeadline = -1f;
            m_ReconnectAttempts = 0;
        }

        /// <summary>结束重连并给出终局提示。</summary>
        /// <param name="error">展示给玩家的原因。</param>
        private void AbortReconnect(string error)
        {
            m_Reconnecting = false;
            m_ReconnectDeadline = -1f;
            m_NextReconnectAt = -1f;
            m_ReconnectLoginDeadline = -1f;

            ShutdownNetworkQuietly();

            SetPhase(MultiplayerClientPhase.Offline);
            StatusText = string.Empty;
            SetError(error);
        }

        /// <summary>安静地关掉网络层（已经断开时是空操作）。</summary>
        private void ShutdownNetworkQuietly()
        {
            var network = m_Network;
            if (network != null && network.IsListening)
            {
                network.Shutdown();
            }
        }
    }
}
