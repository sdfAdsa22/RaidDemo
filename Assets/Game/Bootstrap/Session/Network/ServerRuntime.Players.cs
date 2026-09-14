using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"玩家进出权威世界"部分。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>这是大厅（决定谁该在局里）与移动权威世界（实际承载玩家）之间的
    /// 那道门。开局、结算、退赛、断线四条路径都要经过它，放在移动文件里会与"收输入、推世界、发快照"
    /// 这条主循环混在一起，改一处要读两大段不相干的代码。</para>
    ///
    /// <para>两条路径都允许重复调用：调用方（大厅、结算、断线回调）不需要先判断状态。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 把一名玩家放进权威世界。
        /// </summary>
        /// <param name="playerId">玩家编号（等于其连接编号）。</param>
        /// <returns>成功加入返回 true。</returns>
        /// <remarks>
        /// <para><b>P4 起由开局调用</b>：在此之前（P1~P3.5）是"连上就进世界"，
        /// 那种语义适合自动化验证，但联机大厅要求玩家先登录、进房间、等队友——
        /// 这些阶段里地图上不应该有他这个人。</para>
        ///
        /// <para>重复调用是安全的：已经在世界里的玩家直接返回，不会再被加一次
        /// （否则同一局里会出现两个同编号实体，位置与血量各算一份）。</para>
        /// </remarks>
        internal bool SpawnPlayerIntoWorld(int playerId)
        {
            if (m_World == null)
            {
                m_Session?.Log.Warning($"[服务器] 权威世界未就绪，玩家 {playerId} 无法进入战局。");
                return false;
            }

            if (m_PlayerBodies.ContainsKey(playerId))
            {
                return true;
            }

            CreatePlayerBody(playerId);

            var position = SpawnPositionFor(playerId);
            if (!m_World.TryAddPlayer(playerId, position, SpawnFacing, out var error))
            {
                m_Session?.Log.Warning($"[服务器] 玩家 {playerId} 加入权威世界失败：{error}");
                m_PlayerBodies.Remove(playerId);
                return false;
            }

            m_Session?.Log.Info($"[服务器] 玩家 {playerId} 已进入战局（在局 {m_World.PlayerCount} 人）。");
            RegisterLifeState(playerId);
            AddPlayerToCombat(playerId);
            BindPlayerHitTarget(playerId);

            // 背包命令通道要在参战之后建（需要随身装备）；容器内容随入图下发。
            RegisterPlayerContainers(playerId);
            SendAllContainerContentsTo((ulong)playerId);
            return true;
        }

        /// <summary>
        /// 把一名玩家移出权威世界（结算收尾、退赛、断开都走这条）。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>不在世界里时直接返回：调用方不需要先判断，这条路径允许重复调用。</remarks>
        internal void RemovePlayerFromWorld(int playerId)
        {
            if (!m_PlayerBodies.ContainsKey(playerId) && (m_World == null || !m_World.ContainsPlayer(playerId)))
            {
                return;
            }

            m_PlayerBodies.Remove(playerId);
            m_PlayerGroundHeights.Remove(playerId);

            // 载体对象必须显式销毁：它带着胶囊碰撞体与命中标识，留在场景里就是"幽灵玩家"——
            // 射线会打到它，命中判定会把伤害算到一个已经离场的编号上。
            if (m_PlayerColliders.TryGetValue(playerId, out var body) && body != null)
            {
                Object.Destroy(body);
            }

            m_PlayerColliders.Remove(playerId);
            UnregisterPlayerContainers(playerId);
            UnregisterRaidProgress(playerId);
            UnregisterLifeState(playerId);
            RemovePlayerFromCombat(playerId);

            if (m_World != null && m_World.RemovePlayer(playerId))
            {
                m_Session?.Log.Info($"[服务器] 玩家 {playerId} 已离开战局（在局 {m_World.PlayerCount} 人）。");
            }
        }

        /// <summary>客户端断开：从权威世界移除（若他在里面）。</summary>
        private void OnClientDisconnected(ulong clientId)
        {
            RemovePlayerFromWorld((int)clientId);
        }
    }
}
