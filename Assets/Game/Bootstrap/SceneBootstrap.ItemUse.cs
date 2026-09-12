using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Raid;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的物品使用部分：完成后的回血与消耗。
    /// </summary>
    /// <remarks>
    /// 与战局主循环分开：那边回答「每帧推进什么」，这边回答「一件物品用完之后世界变成什么样」。
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>使用完成：回血并把物品消耗掉。</summary>
        private void OnItemUseCompleted(ItemUseCompletedEvent evt)
        {
            var item = evt.Item;
            var medical = ResolveMedical(item);
            if (medical == null)
            {
                return;
            }

            if (m_CombatWorld != null && m_CombatWorld.TryGet(m_PlayerCombatantId, out var state))
            {
                var healed = Mathf.Min(state.Health + medical.HealAmount, PlayerMaxHealth);
                state.SetHealth(healed);
            }

            ConsumeOne(item);
            if (m_RaidHud != null)
            {
                m_RaidHud.ShowHeal(medical.HealAmount);
            }
        }

        /// <summary>
        /// 从堆叠里扣掉一件。
        /// </summary>
        /// <remarks>
        /// 数量大于 1 时用 <c>Split(1)</c> 让原堆减一，拆出来的那一件直接丢弃；
        /// 只剩一件时整件从格子里移除。两条路径都走物品自身的 API，
        /// 不直接改数量，避免绕开堆叠规则。
        /// </remarks>
        private void ConsumeOne(ItemInstance item)
        {
            var grid = FindOwningGrid(item);
            if (grid == null)
            {
                return;
            }

            if (item.StackCount > 1)
            {
                item.Split(1);
                return;
            }

            grid.Remove(item);
        }

        /// <summary>找出某个物品实例当前在哪个随身容器里。</summary>
        private InventoryGrid FindOwningGrid(ItemInstance item)
        {
            var backpack = m_Loadout?.Backpack;
            if (backpack != null && backpack.Contains(item))
            {
                return backpack;
            }

            var pouch = m_Loadout?.AmmoPouch;
            if (pouch != null && pouch.Contains(item))
            {
                return pouch;
            }

            return null;
        }
    }
}
