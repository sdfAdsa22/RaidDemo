using System.Collections.Generic;
using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 全部物品定义的索引资产。
    /// </summary>
    /// <remarks>
    /// <para>它是"游戏里一共有哪些物品"的唯一权威来源。战利品表、商人库存、
    /// 存档载入都通过它把字符串 ID 还原成定义对象，因此存档里永远只需要写 ID。</para>
    ///
    /// <para>查找表是**惰性构建**的：运行时第一次查询时建立 ID 到定义的字典。
    /// 这样做避免了在编辑器里每次改动物品列表都要手动刷新缓存，
    /// 也避免把缓存写进序列化数据（那会让资产文件里出现一份可被改坏的冗余数据）。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ItemCatalog",
        menuName = "RaidDemo/物品目录",
        order = 11)]
    public sealed class ItemCatalog : ScriptableObject
    {
        /// <summary>目录中的全部物品定义。</summary>
        [SerializeField] private List<ItemDefinition> m_Items = new List<ItemDefinition>();

        /// <summary>ID 到定义的查找表，惰性构建。</summary>
        private Dictionary<string, ItemDefinition> m_Lookup;

        /// <summary>目录中的全部物品定义。</summary>
        public IReadOnlyList<ItemDefinition> All
        {
            get { return m_Items; }
        }

        /// <summary>
        /// 按 ID 查找物品定义。
        /// </summary>
        /// <param name="id">物品的稳定标识。</param>
        /// <param name="definition">找到的定义。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGet(string id, out ItemDefinition definition)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(id))
            {
                definition = null;
                return false;
            }

            return m_Lookup.TryGetValue(id, out definition);
        }

        /// <summary>按 ID 查找物品定义，找不到时返回 null。</summary>
        public ItemDefinition Get(string id)
        {
            return TryGet(id, out var definition) ? definition : null;
        }

        /// <summary>
        /// 校验整个目录。
        /// </summary>
        /// <returns>问题描述列表，全部合法时为空列表。</returns>
        /// <remarks>
        /// 除逐项校验外还检查两类跨条目的问题：ID 重复，以及列表里混入空引用。
        /// 这两类问题单看某一个资产是发现不了的，只有站在目录的层面才能查出。
        /// </remarks>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            var seen = new HashSet<string>();

            for (var i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                if (item == null)
                {
                    problems.Add($"目录第 {i} 项为空引用。");
                    continue;
                }

                var problem = item.ValidateSelf();
                if (problem != null)
                {
                    problems.Add($"{item.name}：{problem}");
                }

                if (!string.IsNullOrEmpty(item.Id) && !seen.Add(item.Id))
                {
                    problems.Add($"{item.name}：物品 ID 重复——{item.Id}。");
                }
            }

            return problems;
        }

        /// <summary>丢弃查找表缓存，下次查询时重建。</summary>
        /// <remarks>
        /// 编辑器里改完物品列表后调用它，可以立刻看到结果而不必重进播放模式。
        /// </remarks>
        public void RebuildLookup()
        {
            m_Lookup = null;
        }

        /// <summary>按需构建 ID 查找表。</summary>
        private void EnsureLookup()
        {
            if (m_Lookup != null)
            {
                return;
            }

            m_Lookup = new Dictionary<string, ItemDefinition>(m_Items.Count);
            for (var i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                if (item == null || string.IsNullOrEmpty(item.Id))
                {
                    continue;
                }

                // 重复 ID 不再覆盖：保留先出现的那一个，让结果稳定，
                // 重复本身由 Validate 报出，不需要在运行时表现成"随机取其中一个"。
                if (!m_Lookup.ContainsKey(item.Id))
                {
                    m_Lookup.Add(item.Id, item);
                }
            }
        }
    }
}
