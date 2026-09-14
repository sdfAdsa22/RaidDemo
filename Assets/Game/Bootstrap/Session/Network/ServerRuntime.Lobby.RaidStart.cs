using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"开局 / 收尾通知"下行：把 <see cref="RaidStartMessage"/> 与
    /// <see cref="RaidEndMessage"/> 逐个发给房间成员。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>"开局的时刻"由 <c>ServerRuntime.Lobby.Raid.cs</c> 决定，
    /// 而"消息长什么样、发给谁"是协议层的事。这两件事的改动原因不同：
    /// 前者随玩法变化，后者随协议版本变化。</para>
    ///
    /// <para><b>为什么逐发而不是广播：</b>将来支持多房间时，广播会把别的房间的成员也喊进地图。
    /// 逐发只多几行，却让"谁该收到"在代码里一眼可见，不必依赖"房间成员 == 在线玩家"这个当下成立的前提。</para>
    ///
    /// <para><b>断开的人直接跳过：</b>房间名册与实际连接之间有一帧左右的时间差
    /// （先是连接断开，随后才处理离房）。向已断开的连接发命名消息在 NGO 里会抛异常，
    /// 因此这里逐个确认在线状态。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 通知房间成员"战局开始，请加载地图"。
        /// </summary>
        /// <param name="mapSceneName">要加载的战局场景名；为空时用当前托管的战局场景 / 服务器参数。</param>
        private void BroadcastRaidStart(string mapSceneName)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null || m_Room == null)
            {
                return;
            }

            var scene = string.IsNullOrEmpty(mapSceneName) ? m_WorldSceneName : mapSceneName;
            var message = new RaidStartMessage { MapSceneName = scene ?? string.Empty };

            for (var i = 0; i < m_Room.Members.Count; i++)
            {
                var clientId = m_Room.Members[i].ClientId;
                if (!IsClientConnected((ulong)clientId))
                {
                    continue;
                }

                using var writer = new FastBufferWriter(80, Allocator.Temp);
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(
                    LobbyChannel.RaidStartMessageName,
                    (ulong)clientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>
        /// 把"战局开始"发给单个客户端（重连回局时用，P5）。
        /// </summary>
        /// <param name="clientId">目标客户端。</param>
        /// <remarks>与广播同源同格式，只是收件人只有一个：重连的人需要立刻回到地图上。</remarks>
        private void SendRaidStartTo(int clientId)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null || !IsClientConnected((ulong)clientId))
            {
                return;
            }

            var message = new RaidStartMessage { MapSceneName = m_WorldSceneName ?? string.Empty };
            using var writer = new FastBufferWriter(80, Allocator.Temp);
            writer.WriteValueSafe(message);
            manager.CustomMessagingManager.SendNamedMessage(
                LobbyChannel.RaidStartMessageName,
                (ulong)clientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }

        /// <summary>
        /// 通知房间成员"这一局结束，回共享安全屋"。
        /// </summary>
        /// <remarks>
        /// <para>与开局通知同一套遍历与在线检查：房间名册与实际连接之间有一帧左右的时间差，
        /// 向已断开的连接发命名消息会抛异常。</para>
        ///
        /// <para><b>调用时机：</b>服务器已经切回安全屋、并且把所有人放回屋里之后
        /// （见 <c>ServerRuntime.Lobby.Raid.EndRaidToLobby</c>）——客户端加载安全屋场景时，
        /// 服务器这边已经准备好接住他的移动输入，不会出现"回到屋里却动不了"的空窗。</para>
        /// </remarks>
        private void BroadcastRaidEnd()
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null || m_Room == null)
            {
                return;
            }

            var message = new RaidEndMessage { SceneName = GameScenes.SafeHouse };

            for (var i = 0; i < m_Room.Members.Count; i++)
            {
                var clientId = m_Room.Members[i].ClientId;
                if (!IsClientConnected((ulong)clientId))
                {
                    continue;
                }

                using var writer = new FastBufferWriter(80, Allocator.Temp);
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(
                    LobbyChannel.RaidEndMessageName,
                    (ulong)clientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            m_Session?.Log.Info("[服务器] 已通知全员回到共享安全屋。");
        }
    }
}
