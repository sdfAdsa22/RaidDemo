using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Inventory;
using RaidDemo.Presentation;
using RaidDemo.Raid;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>服务器运行时的撤离与结算部分：撤离区读秒、结算与结果广播。</summary>
    /// <remarks>
    /// <para>与 <c>ServerRuntime.Raid.cs</c>（生命两态与施救）分开：那条管"还能不能打"，
    /// 这条管"这一局什么时候、以什么结果结束"。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>地图生效后收集撤离点。</summary>
        private void TryCollectExtractionZones()
        {
            if (string.IsNullOrEmpty(m_Options.MapSceneName)
                || SceneManager.GetActiveScene().name != m_Options.MapSceneName)
            {
                return;
            }

            var markers = Object.FindObjectsByType<ExtractionZoneMarker>(FindObjectsSortMode.None);
            m_ExtractionZones.Clear();
            m_ExtractionZones.AddRange(markers);
            m_RaidZonesReady = true;

            m_Session?.Log.Info($"[服务器] 撤离点已就绪：{m_ExtractionZones.Count} 个。");
        }

        /// <summary>推进一名玩家的撤离读秒与生死判定。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void TickPlayerRaid(int playerId)
        {
            TickDownTestHook(playerId);

            if (!m_RaidProgress.TryGetValue(playerId, out var progress))
            {
                progress = new RaidProgress();
                m_RaidProgress[playerId] = progress;
            }

            if (progress.Settled)
            {
                return;
            }

            // 生命状态优先于撤离：倒在撤离区里不能算撤离成功。
            if (!IsPlayerAlive(playerId))
            {
                HandleHealthDepleted(playerId, progress);
                return;
            }

            // 已经失能的人不参与撤离读秒（他躺着，动不了）。
            if (m_LifeStates.GetState(playerId) == PlayerLifeState.Downed)
            {
                return;
            }

            if (!m_World.TryGetSnapshot(playerId, out var snapshot))
            {
                return;
            }

            var zoneId = FindZoneAt(snapshot.State.Position);
            if (zoneId < 0)
            {
                // 离开撤离区：读条清零（与客户端一致——撤离读条不能"攒着"）。
                progress.SecondsInZone = 0f;
                progress.ZoneId = -1;
                return;
            }

            if (zoneId != progress.ZoneId)
            {
                progress.ZoneId = zoneId;
                progress.SecondsInZone = 0f;
            }

            progress.SecondsInZone += RaidTickInterval;

            // 读秒痕迹：整秒变化时记一笔。"撤离读秒由服务器裁定"这条路径
            // 只有走通了才有意义，而验收时它必须能从日志里看出来。
            var wholeSeconds = Mathf.FloorToInt(progress.SecondsInZone);
            if (wholeSeconds != progress.LastReportedSecond)
            {
                progress.LastReportedSecond = wholeSeconds;
                m_Session?.Log.Info(
                    $"[服务器] 玩家 {playerId} 在撤离区 {zoneId} 读秒 {wholeSeconds}/{ExtractionSeconds:F0} 秒。");
            }

            if (progress.SecondsInZone < ExtractionSeconds)
            {
                return;
            }

            ExtractedPlayerCount++;
            SettlePlayer(playerId, progress, RaidOutcome.Extracted);
        }

        /// <summary>
        /// 生命归零：进入失能，或在没有队友可救时直接按死亡结算。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="progress">该玩家的战局进度。</param>
        /// <remarks>
        /// <para><b>单机为什么直接死：</b>只有一名玩家时没人能来救，倒地只会把"我死了"
        /// 变成六十秒的等待——那是纯粹的时间浪费。因此人数 ≤ 1 时保持原有语义（倒地即死亡），
        /// 这也是设计文档里对单机的明确要求。</para>
        /// </remarks>
        private void HandleHealthDepleted(int playerId, RaidProgress progress)
        {
            var playerCount = m_World != null ? m_World.PlayerCount : 0;

            if (playerCount <= 1 || !m_LifeStates.MarkDowned(playerId))
            {
                KilledPlayerCount++;
                SettlePlayer(playerId, progress, RaidOutcome.Killed);
                return;
            }

            m_Session?.Log.Info(
                $"[服务器] 玩家 {playerId} 倒地（失能），{PlayerLifeStateTracker.BleedOutSeconds:F0} 秒内无人施救将死亡。");

            BroadcastLifeEvent(new RaidLifeEventMessage
            {
                Kind = RaidLifeEventMessage.KindDowned,
                PlayerId = playerId,
                SecondsRemaining = PlayerLifeStateTracker.BleedOutSeconds,
            });
        }

        /// <summary>流血倒计时耗尽：按死亡结算。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void OnPlayerBledOut(int playerId)
        {
            m_Session?.Log.Info($"[服务器] 玩家 {playerId} 倒地超时，已死亡。");

            BroadcastLifeEvent(new RaidLifeEventMessage
            {
                Kind = RaidLifeEventMessage.KindBledOut,
                PlayerId = playerId,
            });

            if (m_RaidProgress.TryGetValue(playerId, out var progress) && !progress.Settled)
            {
                KilledPlayerCount++;
                SettlePlayer(playerId, progress, RaidOutcome.Killed);
            }
        }

        /// <summary>被队友救起：恢复一部分生命，重新可以行动。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void OnPlayerRevived(int playerId)
        {
            RestorePlayerHealth(playerId, PlayerLifeStateTracker.RevivedHealth);

            m_Session?.Log.Info(
                $"[服务器] 玩家 {playerId} 已被队友救起，恢复 {PlayerLifeStateTracker.RevivedHealth:F0} 点生命。");

            BroadcastLifeEvent(new RaidLifeEventMessage
            {
                Kind = RaidLifeEventMessage.KindRevived,
                PlayerId = playerId,
            });
        }

        /// <summary>把玩家的权威生命值设为该值（救起时用）。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="health">目标生命值。</param>
        private void RestorePlayerHealth(int playerId, float health)
        {
            if (m_Combat == null)
            {
                return;
            }

            var combatantId = m_Combat.GetCombatantId(playerId);
            if (combatantId != 0 && m_Combat.World.TryGet(combatantId, out var state))
            {
                state.SetHealth(health);
            }
        }

        /// <summary>广播一条生命事件。</summary>
        /// <param name="message">事件内容。</param>
        private void BroadcastLifeEvent(in RaidLifeEventMessage message)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null
                || manager.ConnectedClientsIds.Count == 0)
            {
                return;
            }

            using (var writer = new FastBufferWriter(24, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessageToAll(
                    ContainerNetworkChannel.LifeMessageName,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>记录一名玩家本帧是否按住救援键。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="held">是否按住。</param>
        internal void SetReviveHeld(int playerId, bool held)
        {
            m_ReviveHeld[playerId] = held;
        }

        /// <summary>该玩家当前是否处于失能（不能移动、不能开枪）。</summary>
        /// <param name="playerId">玩家编号。</param>
        internal bool IsPlayerDowned(int playerId)
        {
            return m_LifeStates.GetState(playerId) == PlayerLifeState.Downed;
        }

        /// <summary>玩家所处撤离区的编号；不在任何区内时返回 -1。</summary>
        /// <param name="position">玩家的权威平面位置。</param>
        /// <summary>验收钩子触发延迟（秒）。</summary>
        private const float DownTestDelaySeconds = 15f;

        private bool m_DownTestApplied;

        /// <summary>
        /// 验收钩子：`-downtest` 时在开局 15 秒后把玩家 1 的生命打到 0。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>
        /// 与 `-spawnzone` 同性质：**只改场景，不改规则**。伤害直接写在战斗单位上，
        /// 之后"进入失能 → 倒计时 → 队友扶起"全部走正常路径。
        /// </remarks>
        private void TickDownTestHook(int playerId)
        {
            if (!m_Options.DownTest || playerId != 1 || m_DownTestApplied)
            {
                return;
            }

            if (Time.unscaledTime < DownTestDelaySeconds || !IsPlayerAlive(playerId))
            {
                return;
            }

            // 必须等到"有队友可救"：只有一名玩家时服务器按单机语义直接死亡结算
            //（与真实游玩一致），那样这条钩子就验收不到倒地与救援了。
            if (m_World == null || m_World.PlayerCount < 2)
            {
                return;
            }

            m_DownTestApplied = true;
            RestorePlayerHealth(playerId, 0f);
            m_Session?.Log.Info("[服务器] 验收钩子：把玩家 1 的生命打到 0（-downtest）。");
        }

        private int FindZoneAt(RaidDemo.Shared.Vector2F position)
        {
            for (var i = 0; i < m_ExtractionZones.Count; i++)
            {
                var marker = m_ExtractionZones[i];
                if (marker == null)
                {
                    continue;
                }

                var center = marker.transform.position;
                var dx = center.x - position.X;
                var dz = center.z - position.Y;
                var radius = marker.Radius;
                if ((dx * dx) + (dz * dz) <= radius * radius)
                {
                    return marker.ZoneId;
                }
            }

            return -1;
        }

        /// <summary>
        /// 结算一名玩家：算带出价值、广播结果、留一条 Info 痕迹。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="progress">该玩家的战局进度。</param>
        /// <param name="outcome">结果。</param>
    }
}
