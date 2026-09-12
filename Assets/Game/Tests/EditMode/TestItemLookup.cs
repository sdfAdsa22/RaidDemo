using System.Collections.Generic;
using RaidDemo.Data;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 测试用物品查询表：用普通字典替代 ScriptableObject 目录。
    /// </summary>
    /// <remarks>
    /// 规则层只依赖 <see cref="IItemDefinitionLookup"/>，因此经济、任务与存档测试
    /// 不需要创建任何资产文件，也不会受真实物品表改动影响。
    /// </remarks>
    internal sealed class TestItemLookup : IItemDefinitionLookup
    {
        private readonly Dictionary<string, IItemDefinition> m_Items =
            new Dictionary<string, IItemDefinition>();

        /// <summary>登记一件物品。</summary>
        public TestItemLookup Add(IItemDefinition definition)
        {
            if (definition != null && !string.IsNullOrEmpty(definition.Id))
            {
                m_Items[definition.Id] = definition;
            }

            return this;
        }

        /// <inheritdoc />
        public bool TryGet(string id, out IItemDefinition definition)
        {
            return m_Items.TryGetValue(id ?? string.Empty, out definition);
        }

        /// <summary>测试辅助：按 ID 取定义，找不到时直接断言失败。</summary>
        public IItemDefinition GetForTest(string id)
        {
            if (!m_Items.TryGetValue(id, out var definition))
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"测试目录里没有登记物品 {id}。");
            }

            return definition;
        }
    }
}
