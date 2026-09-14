using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 物品实例复用池：把"同步前就在这个网格里的物品"按规格分桶存起来，
    /// 供铺放服务器内容时优先复用，避免每次都重建对象。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决的是什么问题：</b>界面把物品对象当成身份在用——双击识别比较引用、
    /// 拖拽松手要按对象找坐标。服务器每下发一次容器内容就把物品全部 new 一遍的话，
    /// 只要同步落在两次点击之间或一次拖拽的过程里，玩家手上那件东西就换了对象：
    /// 双击没反应、拖到位松手什么也没发生，日志里却没有任何错误
    /// （P4.5 实测：同一格、同一件东西，两次同步之间的实例号不同）。</para>
    ///
    /// <para><b>为什么可以复用：</b>物品的"内容"由定义与数量决定，复用只是不换对象，
    /// 数量、坐标、朝向仍然按服务器那一份写入。复用不到（新捡到的、数量变了的）才新建。</para>
    ///
    /// <para><b>使用顺序很重要：</b>必须先 <see cref="Collect"/>（此时物品还在网格里），
    /// 再由调用方清空网格，最后逐件 <see cref="Take"/>。顺序反了就一件也复用不到。</para>
    /// </remarks>
    public sealed class LootContainerItemReusePool
    {
        /// <summary>规格 → 可复用的旧实例（同一规格可能有多件，因此是列表）。</summary>
        private readonly Dictionary<ReuseKey, List<ItemInstance>> m_Buckets =
            new Dictionary<ReuseKey, List<ItemInstance>>();

        /// <summary>
        /// 收集一个网格里当前所有的物品实例。
        /// </summary>
        /// <param name="grid">即将被服务器内容覆盖的网格。</param>
        /// <remarks>
        /// 同一个池可以反复用于不同容器，但每个容器用之前要新建一个池（或清空），
        /// 否则会把别的容器里的实例带进来——那样物品会"从箱子里跑进背包"。
        /// </remarks>
        public void Collect(InventoryGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            var items = grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.Definition == null)
                {
                    continue;
                }

                var key = new ReuseKey(item.Definition.Id, item.StackCount, item.Rotated);
                if (!m_Buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<ItemInstance>(1);
                    m_Buckets.Add(key, bucket);
                }

                bucket.Add(item);
            }
        }

        /// <summary>
        /// 取出一个与给定规格一致的旧实例。
        /// </summary>
        /// <param name="itemId">物品定义编号（服务器下发的那个字符串）。</param>
        /// <param name="count">数量（必须完全一致才复用：数量变了就说明堆叠被拆过或合并过）。</param>
        /// <param name="rotated">是否横放（旋转会改变占位，必须一致）。</param>
        /// <returns>可复用的旧实例；没有匹配时返回 null，由调用方新建。</returns>
        public ItemInstance Take(string itemId, int count, bool rotated)
        {
            if (itemId == null)
            {
                return null;
            }

            var key = new ReuseKey(itemId, count, rotated);
            if (!m_Buckets.TryGetValue(key, out var bucket) || bucket.Count == 0)
            {
                return null;
            }

            // 从末尾取：顺序无关紧要（同规格实例彼此等价），但后进先出让最近用过的那件继续留在手上，
            // 玩家刚拖动过的那一件因此最不容易换对象。
            var index = bucket.Count - 1;
            var item = bucket[index];
            bucket.RemoveAt(index);
            return item;
        }

        /// <summary>
        /// 复用池的键：对界面来说"同一件东西" = 同一份定义 + 同样的数量 + 同样的摆放方向。
        /// </summary>
        /// <remarks>
        /// 刻意不把坐标放进键里：物品被搬动一格之后仍然是"同一件东西"，
        /// 因此搬动不会让它的对象失效——这正是玩家拖完一格还想接着拖下一格时要的效果。
        /// </remarks>
        private readonly struct ReuseKey : IEquatable<ReuseKey>
        {
            private readonly string m_ItemId;
            private readonly int m_Count;
            private readonly bool m_Rotated;

            /// <summary>创建一个复用键。</summary>
            /// <param name="itemId">物品定义编号。</param>
            /// <param name="count">数量。</param>
            /// <param name="rotated">是否横放。</param>
            public ReuseKey(string itemId, int count, bool rotated)
            {
                m_ItemId = itemId;
                m_Count = count;
                m_Rotated = rotated;
            }

            /// <inheritdoc />
            public bool Equals(ReuseKey other)
            {
                return m_Count == other.m_Count
                       && m_Rotated == other.m_Rotated
                       && string.Equals(m_ItemId, other.m_ItemId, StringComparison.Ordinal);
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is ReuseKey other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                var hash = m_ItemId != null ? m_ItemId.GetHashCode() : 0;
                hash = (hash * 397) ^ m_Count;
                return (hash * 397) ^ (m_Rotated ? 1 : 0);
            }
        }
    }
}
