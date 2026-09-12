namespace RaidDemo.Data
{
    /// <summary>
    /// 物品 ID 到物品定义的只读查询接口。
    /// </summary>
    /// <remarks>
    /// <para>规则层只依赖这个接口，不依赖 <c>ItemCatalog</c> 资产本身。
    /// 这样任务奖励、商人交易与存档还原都能在 EditMode 测试里用字典替身验证，
    /// 而不需要创建 ScriptableObject 资产。</para>
    ///
    /// <para>它是"稳定 ID 是唯一持久化键"这条规则的落点：
    /// 任何持有 ID 的系统，都需要一个入口把 ID 还原成定义。</para>
    /// </remarks>
    public interface IItemDefinitionLookup
    {
        /// <summary>按 ID 查找物品定义。</summary>
        /// <param name="id">物品稳定标识。</param>
        /// <param name="definition">找到的定义。</param>
        /// <returns>找到返回 true。</returns>
        bool TryGet(string id, out IItemDefinition definition);
    }
}
