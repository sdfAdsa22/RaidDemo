using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using RaidDemo.Presentation;
using RaidDemo.Raid;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{

    /// <summary>
    /// 服务器运行时的战局重置部分：把一局恢复成初始状态（重开、开局前复位）。
    /// </summary>
    /// <remarks>
    /// <para>它是唯一允许绕过移动模拟直接写位置的地方（见 <c>MovementServerWorld.TryTeleport</c>），
    /// 因此单独成文件：任何新增的“把玩家挪回去”的逻辑都必须走这里，不能各自写传送。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>收到"重开战局"请求：把这一局重置成初始状态（P4 会收进房主权限）。</summary>
        /// <param name="senderId">发起请求的客户端。</param>
        /// <param name="reader">消息体（当前为空）。</param>
        private void OnRaidRestartRequested(ulong senderId, FastBufferReader reader)
        {
            var playerId = (int)senderId;

            // P4：重开是房主权限（设计文档 §4 把"谁能重开"划归大厅与房间管理）。
            if (m_Room.HostClientId != playerId)
            {
                m_Session?.Log.Warning($"[服务器] 玩家 {playerId} 请求重开被拒：只有房主可以重开这一局。");
                return;
            }

            m_Session?.Log.Info($"[服务器] 房主 {playerId} 请求重开，正在重置战局。");
            RestartRaid();
        }

        /// <summary>把战局重置成初始状态（容器重抽、AI 重建、玩家回到出生点）。</summary>
        private void RestartRaid()
        {
            // 1. 进度与计数清零。
            m_RaidProgress.Clear();
            ResetLifeStates();
            ExtractedPlayerCount = 0;
            KilledPlayerCount = 0;

            // 2. 容器重新抽：先清空注册表里的场景容器，再按同一份生成点重建（编号不变）。
            var sceneIds = new List<int>();
            var ids = m_Containers.ContainerIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] >= ContainerIds.SceneBase && ids[i] < ContainerIds.ServerPlayerBase)
                {
                    sceneIds.Add(ids[i]);
                }
            }

            for (var i = 0; i < sceneIds.Count; i++)
            {
                m_Containers.Unregister(sceneIds[i]);
            }

            m_ContainersReady = false;
            TickContainers();

            // 3. AI 重建：先全部销毁，下一帧由 TickAi 重新生成（它本来就是惰性初始化）。
            ShutdownAi();

            // 4. 每名玩家回到参战状态：移除再添加，等价于"血量、护甲、弹药全部重置"。
            var playerIds = new List<int>();
            m_World.GetPlayerIds(playerIds);

            for (var i = 0; i < playerIds.Count; i++)
            {
                var playerId = playerIds[i];
                RemovePlayerFromCombat(playerId);
                UnregisterPlayerContainers(playerId);
                m_PlayerGroundHeights.Remove(playerId);

                var spawn = SpawnPositionFor(playerId);
                m_World.TryTeleport(playerId, spawn);

                CreatePlayerBody(playerId);
                RegisterLifeState(playerId);
                AddPlayerToCombat(playerId);
                RegisterPlayerContainers(playerId);
                SendContainerContentsTo(playerId);
            }

            BroadcastAllContainerContents();
            m_Session?.Log.Info("[服务器] 战局已重开：容器重抽、AI 重建、玩家回到出生点。");
        }
        /// <summary>按玩家标识排布出生点（验收模式下改到撤离区）。</summary>
        /// 按玩家标识排布出生点。
        /// </summary>
        /// <remarks>
        /// 每个人都叠在地图原点会让第一帧看起来像只有一个角色。P1 用一圈小队列把玩家排开，
        /// 真正的出生点由战局配置决定（P3 的生成管理）。
        /// </remarks>
        private Vector2F SpawnPositionFor(int playerId)
        {
            // 验收模式：出生点放进撤离区，用于验收"撤离读秒与结算由服务器裁定"。
            // 只改出生位置，不改任何规则：读秒、判定、结算走的都是真实路径。
            if (m_Options != null && m_Options.SpawnAtExtraction && m_ExtractionZones.Count > 0)
            {
                var marker = m_ExtractionZones[0];
                if (marker != null)
                {
                    var position = marker.transform.position;
                    return new Vector2F(position.x, position.z);
                }
            }

            var index = playerId < 0 ? 0 : playerId;
            return new Vector2F((index % 4) * SpawnSpacing, (index / 4) * SpawnSpacing);
        }
    }
}
