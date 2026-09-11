using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 一次弹药取用的结果。
    /// </summary>
    public readonly struct AmmoWithdrawal
    {
        /// <summary>创建取用结果。</summary>
        /// <param name="amount">实际取出的发数。</param>
        /// <param name="penetration">取出弹药的代表穿透力。</param>
        public AmmoWithdrawal(int amount, float penetration)
        {
            Amount = amount;
            Penetration = penetration;
        }

        /// <summary>实际取出的发数。</summary>
        public int Amount { get; }

        /// <summary>
        /// 取出弹药的代表穿透力。
        /// </summary>
        /// <remarks>
        /// 一个口径可能存在多种弹药（普通弹、穿甲弹）。它们的穿透力不同，
        /// 因此**装进弹匣的是哪一种，之后打出去的就是哪一种**——
        /// 这让"出发前带什么子弹"变成一个真实的选择。
        /// 从多个堆里取用时，以贡献最多的那一堆为准。
        /// </remarks>
        public float Penetration { get; }

        /// <summary>是否什么都没取到。</summary>
        public bool IsEmpty
        {
            get { return Amount <= 0; }
        }
    }

    /// <summary>
    /// 背包里的弹药储备：查找与取用。
    /// </summary>
    /// <remarks>
    /// <para>这是战斗系统与背包系统之间**唯一**的接缝。战斗层不关心背包里还有什么，
    /// 只问两件事：这个口径有多少发、取走若干发。</para>
    /// <para>取用是"能取多少取多少"而不是"不够就失败"，
    /// 因为换弹允许部分装填——玩家弹药见底时仍应能应战。</para>
    /// </remarks>
    public static class AmmoReserve
    {
        /// <summary>
        /// 统计背包中某个口径的可用弹药总数。
        /// </summary>
        /// <param name="container">要查找的容器。</param>
        /// <param name="caliberId">口径标识。</param>
        /// <returns>可用发数。</returns>
        public static int CountAvailable(InventoryGrid container, string caliberId)
        {
            if (container == null || string.IsNullOrEmpty(caliberId))
            {
                return 0;
            }

            var total = 0;
            var items = container.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (TryGetAmmo(items[i], caliberId, out _))
                {
                    total += items[i].StackCount;
                }
            }

            return total;
        }

        /// <summary>
        /// 从背包取走指定口径的弹药。
        /// </summary>
        /// <param name="container">要取用的容器。</param>
        /// <param name="caliberId">口径标识。</param>
        /// <param name="requested">希望取走的发数。</param>
        /// <returns>实际取走的发数与其穿透力。取不到时返回 0。</returns>
        /// <remarks>
        /// 会从多个弹药堆里依次取用，直到满足需求或弹药耗尽。
        /// 某堆被取空时整件移除，取走一部分时用拆分——两种路径都走背包层已有的规则，
        /// 因此"取弹药"不会绕过任何背包约束。
        /// </remarks>
        public static AmmoWithdrawal Consume(InventoryGrid container, string caliberId, int requested)
        {
            if (container == null || string.IsNullOrEmpty(caliberId) || requested <= 0)
            {
                return default;
            }

            var remaining = requested;
            var takenTotal = 0;
            var dominantPenetration = 0f;
            var dominantAmount = 0;

            // 复制一份再遍历：取用过程中会改动容器内的物品集合。
            var snapshot = new List<ItemInstance>(container.Items);
            for (var i = 0; i < snapshot.Count && remaining > 0; i++)
            {
                var item = snapshot[i];
                if (!TryGetAmmo(item, caliberId, out var penetration))
                {
                    continue;
                }

                var take = remaining < item.StackCount ? remaining : item.StackCount;
                if (take >= item.StackCount)
                {
                    container.Remove(item);
                }
                else
                {
                    // 拆分出的那一份就是被取走的部分，容器里留下的是余量。
                    item.Split(take);
                }

                remaining -= take;
                takenTotal += take;

                if (take > dominantAmount)
                {
                    dominantAmount = take;
                    dominantPenetration = penetration;
                }
            }

            return takenTotal > 0 ? new AmmoWithdrawal(takenTotal, dominantPenetration) : default;
        }

        /// <summary>判断一件物品是否为指定口径的弹药。</summary>
        /// <summary>
        /// 查看背包中该口径弹药的穿透力，**不取走**任何东西。
        /// </summary>
        /// <param name="container">要查看的容器。</param>
        /// <param name="caliberId">口径标识。</param>
        /// <param name="fallback">找不到匹配弹药时返回的备用值。</param>
        /// <returns>找到的第一堆弹药的穿透力，找不到则返回备用值。</returns>
        /// <remarks>
        /// 用途是"出厂弹匣里装的是什么"：玩家装备一把枪时弹匣已经是满的，
        /// 那里面装的应当是他带来的子弹，而不是穿透力为 0 的空壳。
        /// 只查看不取走，因此不会凭空消耗玩家的弹药。
        /// </remarks>
        public static float PeekPenetration(InventoryGrid container, string caliberId, float fallback)
        {
            if (container == null || string.IsNullOrEmpty(caliberId))
            {
                return fallback;
            }

            var items = container.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (TryGetAmmo(items[i], caliberId, out var penetration))
                {
                    return penetration;
                }
            }

            return fallback;
        }

        private static bool TryGetAmmo(ItemInstance item, string caliberId, out float penetration)
        {
            penetration = 0f;
            if (item == null)
            {
                return false;
            }

            var ammo = item.Definition.AmmoStats;
            if (ammo == null || !string.Equals(ammo.CaliberId, caliberId, System.StringComparison.Ordinal))
            {
                return false;
            }

            penetration = ammo.Penetration;
            return true;
        }
    }
}
