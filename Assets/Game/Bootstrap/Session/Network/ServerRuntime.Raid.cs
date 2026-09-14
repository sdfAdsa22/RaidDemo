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

        /// <summary>生命两态（存活 / 失能 / 死亡）与施救进度。</summary>
        private readonly PlayerLifeStateTracker m_LifeStates = new PlayerLifeStateTracker();

        /// <summary>每名玩家本帧是否按住"扶起队友"。</summary>
        private readonly Dictionary<int, bool> m_ReviveHeld = new Dictionary<int, bool>();

        private readonly List<int> m_BledOutBuffer = new List<int>();
        private readonly List<int> m_RevivedBuffer = new List<int>();

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

            TickRevives();

            // 倒地倒计时与救起判定走逻辑层的跟踪器：那里的规则有单元测试钉着。
            m_BledOutBuffer.Clear();
            m_RevivedBuffer.Clear();
            m_LifeStates.Tick(RaidTickInterval, m_BledOutBuffer, m_RevivedBuffer);

            for (var i = 0; i < m_RevivedBuffer.Count; i++)
            {
                OnPlayerRevived(m_RevivedBuffer[i]);
            }

            for (var i = 0; i < m_BledOutBuffer.Count; i++)
            {
                OnPlayerBledOut(m_BledOutBuffer[i]);
            }

            foreach (var pair in m_PlayerBodies)
            {
                TickPlayerRaid(pair.Key);
            }
        }

        /// <summary>
        /// 累加所有"正在被施救"的玩家的进度。
        /// </summary>
        /// <remarks>
        /// <para>每名倒地玩家只认一名施救者：最近的那个按住 F 的活着的队友。
        /// 多人同时救不叠加（叠加会让"两个人一起救"变成一瞬间起来，破坏这个机制的时间成本）。</para>
        /// </remarks>
        private void TickRevives()
        {
            List<int> downed = null;

            foreach (var pair in m_PlayerBodies)
            {
                if (m_LifeStates.GetState(pair.Key) != PlayerLifeState.Downed)
                {
                    continue;
                }

                downed ??= new List<int>();
                downed.Add(pair.Key);
            }

            if (downed == null)
            {
                return;
            }

            for (var i = 0; i < downed.Count; i++)
            {
                var targetId = downed[i];
                var targetPosition = ResolvePlanePosition(targetId);
                var rescuer = FindRescuer(targetId, targetPosition);

                if (rescuer < 0)
                {
                    m_LifeStates.InterruptRevive(targetId);
                    continue;
                }

                m_LifeStates.AddReviveProgress(targetId, RaidTickInterval);
            }
        }

        /// <summary>找正在施救的队友；没有返回 -1。</summary>
        /// <param name="targetId">被救者。</param>
        /// <param name="targetPosition">被救者位置。</param>
        private int FindRescuer(int targetId, RaidDemo.Shared.Vector2F targetPosition)
        {
            foreach (var pair in m_PlayerBodies)
            {
                var rescuerId = pair.Key;
                if (!m_ReviveHeld.TryGetValue(rescuerId, out var held) || !held)
                {
                    continue;
                }

                if (m_LifeStates.CanRevive(
                        rescuerId, targetId, ResolvePlanePosition(rescuerId), targetPosition))
                {
                    return rescuerId;
                }
            }

            return -1;
        }

        /// <summary>取一名玩家的权威平面位置；查不到时返回原点。</summary>
        private RaidDemo.Shared.Vector2F ResolvePlanePosition(int playerId)
        {
            return m_World != null && m_World.TryGetSnapshot(playerId, out var snapshot)
                ? snapshot.State.Position
                : RaidDemo.Shared.Vector2F.Zero;
        }

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

            // 全员结算完就收尾回大厅（P4）：这里是"这一局什么时候算结束"的唯一判定入口。
            CheckRaidCompletion();
        }

        /// <summary>
        /// 统计一名玩家带出/损失物品的总价值（只算背包与弹药挂，装备槽留给 P5 的结算细化）。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
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

        /// <summary>登记一名玩家的生命状态（接入时调用）。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void RegisterLifeState(int playerId)
        {
            m_LifeStates.Register(playerId);
            m_ReviveHeld[playerId] = false;
        }

        /// <summary>清理一名玩家的生命状态（断开时调用）。</summary>
        /// <param name="playerId">玩家编号。</param>
        private void UnregisterLifeState(int playerId)
        {
            m_LifeStates.Remove(playerId);
            m_ReviveHeld.Remove(playerId);
        }

        /// <summary>重置全员的生命状态（重开一局）。</summary>
        private void ResetLifeStates()
        {
            m_LifeStates.Clear();
            m_ReviveHeld.Clear();
        }

    }
}
