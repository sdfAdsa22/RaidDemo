using System.Collections.Generic;
using RaidDemo.Inventory;
using RaidDemo.Raid;
using RaidDemo.Shared;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的战局结算部分：这一局什么时候结束、每人带走多少、结果怎么下发。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么从 <c>ServerRuntime.Raid.cs</c> 拆出来：</b>那个文件管"每 tick 推进什么"
    /// （撤离读秒、倒地流血、救援），这里管"结束的那一刻做什么"。两者的改动原因不同，
    /// 放一起会同时顶到单文件行数上限与阅读成本（工程规范：单文件 ≤ 400 行）。</para>
    ///
    /// <para><b>三种结束方式的唯一入口都是 <see cref="SettlePlayer"/>：</b>撤离、阵亡、
    /// 时间耗尽。抄近路（例如超时直接清场）会让某种结局少做几步——广播、落服务端存档、
    /// 移出权威世界、检查是否全员结算，四件事一件都不能少。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>超时结算时的玩家编号缓冲：结算会改动名册与世界，不能边遍历边动手。</summary>
        private readonly List<int> m_RaidSettleBuffer = new List<int>();

        /// <summary>
        /// 战局总时长到点后，把还没结算的玩家按"时间耗尽"收尾（RD-AUD-042）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么必须由服务器判定：</b>客户端也有一个本地倒计时（用于 HUD），
        /// 但"这一局结束了吗"是战局裁决。以前只有客户端在数时间，多人局里就会出现
        /// "客户端已经弹结算面板、服务器还在跑"的分裂状态。</para>
        ///
        /// <para>时长上限来自 <c>-raidDuration</c>（默认 480 秒）；配 0 表示不做超时判定
        /// （排障时用来复现"永远不超时"的旧行为）。</para>
        /// </remarks>
        private void TickRaidTimeLimit()
        {
            var limit = m_Options != null ? m_Options.RaidTimeLimitSeconds : 0f;
            if (limit <= 0f || m_RaidStartedAt < 0f)
            {
                return;
            }

            var elapsed = Time.realtimeSinceStartup - m_RaidStartedAt;
            if (elapsed < limit)
            {
                return;
            }

            // 先把"还没结算的人"拷进缓冲再动手：SettlePlayer 在最后一人结算时会走到
            // 回大厅流程（清空 m_RaidProgress、切世界），不能边遍历边改。
            m_RaidSettleBuffer.Clear();
            foreach (var pair in m_RaidProgress)
            {
                if (!pair.Value.Settled)
                {
                    m_RaidSettleBuffer.Add(pair.Key);
                }
            }

            if (m_RaidSettleBuffer.Count == 0)
            {
                return;
            }

            m_Session?.Log.Info(
                $"[服务器] 战局时长到达上限（{limit:F0} 秒）：{m_RaidSettleBuffer.Count} 名玩家按超时结算。");

            for (var i = 0; i < m_RaidSettleBuffer.Count; i++)
            {
                var playerId = m_RaidSettleBuffer[i];
                if (m_RaidProgress.TryGetValue(playerId, out var progress) && !progress.Settled)
                {
                    SettlePlayer(playerId, progress, RaidOutcome.TimeExpired);
                }
            }
        }

        /// <summary>
        /// 给一名玩家结算：广播结果 → 落服务端存档 → 移出权威世界 → 检查是否全员结算。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="progress">该玩家的战局进度（会被标记为已结算）。</param>
        /// <param name="outcome">结局。</param>
        private void SettlePlayer(int playerId, RaidProgress progress, RaidOutcome outcome)
        {
            progress.Settled = true;

            var carriedValue = ResolveCarriedValue(playerId);

            // 本局用时按"开局时刻"算，而不是 m_World.SimulationTime（RD-AUD-051）：
            // 后者是世界对象创建以来的累计时间——服务器启动那一刻起就在涨，
            // 于是第二局起，"本局用时"会把服务器开机时长一起算进来（结算面板显示几百分钟）。
            var elapsed = m_RaidStartedAt >= 0f
                ? Mathf.Max(0f, Time.realtimeSinceStartup - m_RaidStartedAt)
                : 0f;

            // AR-05：死亡/超时结算的金额是"损失价值"，不是"带出价值"。
            // 旧文案对三种结局一律写"带出价值"，与下一行的"随身携带物已清空"自相矛盾。
            var valueLabel = outcome == RaidOutcome.Extracted ? "带出价值" : "损失价值";
            m_Session?.Log.Info(
                $"[服务器] 结算：玩家 {playerId} {DescribeOutcome(outcome)}，"
                + $"{valueLabel} {carriedValue}，击杀 {progress.Kills}，用时 {elapsed:F0} 秒。");

            BroadcastRaidOutcome(new RaidOutcomeMessage
            {
                PlayerId = playerId,
                Outcome = (byte)outcome,
                CarriedValue = carriedValue,
                Kills = progress.Kills,
                ElapsedSeconds = elapsed,
            });

            // P5：结果落到服务端存档（撤离入库 / 阵亡清空）并立刻写盘。
            // 放在广播之后：客户端先看到结算面板，服务器再落库——顺序反了的话，
            // 落库失败会让玩家看到"界面说带出来了、仓库里没有"，而这是最难解释的一类问题。
            ApplyOutcomeToProfile(playerId, outcome, carriedValue);

            // U-87：结算即离开权威世界。
            //
            // 结算之后这名玩家的战局已经结束（撤离或阵亡），他的身体不该继续出现在快照里——
            // 否则队友那一侧的验收机器人会把他当目标一直追（"已回屋的队友"在服务器世界里
            // 仍有身体），既不撤离、也不结束战局。移除之后快照里不再有他，
            // 远端玩家的表现会随快照自然消失。
            RemovePlayerFromWorld(playerId);

            // 全员结算完就收尾回大厅（P4）：这里是"这一局什么时候算结束"的唯一判定入口。
            CheckRaidCompletion();
        }

        /// <summary>
        /// 统计一名玩家带出 / 损失物品的总价值：背包 + 弹药挂 + 全部装备槽。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <returns>价值合计；读不到携带物时返回 0。</returns>
        private int ResolveCarriedValue(int playerId)
        {
            if (m_Combat == null || !m_Combat.TryGetLoadout(playerId, out var loadout) || loadout == null)
            {
                return 0;
            }

            var total = 0;
            total += SumGridValue(loadout.Backpack);
            total += SumGridValue(loadout.AmmoPouch);
            total += SumEquipmentValue(loadout.Equipment);
            return total;
        }

        /// <summary>累加装备槽里所有物品的价值。</summary>
        /// <param name="equipment">装备槽；可以为 null。</param>
        /// <returns>装备槽内物品的价值合计。</returns>
        /// <remarks>
        /// 漏算装备槽会让"带出价值"系统性偏低（`RD-AUD-061`）：玩家捡到的好枪好甲就装在身上，
        /// 那正是最值钱的部分。槽位顺序与 <c>MetaProgress.DepositLoadoutToStash</c> 保持一致。
        /// </remarks>
        private static int SumEquipmentValue(EquipmentLoadout equipment)
        {
            if (equipment == null)
            {
                return 0;
            }

            var total = 0;
            var slots = new[]
            {
                EquipmentSlot.PrimaryWeapon,
                EquipmentSlot.SecondaryWeapon,
                EquipmentSlot.Head,
                EquipmentSlot.Body,
                EquipmentSlot.Backpack,
            };

            for (var i = 0; i < slots.Length; i++)
            {
                var item = equipment.Get(slots[i]);
                if (item != null)
                {
                    total += item.TotalValue;
                }
            }

            return total;
        }

        /// <summary>累加一个网格里所有物品的价值。</summary>
        /// <param name="grid">网格；可以为 null。</param>
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
                case RaidOutcome.TimeExpired:
                    return "时间耗尽";
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
    }
}
