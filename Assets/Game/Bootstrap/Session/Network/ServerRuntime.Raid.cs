using System.Collections.Generic;
using RaidDemo.Inventory;
using RaidDemo.Presentation;
using RaidDemo.Raid;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的战局裁决部分：撤离读秒与结算（P3-3 起）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么结算必须由服务器裁定：</b>结算把物品从"这一局"变成"跨局的资产"
    /// （仓库、金币、任务进度都以它为准）。如果客户端自己算，改客户端就能凭空造出收益——
    /// 而这类作弊在联机里是最容易发生的（改一个数字即可）。</para>
    ///
    /// <para><b>撤离点从哪来：</b>与容器、导航一样，服务器读的是**同一张地图**上的
    /// <see cref="ExtractionZoneMarker"/>，因此点位置、半径、编号两端天然一致。</para>
    ///
    /// <para><b>各自结算：</b>每名玩家有自己的撤离读秒与生死状态——
    /// 一个人撤离成功不代表另一个人也结束，这正是联机合作与单机最本质的区别。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>撤离所需的持续停留时间（秒）。与客户端读条一致。</summary>
        private const float ExtractionSeconds = 10f;

        /// <summary>撤离读秒的推进间隔（秒）。不需要每帧算，1/5 秒足够精确。</summary>
        private const float RaidTickInterval = 0.2f;

        /// <summary>一名玩家的战局内进度。</summary>
        private sealed class RaidProgress
        {
            /// <summary>在撤离区内已停留的秒数。</summary>
            public float SecondsInZone;

            /// <summary>本局击杀数（只统计玩家造成的）。</summary>
            public int Kills;

            /// <summary>是否已经有结果（撤离 / 阵亡）。</summary>
            public bool Settled;

            /// <summary>已停留的撤离区编号（-1 表示不在任何区内）。</summary>
            public int ZoneId = -1;

            /// <summary>上一次打日志时的整秒数，避免每秒刷一行。</summary>
            public int LastReportedSecond = -1;
        }

        private readonly Dictionary<int, RaidProgress> m_RaidProgress = new Dictionary<int, RaidProgress>();
        private readonly List<ExtractionZoneMarker> m_ExtractionZones = new List<ExtractionZoneMarker>();
        private float m_NextRaidTickTime;
        private bool m_RaidZonesReady;

        /// <summary>已撤离的玩家数（验收与调试用）。</summary>
        public int ExtractedPlayerCount { get; private set; }

        /// <summary>已阵亡的玩家数（验收与调试用）。</summary>
        public int KilledPlayerCount { get; private set; }

        /// <summary>每 0.2 秒推进一次战局裁决：撤离读秒与生死。</summary>
        private void TickRaid()
        {
            if (!m_RaidZonesReady)
            {
                TryCollectExtractionZones();
            }

            if (Time.unscaledTime < m_NextRaidTickTime)
            {
                return;
            }

            m_NextRaidTickTime = Time.unscaledTime + RaidTickInterval;

            foreach (var pair in m_PlayerBodies)
            {
                TickPlayerRaid(pair.Key);
            }
        }

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
            if (!m_RaidProgress.TryGetValue(playerId, out var progress))
            {
                progress = new RaidProgress();
                m_RaidProgress[playerId] = progress;
            }

            if (progress.Settled)
            {
                return;
            }

            // 阵亡优先于撤离：倒在撤离区里不能算撤离成功。
            if (!IsPlayerAlive(playerId))
            {
                KilledPlayerCount++;
                SettlePlayer(playerId, progress, RaidOutcome.Killed);
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

        /// <summary>玩家所处撤离区的编号；不在任何区内时返回 -1。</summary>
        /// <param name="position">玩家的权威平面位置。</param>
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
        private void SettlePlayer(int playerId, RaidProgress progress, RaidOutcome outcome)
        {
            progress.Settled = true;

            var carriedValue = ResolveCarriedValue(playerId);
            var elapsed = m_World != null ? (float)m_World.SimulationTime : 0f;

            m_Session?.Log.Info(
                $"[服务器] 结算：玩家 {playerId} {DescribeOutcome(outcome)}，"
                + $"带出价值 {carriedValue}，击杀 {progress.Kills}，用时 {elapsed:F0} 秒。");

            BroadcastRaidOutcome(new RaidOutcomeMessage
            {
                PlayerId = playerId,
                Outcome = (byte)outcome,
                CarriedValue = carriedValue,
                Kills = progress.Kills,
                ElapsedSeconds = elapsed,
            });
        }

        /// <summary>统计一名玩家带出/损失物品的总价值（服务器那份数据）。</summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>
        /// 只统计随身背包与弹药挂：装备槽的物品价值属于"带进去的东西"，
        /// 等 P5 的仓库与结算细化再一起算（这条简化不会让任何人有额外收益）。
        /// </remarks>
        private int ResolveCarriedValue(int playerId)
        {
            if (m_Combat == null || !m_Combat.TryGetLoadout(playerId, out var loadout) || loadout == null)
            {
                return 0;
            }

            var total = 0;
            total += SumGridValue(loadout.Backpack);
            total += SumGridValue(loadout.AmmoPouch);
            return total;
        }

        /// <summary>累加一个网格里所有物品的价值。</summary>
        private static int SumGridValue(InventoryGrid grid)
        {
            if (grid == null)
            {
                return 0;
            }

            var items = grid.Items;
            var total = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    total += items[i].TotalValue;
                }
            }

            return total;
        }

        /// <summary>结果的中文描述（日志用）。</summary>
        private static string DescribeOutcome(RaidOutcome outcome)
        {
            switch (outcome)
            {
                case RaidOutcome.Extracted:
                    return "撤离成功";
                case RaidOutcome.Killed:
                    return "阵亡";
                default:
                    return outcome.ToString();
            }
        }

        /// <summary>把一条结果广播给所有客户端。</summary>
        /// <param name="message">结果消息。</param>
        private void BroadcastRaidOutcome(in RaidOutcomeMessage message)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null
                || manager.ConnectedClientsIds.Count == 0)
            {
                return;
            }

            using (var writer = new FastBufferWriter(32, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessageToAll(
                    ContainerNetworkChannel.OutcomeMessageName,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>玩家断开时清掉他的战局进度。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void UnregisterRaidProgress(int playerId)
        {
            m_RaidProgress.Remove(playerId);
        }
    }
}
