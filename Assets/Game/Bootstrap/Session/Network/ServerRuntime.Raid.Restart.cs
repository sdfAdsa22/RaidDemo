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

            // P4.5-b：重开只在战局世界里成立。回到安全屋之后再收到（延迟到达的）重开请求
            // 必须忽略——否则会把安全屋当成战局重置：容器重建、玩家被传送到战局出生点，
            // 而这张场景里根本没有那些坐标。
            if (m_WorldKind != ServerWorldKind.Raid)
            {
                m_Session?.Log.Info("[服务器] 重开请求被忽略：当前不在战局世界（已回到安全屋）。");
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
                m_World.TryTeleport(playerId, spawn, SpawnFacing);

                CreatePlayerBody(playerId);
                RegisterLifeState(playerId);

                // P5：重开一局也要按**账号自己的装备**配发。
                // 这里曾经传 null（退回默认 AK 配发），结果是"第一帧按账号装备、重开之后变回默认套"——
                // 玩家在安全屋里准备的枪在开局的第二次装配时被悄悄换掉了。
                AddPlayerToCombat(
                    playerId,
                    m_WorldKind == ServerWorldKind.Raid ? ResolveProfileForPlayer(playerId)?.Loadout : null);
                RegisterPlayerContainers(playerId);
                SendContainerContentsTo(playerId);
            }

            BroadcastAllContainerContents();
            m_Session?.Log.Info("[服务器] 战局已重开：容器重抽、AI 重建、玩家回到出生点。");
        }
        /// <summary>
        /// 出生基准点：地图西南角的开阔草地（客户端场景里的 m_PlayerSpawnPosition 必须一致）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么从地图正中挪到这里：</b>原来的出生点是谷底正中，最近的敌人出生点只有约 5 米，
        /// 玩家一进图就落在敌人的视线与射程里——站着不动 25 秒左右必死。
        /// 实机验收与自动脚本都被这一点拖累：还没走到第一个箱子就阵亡。</para>
        ///
        /// <para><b>为什么选这里：</b>离最近的敌人出生点约 17.6 米（AI 的必发现距离是 6 米、
        /// 警惕带 6~9 米，留足反应空间），离最近的撤离点约 17.7 米（不至于一出生就能撤），
        /// 半径 1.2 米内除了地形没有任何碰撞体（不会卡在货箱或墙里）。</para>
        /// </remarks>
        private static readonly Vector2F SpawnBase = new Vector2F(-16.8f, -24.1f);

        /// <summary>出生朝向（度）：指向地图中心，进图第一眼就朝着工业区。</summary>
        private static readonly Vector2F SpawnFacing = Vector2F.FromDegrees(55f);

        /// <summary>按玩家标识排布出生点（验收模式下改到撤离区）。</summary>
        /// <remarks>
        /// 每个人都叠在同一个点会让第一帧看起来像只有一个角色，因此按 1.6 米的间距
        /// 在基准点**往东北方向**排成小网格（往东北是为了不把玩家推出谷底西南角的空地边界）。
        /// </remarks>
        private Vector2F SpawnPositionFor(int playerId)
        {
            // 安全屋世界有自己的一张"小地图"（房间 + 设施），出生点当然也不同：
            // 用战局地图的西南角坐标会把人放进安全屋的墙里（P4.5-b 起世界是两张场景）。
            if (m_WorldKind == ServerWorldKind.SafeHouse)
            {
                return SafeHouseSpawnPositionFor(playerId);
            }

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
            return new Vector2F(
                SpawnBase.X + ((index % 4) * SpawnSpacing),
                SpawnBase.Y + ((index / 4) * SpawnSpacing));
        }
    }
}
