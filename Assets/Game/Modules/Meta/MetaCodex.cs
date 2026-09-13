using System;
using System.Collections.Generic;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 收集图鉴：记录玩家「曾经拿到过」的物品种类。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么记录的是「曾经」而不是「现在拥有」：</b>图鉴的语义是"这件东西我摸过"。
    /// 如果按"现在拥有"来算，玩家把战利品卖掉的瞬间条目就会熄灭，
    /// 收集进度会随着花钱来回跳，看起来像丢档；而"出现过就永久点亮"才是玩家对图鉴的预期。
    /// 阵亡丢装备同理：丢的是物品，不是见识。</para>
    ///
    /// <para><b>为什么单独成一个类而不是塞进 <see cref="MetaProgress"/>：</b>
    /// 图鉴与仓库、装备槽没有共享状态，只是恰好同处一份存档。
    /// 独立成类后，它的规则（去重、稳定排序、整批还原）可以在 EditMode 里单独测，
    /// 不必搭一整套背包出来。</para>
    ///
    /// <para>本类不做任何校验"这个 ID 是否存在"——校验依赖物品目录，而那属于表现与内容层。
    /// 存档里多余的旧 ID 会被原样保留：万一将来某个物品回归，它的点亮状态还在。</para>
    /// </remarks>
    public sealed class MetaCodex
    {
        /// <summary>
        /// 已点亮的物品稳定 ID 集合。
        /// </summary>
        /// <remarks>用 Ordinal 比较：物品 ID 是程序生成的稳定标识，
        /// 不做语言相关的字符串比较，避免不同区域设置下出现"看起来一样却判不等"。</remarks>
        private readonly HashSet<string> m_Discovered = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>已点亮的条目数。</summary>
        public int Count
        {
            get { return m_Discovered.Count; }
        }

        /// <summary>该物品是否已经点亮。</summary>
        /// <param name="itemId">物品稳定 ID。</param>
        public bool Contains(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && m_Discovered.Contains(itemId);
        }

        /// <summary>
        /// 点亮一件物品。
        /// </summary>
        /// <param name="itemId">物品稳定 ID。</param>
        /// <returns>本次是"新点亮"返回 true；已经点亮过或 ID 为空返回 false。</returns>
        public bool Mark(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return false;
            }

            return m_Discovered.Add(itemId);
        }

        /// <summary>
        /// 批量点亮一组物品。
        /// </summary>
        /// <param name="itemIds">物品稳定 ID 序列；允许为空。</param>
        /// <returns>其中"新点亮"的条目数。</returns>
        public int MarkAll(IEnumerable<string> itemIds)
        {
            if (itemIds == null)
            {
                return 0;
            }

            var added = 0;
            foreach (var id in itemIds)
            {
                if (Mark(id))
                {
                    added++;
                }
            }

            return added;
        }

        /// <summary>
        /// 存档还原专用：整批替换当前集合。
        /// </summary>
        /// <param name="itemIds">存档里的物品 ID 列表；为 null 时等价于清空（旧存档没有这个字段）。</param>
        public void Restore(IEnumerable<string> itemIds)
        {
            m_Discovered.Clear();
            if (itemIds == null)
            {
                return;
            }

            foreach (var id in itemIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    m_Discovered.Add(id);
                }
            }
        }

        /// <summary>
        /// 整理成稳定顺序的数组，供写盘使用。
        /// </summary>
        /// <returns>按 ID 排序后的副本。</returns>
        /// <remarks>哈希集合的枚举顺序不保证稳定：直接写盘会让每次保存的字节都不一样，
        /// 存档 diff 与问题排查都会变得困难。排序的代价可以忽略，收益是"同样的进度写出同样的文件"。</remarks>
        public string[] ToSortedArray()
        {
            var ids = new List<string>(m_Discovered);
            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }
    }
}
