using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的"使用消耗品"部分：医疗品的权威执行（回血 + 扣物品 + 回发状态）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>它与"装备 / 卸下"同属背包命令，但语义不同——
    /// 那个改的是装备槽，这个改的是**生命值**（战斗状态）并消耗物品。
    /// 拆开也让 <c>ServerRuntime.Inventory.Commands.cs</c> 保持在单文件行数上限以内。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 联机下的"使用消耗品"（当前只有医疗品）：服务器回血、扣物品、把结果发回本人。
        /// </summary>
        /// <param name="playerId">发起者。</param>
        /// <param name="message">上行命令（容器 + 格子）。</param>
        /// <remarks>
        /// <para><b>为什么必须由服务器执行（`RD-AUD-044`）：</b>联机客户端没有本地战斗世界，
        /// 自己回血只会改一份没人读的镜像，而物品却真的从背包里扣掉了——
        /// 玩家的体感是"绷带用掉了、血没回"。权威生命值在服务器这边，
        /// 所以"能不能用、回多少、扣哪一件"全都得在这里算。</para>
        ///
        /// <para>校验顺序：容器 → 格子里的物品 → 是不是医疗品 → 玩家有没有战斗单位。
        /// 任何一步不成立就安静丢弃（并留一行日志），不改任何权威状态。</para>
        /// </remarks>
        private void HandleItemUseCommand(int playerId, in InventoryEquipCommandMessage message)
        {
            var containerId = TranslateContainerId(playerId, message.ContainerId);
            if (m_Containers == null || !m_Containers.TryGetGrid(containerId, out var grid))
            {
                return;
            }

            if (!TryResolveItemAt(grid, message.CellX, message.CellY, out var item))
            {
                return;
            }

            // 行为挂在**目录里的定义**上，而不是物品实例自带的 Definition：
            // 与客户端 `ResolveMedical` 走同一个来源（ServerMode.SceneItemCatalog）。
            var definition = item.Definition != null && ServerMode.SceneItemCatalog != null
                ? ServerMode.SceneItemCatalog.Get(item.Definition.Id)
                : null;
            var medical = definition != null ? definition.Behavior as MedicalBehavior : null;
            if (medical == null)
            {
                m_Session?.Log.Warning(
                    $"[服务器] 玩家 {playerId} 请求使用非消耗品「{item.Definition?.Id}」，已忽略。");
                RefreshContainersAfterCommand(playerId);
                return;
            }

            var combatantId = m_Combat != null ? m_Combat.GetCombatantId(playerId) : 0;
            if (combatantId == 0 || !m_Combat.World.TryGet(combatantId, out var state))
            {
                m_Session?.Log.Warning($"[服务器] 玩家 {playerId} 还没有战斗单位，使用请求被忽略。");
                return;
            }

            var maxHealth = ServerCombatCoordinator.DefaultMaxHealth;
            var healed = Mathf.Min(state.Health + medical.HealAmount, maxHealth);
            state.SetHealth(healed);

            ConsumeOneOnServer(grid, item);

            m_Session?.Log.Info(
                $"[服务器] 玩家 {playerId} 使用「{item.Definition.Id}」："
                + $"生命 {healed:F0}/{maxHealth:F0}（+{medical.HealAmount}）。");

            // 只回本人：生命是私有状态。容器也要回发——那一件已经从权威背包里扣掉了。
            SendCombatEventTo(playerId, new CombatEventMessage
            {
                Kind = CombatEventMessage.KindHealed,
                TargetId = ResolveEntityId(combatantId),
                RemainingHealth = healed,
            });
            RefreshContainersAfterCommand(playerId);
        }

        /// <summary>按格子坐标找容器里的物品；找不到时返回 false。</summary>
        /// <remarks>
        /// 遍历物品表而不是直接索引占用表：占用表是"物品 → 坐标"的方向，
        /// 而这里是反查，遍历一遍的代价在几件到几十件的规模上可以忽略。
        /// </remarks>
        private static bool TryResolveItemAt(
            InventoryGrid grid,
            int cellX,
            int cellY,
            out ItemInstance item)
        {
            item = null;

            var items = grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var candidate = items[i];
                if (candidate == null || !grid.TryGetOrigin(candidate, out var origin))
                {
                    continue;
                }

                if (origin.X == cellX && origin.Y == cellY)
                {
                    item = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>从权威容器里扣掉一件物品（数量 &gt; 1 时减一）。</summary>
        /// <param name="grid">物品所在容器。</param>
        /// <param name="item">要消耗的物品。</param>
        /// <remarks>
        /// 与客户端单机那条路保持一致：数量大于 1 用 <c>Split(1)</c> 让原堆减一，
        /// 只剩一件时整件移除。两条路规则不同的话，联机与单机会出现"同样的绷带剩的数量不一样"。
        /// </remarks>
        private static void ConsumeOneOnServer(InventoryGrid grid, ItemInstance item)
        {
            if (item.StackCount > 1)
            {
                item.Split(1);
                return;
            }

            grid.Remove(item);
        }
    }
}

