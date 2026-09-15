using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"传输层自愈"部分（排障手册 P-51 的应用层兜底）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要这一层（而不是修传输层）：</b>P-51 的取证结论是——某个客户端进程被强制结束后，
    /// 服务器的 UDP **接收路径**会整体失效：发送照常、主线程不卡、也没有任何发送错误，
    /// 但服务器再也收不到任何一个客户端的上行，10 秒后 NGO 按协议超时把所有人一起踢掉
    /// （玩家看到的是"队友掉了，我也被踢，房间解散"）。这一层在 UTP/Baselib 的原生 socket 里，
    /// 应用层改不动。既然修不了，就让服务器**自己发现并重开**：判定"全员静默"之后
    /// 关掉 Netcode 再重新监听同一个端口，已经在局里的玩家进宽限期，客户端自动重连回来接管。</para>
    ///
    /// <para><b>为什么不重建 NetworkManager 组件：</b>服务器进程里有 40 多个文件在通过
    /// <c>m_Network</c> 访问网络管理器（含状态页、局域网发现、AI 上报）。销毁组件再新建会让这些引用
    /// 全部变成"已销毁对象"，表现为一堆空引用；而 NGO 本身就支持
    /// <c>Shutdown() → StartServer()</c> 这条重启路径（<c>NetworkManager</c> 上的连接 / 断开事件订阅
    /// 也存活下来）。因此这里只重启会话，**重建的是传输层与消息处理器**，
    /// 与方案的叫法一致（"重建 NetworkManager 的传输层"）。</para>
    ///
    /// <para><b>重启时必须补的一件事：</b>NGO 关闭时会把 <c>CustomMessagingManager</c> 置空
    /// 并在重新启动时新建，因此**命名消息处理器全部要重新注册**（<see cref="RegisterServerMessageHandlers"/>）。
    /// 漏掉它的症状是"重连上来了，但输入、容器、大厅请求全部石沉大海"——比掉线更难查。</para>
    ///
    /// <para><b>与宽限的关系：</b>重建会让所有人断开，因此重建前先给名册上的人打上宽限标记
    /// （<see cref="KeepRosterOnReconnect"/>）。他们回来时走的正是 P5 已有的"重连接管"
    /// （<c>ServerRuntime.Reconnect.cs</c>）：位置、背包、战局进度一起搬过去。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 关闭传输层之后、重新监听之前的最小间隔（秒）。
        /// </summary>
        /// <remarks>
        /// 留一点点时间让操作系统把 UDP 端口彻底放开。同一帧里"关了立刻开"在多数机器上也能成功，
        /// 但那属于依赖时序的巧合；0.2 秒的代价换掉一整类"端口偶尔被占用"的偶发失败。
        /// </remarks>
        private const float RebuildSettleSeconds = 0.2f;

        /// <summary>
        /// 关闭传输层的等待上限（秒）。超过它还在"关闭中"就打一条错误日志——那说明 NGO 卡住了，
        /// 不是本类的逻辑问题，必须让人看见。
        /// </summary>
        private const float RebuildTimeoutSeconds = 5f;

        /// <summary>全员静默看门狗（纯逻辑，规则在 <see cref="TransportWatchdog"/> 里）。</summary>
        private TransportWatchdog m_Watchdog;

        /// <summary>是否正在重建传输层（重建期间每帧推进 <see cref="TickTransportRebuild"/>）。</summary>
        private bool m_TransportRebuildPending;

        /// <summary>本次重建的开始时刻（<c>Time.realtimeSinceStartup</c>）。</summary>
        private float m_TransportRebuildStartedAt;

        /// <summary>累计重建次数（日志与状态页用；验收脚本据此判断"确实自愈过"）。</summary>
        private int m_TransportRebuildCount;

        /// <summary>累计重建次数；供状态页与诊断读取。</summary>
        internal int TransportRebuildCount => m_TransportRebuildCount;

        /// <summary>
        /// 建立看门狗（在监听成功之后调用；<c>-watchdog 0</c> 时关闭）。
        /// </summary>
        private void InitializeTransportWatchdog()
        {
            if (m_Options == null || m_Options.TransportWatchdogSeconds <= 0f)
            {
                m_Watchdog = null;
                m_Session?.Log.Warning(
                    "[服务器] 全员静默看门狗已关闭（-watchdog 0）：上行接收路径被打哑时不会自愈，"
                    + "只能等 NGO 的协议超时把人踢掉。");
                return;
            }

            m_Watchdog = new TransportWatchdog(m_Options.TransportWatchdogSeconds);

            m_Session?.Log.Info(
                $"[服务器] 全员静默看门狗已启用：局里有人、且连续 {m_Watchdog.AllSilentSeconds:F1} 秒"
                + "收不到任何客户端上行时重建传输层（P-51 的应用层兜底）。");
        }

        /// <summary>
        /// 每帧推进：判定接收路径是否整体失效，以及重建流程走到哪一步。
        /// </summary>
        /// <remarks>
        /// 必须在 <c>Update</c> 的"未监听就返回"那道门**之前**调用：重建期间
        /// <c>IsListening</c> 恰好是 false，放在门后会让重建卡在第二步永远走不下去。
        /// </remarks>
        private void TickTransportWatchdog()
        {
            if (m_Network == null || m_Options == null)
            {
                return;
            }

            if (m_TransportRebuildPending)
            {
                TickTransportRebuild();
                return;
            }

            if (m_Watchdog == null || !m_Network.IsListening)
            {
                return;
            }

            var liveClients = 0;
            var talking = 0;

            foreach (var pair in m_LobbyClients)
            {
                // 宽限中的人已经不在连接表里，本来就不该有上行——他不参与"全员静默"的判定，
                // 否则"队友掉线、只剩我一个人"会永远算作静默。
                if (pair.Value.InGrace || !IsClientConnected((ulong)pair.Key))
                {
                    continue;
                }

                liveClients++;

                // IsClientSilent 对"从没说过话"的连接返回 false（刚连上、正在加载场景），
                // 因此这颗"还在说话"的计票同时保护了进图那一刻的加载停顿。
                if (!IsClientSilent(pair.Key))
                {
                    talking++;
                }
            }

            var now = Time.realtimeSinceStartup;
            var worldHasPlayers = m_World != null && m_World.PlayerCount > 0;

            if (!m_Watchdog.ShouldRebuildTransport(worldHasPlayers, liveClients > 0, talking > 0, now))
            {
                // 空房兜底：没有任何在线连接、但有人在宽限里等重连时也要重建一次——
                // 接收路径哑掉后新连接同样建不起来（P-51 实测），不重建等于让他们全部错过宽限。
                if (!m_Watchdog.ShouldRebuildWhileEmpty(HasPlayerWaitingForReconnect(), now))
                {
                    return;
                }

                BeginTransportRebuild(emptyRoom: true);
                return;
            }

            BeginTransportRebuild(emptyRoom: false);
        }

        /// <summary>是否有人在掉线宽限里等待重连。</summary>
        private bool HasPlayerWaitingForReconnect()
        {
            foreach (var pair in m_LobbyClients)
            {
                if (pair.Value.InGrace)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判定成立：关闭传输层，准备重新监听。
        /// </summary>
        /// <param name="emptyRoom">
        /// true 表示触发原因是"空房里有人在宽限中等待"（没有在线连接，也就没有"全员静默"读数）。
        /// </param>
        private void BeginTransportRebuild(bool emptyRoom)
        {
            var now = Time.realtimeSinceStartup;
            var silentSeconds = m_Watchdog.SilentSeconds(now);

            // 先记"重建已开始"，再动网络：重启过程本身要几帧，这段静默不能再触发一次判定。
            m_Watchdog.NoteRebuildStarted(now);

            m_TransportRebuildPending = true;
            m_TransportRebuildStartedAt = now;

            var reasonText = emptyRoom
                ? "空房且有人在宽限中等待重连"
                : $"全员静默 {silentSeconds:F1} 秒";

            m_Session?.Log.Warning(
                $"[服务器] {reasonText}：判定上行接收路径已失效（P-51），"
                + $"开始重建传输层（第 {m_TransportRebuildCount + 1} 次）——"
                + "在局玩家的位置与背包照旧保留，等他们自动重连。");

            // 先打宽限标记再关网：断开回调要等 NGO 走完关闭流程才触发，
            // 而"关掉之后、重新监听之前"服务器的连接表是空的。若此刻有客户端刚好重连进来，
            // 没有宽限标记的名册会把他判成"昵称还在线"而拒绝重连——自愈反而变成"再也回不来"。
            KeepRosterOnReconnect("传输层重建");

            // 用默认的优雅关闭：NGO 会先给每个客户端发一条带原因的断开消息（原因里写明是服务器关闭），
            // 客户端据此可以立刻开始自动重连，而不是等自己的 10 秒超时。
            m_Network.Shutdown();
        }

        /// <summary>
        /// 重建的第二步：等 NGO 真正关完之后，按同样的参数重新监听并补回消息处理器。
        /// </summary>
        private void TickTransportRebuild()
        {
            if (m_Network == null)
            {
                m_TransportRebuildPending = false;
                return;
            }

            // NGO 的关闭是分帧完成的（Shutdown → 逐个断开客户端 → 内部收尾），
            // 这两条没过完就 StartServer 会被 CanStart 直接拒绝（"Can't start while listening"）。
            if (m_Network.ShutdownInProgress || m_Network.IsListening)
            {
                var waited = Time.realtimeSinceStartup - m_TransportRebuildStartedAt;
                if (waited > RebuildTimeoutSeconds)
                {
                    m_Session?.Log.Error(
                        $"[服务器] 传输层关闭超过 {RebuildTimeoutSeconds:F0} 秒仍未完成（NGO 卡在关闭流程里），"
                        + "重建暂时无法继续；请检查是否有外部代码在断开回调里抛异常。");
                    m_TransportRebuildStartedAt = Time.realtimeSinceStartup;
                }

                return;
            }

            if (Time.realtimeSinceStartup - m_TransportRebuildStartedAt < RebuildSettleSeconds)
            {
                return;
            }

            RestartServerListener();
        }

        /// <summary>
        /// 重新监听同一端口并补回消息处理器。
        /// </summary>
        private void RestartServerListener()
        {
            if (!StartServerListener())
            {
                m_TransportRebuildPending = false;
                m_Session?.Log.Error(
                    $"[服务器] 传输层重建失败：无法在端口 {m_Options.Port} 上重新监听。"
                    + $"本局已进入宽限（{ReconnectGraceSeconds:F0} 秒），期间请人工重启服务器进程。");
                return;
            }

            // 新的 CustomMessagingManager 是空的：输入、容器、大厅请求的处理器都要重新登记。
            RegisterServerMessageHandlers();

            // 兜底再扫一遍名册：万一某条断开回调没触发（传输层已被打哑时完全可能），
            // 这里保证"服务器认为在线但连接早就没了"的残留记录一定进宽限，而不是把重连挡在门外。
            KeepRosterOnReconnect("传输层重建后");

            m_TransportRebuildCount++;
            m_TransportRebuildPending = false;

            m_Session?.Log.Warning(
                $"[服务器] 传输层已重建（第 {m_TransportRebuildCount} 次）：端口 {m_Options.Port} 重新监听，"
                + $"在局玩家的位置与背包保留 {ReconnectGraceSeconds:F0} 秒，等待客户端自动重连。");
        }

        /// <summary>
        /// 按当前启动参数让服务器开始监听（首次启动与重建共用同一条路径）。
        /// </summary>
        /// <returns>监听成功返回 true。</returns>
        /// <remarks>
        /// 首次启动与重建走同一个方法，是为了避免"重建时漏了某一项参数"——
        /// 例如忘了重新设 <c>ConnectionData</c>，重建后的服务器会绑到默认端口上，
        /// 表现成"自愈之后谁也连不上"。
        /// </remarks>
        private bool StartServerListener()
        {
            m_Transport.SetConnectionData(ListenAddress, (ushort)m_Options.Port, ListenAddress);

            // 网络配置与客户端共用同一份工厂：两端不一致时 NGO 会在握手阶段直接断开，
            // 且只在开发者日志里留一条极难发现的警告（M9-P-11）。
            m_Network.NetworkConfig = NetworkConfigFactory.Create(m_Transport);
            return m_Network.StartServer();
        }

        /// <summary>
        /// 注册服务器侧的全部命名消息处理器（首次启动与传输层重建后都要调用）。
        /// </summary>
        /// <remarks>
        /// <para>NGO 关闭会话时会把 <c>CustomMessagingManager</c> 置空，重新启动时再新建一个——
        /// 挂在上面的命名消息处理器**不会跟过来**。因此这里把所有通道集中到一个入口，
        /// 重建时补一次即可，而不是让调用方去记"一共有哪几个通道"。</para>
        ///
        /// <para>新增服务器侧通道时，把注册写进 <c>RegisterLobbyMessageHandlers</c> /
        /// <c>RegisterMovementMessageHandlers</c> 就好，本方法不需要跟着改。</para>
        /// </remarks>
        private void RegisterServerMessageHandlers()
        {
            RegisterLobbyMessageHandlers();
            RegisterMovementMessageHandlers();
            RegisterMerchantHandlers();
        }

        /// <summary>
        /// 把名册上"还活着"的人全部转入掉线宽限：位置、背包、战局进度都保留，等他们重连接管。
        /// </summary>
        /// <param name="reason">写进日志的原因（"传输层重建" / "传输层重建后"）。</param>
        /// <remarks>
        /// <para>还没登录的连接没有可保留的东西（昵称、背包、位置都没有），直接丢弃——
        /// 留着它们反而会在"昵称是否在线"的判断里制造幽灵账号。</para>
        ///
        /// <para>已经处于宽限里的记录不动：他们上一次的截止时刻才是有效的，
        /// 重复续期会把"宽限 60 秒"变成"每次重建再送 60 秒"。</para>
        /// </remarks>
        private void KeepRosterOnReconnect(string reason)
        {
            if (m_LobbyClients.Count == 0)
            {
                return;
            }

            var grace = ReconnectGraceSeconds;
            var deadline = Time.realtimeSinceStartup + grace;
            System.Collections.Generic.List<int> dropped = null;
            System.Collections.Generic.List<LobbyClient> kept = null;

            foreach (var pair in m_LobbyClients)
            {
                var client = pair.Value;
                if (client.InGrace)
                {
                    continue;
                }

                if (!client.LoggedIn || string.IsNullOrEmpty(client.Nickname))
                {
                    dropped ??= new System.Collections.Generic.List<int>();
                    dropped.Add(pair.Key);
                    continue;
                }

                kept ??= new System.Collections.Generic.List<LobbyClient>();
                kept.Add(client);
            }

            if (dropped != null)
            {
                for (var i = 0; i < dropped.Count; i++)
                {
                    m_LobbyClients.Remove(dropped[i]);
                }
            }

            if (kept == null)
            {
                return;
            }

            for (var i = 0; i < kept.Count; i++)
            {
                kept[i].GraceDeadline = deadline;
                m_Session?.Log.Info(
                    $"[服务器] （{reason}）玩家 {kept[i].ClientId}「{kept[i].Nickname}」转入宽限 "
                    + $"{grace:F0} 秒：位置与背包保留，重连即接管。");
            }
        }
    }
}
