using System.Collections.Generic;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"静默客户端"过滤（P5 / 排障手册 P-48 的缓解措施）。
    /// </summary>
    /// <remarks>
    /// <para><b>要解决的问题：</b>实测发现，某个客户端进程被强制结束时，其余客户端到服务器的上行会在
    /// 1~2 秒内一起消失：服务器侧表现为 10 秒后所有连接同时 <c>ProtocolTimeout</c>，
    /// 而服务器既没有发送错误、也没有传输层故障事件，主线程也没有长卡顿；同一时刻新客户端也连不上。
    /// 这些特征都指向"服务器的 UDP 接收路径被污染"。</para>
    ///
    /// <para><b>为什么怀疑"继续给已经消失的端点发包"：</b>对端进程结束后，它的 UDP 端口不再有人监听，
    /// 操作系统会回一个 ICMP port-unreachable；而服务器的快照是 20 Hz 持续推的，
    /// 几秒内就会积累上百条这类错误。Windows 上 UDP 套接字收到这类 ICMP 会把错误冒泡给后续的接收调用，
    /// 一旦被当成致命错误，整条接收路径就哑了——这与"发得出去、收不进来"完全吻合。</para>
    ///
    /// <para><b>缓解办法（本文件）：</b>快照只发给还在说话的客户端——一个人超过
    /// <see cref="SilentClientSeconds"/> 没有上行，就先停掉对他的推送（不是把他踢掉，宽限期照旧），
    /// 于是对已消失端点的发包量从"上百条"降到"几条"。他重新开始说话时推送自动恢复。</para>
    ///
    /// <para><b>它不是根治：</b>如果一条 ICMP 就足以污染接收路径，这个缓解无效——
    /// 那需要传输层（UTP/Baselib socket）或操作系统层面处理，属于 P-48 记录里的深水区。
    /// 因此本类同时留下"谁在什么时候被判定为静默"的日志，供下一步判定。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 多久没收到上行就判定为"静默"（秒）。
        /// </summary>
        /// <remarks>
        /// <para>取 0.6 秒：正常客户端以 60 Hz 上行，隔着网络抖动也不会超过这个量级；
        /// 而它远小于 NGO 的协议超时（10 秒），因此能在"对死端点发包"这件事上省掉九成以上的量。</para>
        ///
        /// <para>代价：某个客户端真的停顿 0.6 秒以上（加载、GC、窗口被系统挂起）时，
        /// 它会短暂地收不到快照——表现是画面停在原地，等它恢复上行后自动跟上。</para>
        /// </remarks>
        private const float SilentClientSeconds = 0.6f;

        /// <summary>每名玩家最后一次上行的时刻。</summary>
        private readonly Dictionary<int, float> m_LastInputAt = new Dictionary<int, float>();

        /// <summary>已经打过"静默"日志的玩家（避免刷屏）。</summary>
        private readonly HashSet<int> m_SilentLogged = new HashSet<int>();

        /// <summary>记录一次上行（在 <c>HandleClientInput</c> 里调用）。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void NoteClientInput(int playerId)
        {
            m_LastInputAt[playerId] = Time.realtimeSinceStartup;

            // 重新说话就允许再次记录静默，否则"抖一下再抖一下"只有第一条日志。
            m_SilentLogged.Remove(playerId);
        }

        /// <summary>
        /// 这名客户端是否已经静默（太久没有上行）。
        /// </summary>
        /// <param name="clientId">连接编号。</param>
        /// <returns>静默返回 true，调用方应跳过对他的快照推送。</returns>
        private bool IsClientSilent(int clientId)
        {
            if (!m_LastInputAt.TryGetValue(clientId, out var last))
            {
                // 从没说过话：不当作静默。刚连上、正在加载场景的客户端属于这一类，
                // 把他过滤掉会让"进图那一小段"永远看不到世界。
                return false;
            }

            var silence = Time.realtimeSinceStartup - last;
            if (silence <= SilentClientSeconds)
            {
                return false;
            }

            if (m_SilentLogged.Add(clientId))
            {
                m_Session?.Log.Info(
                    $"[服务器] 玩家 {clientId} 已静默 {silence:F1} 秒，暂停向它推送快照"
                    + "（减少对已消失端点的发包；他恢复上行后自动继续）。");
            }

            return true;
        }

        /// <summary>玩家离开时清掉记录。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void ForgetClientInput(int playerId)
        {
            m_LastInputAt.Remove(playerId);
            m_SilentLogged.Remove(playerId);
        }
    }
}
