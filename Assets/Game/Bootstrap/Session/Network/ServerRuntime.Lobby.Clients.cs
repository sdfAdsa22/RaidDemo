using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"客户端接入与断开"部分：连接登记、宽限标记、编号复用的防御。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>这是"连接生命周期"这条线（谁进来了、谁走了、走的人要不要留位置），
    /// 与大厅主体（协议分发、房间状态下行）是两件改动原因完全不同的事。P-51 的传输层自愈
    /// 在这条线上加了一个关键细节——重建会让所有人**干净断开**，连接编号可能被随后的重连复用——
    /// 把它与协议代码混在一起会让那个细节很难被读到。</para>
    ///
    /// <para><b>负槽位（P-51）：</b>见 <see cref="m_NextGraceSlot"/> 的说明。宽限记录只按昵称被查找，
    /// 槽位本身没有语义，因此"编号被复用"时把旧记录挪到负数槽位是安全的。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 给"编号被新连接复用"的宽限记录准备的负槽位（从 -1 往下发）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么需要它（P-51）：</b>传输层重建（自愈）会让所有人**干净断开**，
        /// 而 UTP 在干净断开之后会从最小可用编号重新分配——也就是说重连上来的人很可能拿到
        /// 和掉线前一模一样的编号。名册以编号为键，新连接一登记就会覆盖掉同编号的宽限记录，
        /// 那条记录里"位置与背包挂在旧编号下"的事实随之丢失。</para>
        ///
        /// <para>宽限记录只会被 <c>FindGracedClient</c> 按昵称查找，槽位本身没有语义，
        /// 因此把它暂时挪到负数槽位是安全的（负数不可能是真实连接编号）；宽限到期清理时
        /// （<c>FinishDisconnectedSession</c>）按记录里的真实编号去名册与世界里找人。</para>
        /// </remarks>
        private int m_NextGraceSlot = -1;

        /// <summary>客户端接入：只登记登录态，不进战局世界。</summary>
        /// <param name="clientId">连接编号。</param>
        /// <remarks>
        /// 这里刻意**不广播房间状态**：此刻对方的命名消息处理器还没注册完（P-20 的教训），
        /// 广播会直接丢掉。改为在他发来登录请求之后再点对点回一份状态——
        /// 那一定发生在他注册处理器之后。
        /// </remarks>
        private void OnLobbyClientConnected(ulong clientId)
        {
            var id = (int)clientId;

            // 编号会被传输层复用（UTP 在"干净断开"之后从最小可用编号重新分配），而重建传输层
            // （P-51 的自愈）恰好制造了"所有人干净断开"这一刻。若名册上还躺着一条同编号的宽限记录，
            // 直接覆盖会连"他的位置与背包挂在旧编号下"这一事实一起丢掉——重连的人于是变成新玩家
            // （身上的东西全没了），世界里还留下一具没人认领的身体。
            // 因此把宽限记录先挪到负数槽位：它只按昵称被查找，槽位本身没有语义。
            if (m_LobbyClients.TryGetValue(id, out var stale) && stale.InGrace)
            {
                var slot = m_NextGraceSlot--;
                m_LobbyClients.Remove(id);
                m_LobbyClients[slot] = stale;

                m_Session?.Log.Info(
                    $"[服务器] 连接编号 {id} 被复用：宽限中的玩家「{stale.Nickname}」改挂到槽位 {slot}，"
                    + "等他重连时按昵称接管原来的位置与背包。");
            }

            m_LobbyClients[id] = new LobbyClient { ClientId = id };

            m_Session?.Log.Info(
                $"[服务器] 客户端 {id} 已接入服务（未登录），在线连接 {m_LobbyClients.Count} 个。");
        }

        /// <summary>
        /// 客户端断开：在房间里的人进入宽限，其他人直接清理（P5）。
        /// </summary>
        /// <param name="clientId">连接编号。</param>
        /// <remarks>
        /// <para><b>这里不再立刻清场。</b>P-48 的实测结论是：在"队友被硬杀"的那一刻立即清理，
        /// 会把其余玩家的上行链路一起打断。现在把清理推迟到宽限到期
        /// （<c>TickDisconnectGrace</c>），期间他的位置、背包与战局进度都留在服务器上。</para>
        ///
        /// <para>不立刻移除世界里的身体：那由断开路径上的移动部分判断
        /// （见 <c>ServerRuntime.Players.OnClientDisconnected</c>）——同样是"在房间里就保留"。</para>
        /// </remarks>
        private void OnLobbyClientDisconnected(ulong clientId)
        {
            var id = (int)clientId;

            // 已经在宽限里：说明这次断开是"重建传输层"自己造的，宽限标记在关闭网络之前就打好了。
            // 这里直接返回，既避免重复续期（把 60 秒宽限变成"每次重建再送 60 秒"），也少一条重复日志。
            if (m_LobbyClients.TryGetValue(id, out var gracedClient) && gracedClient.InGrace)
            {
                return;
            }

            var member = m_Room.Find(id);
            if (member == null)
            {
                // 还没进房间（只是挂在服务器列表上、或登录到一半）：没有可保留的东西，直接清理。
                m_LobbyClients.Remove(id);
                return;
            }

            var grace = m_Options != null ? m_Options.ReconnectGraceSeconds : LaunchOptions.DefaultReconnectGraceSeconds;
            if (!m_LobbyClients.TryGetValue(id, out var client))
            {
                // 兜底：名册记录缺失时补一条，否则宽限逻辑没有可标记的对象。
                client = new LobbyClient { ClientId = id, Nickname = member.Nickname, LoggedIn = true };
                m_LobbyClients[id] = client;
            }

            client.GraceDeadline = Time.realtimeSinceStartup + grace;

            m_Session?.Log.Info(
                $"[服务器] 玩家 {id}「{member.Nickname}」连接断开：保留其位置与背包 {grace:F0} 秒"
                + "（宽限期内可重连回局）。");
            BroadcastRoomState();
        }
    }
}
