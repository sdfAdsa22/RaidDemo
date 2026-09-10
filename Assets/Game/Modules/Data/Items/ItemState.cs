namespace RaidDemo.Data
{
    /// <summary>
    /// 物品的独立状态载荷：同一型号的两件物品在数据上"长得不一样"的那部分。
    /// </summary>
    /// <remarks>
    /// <para>典型例子：一把打了 300 发、耐久 72% 的 AK，与一把全新的 AK。
    /// 这类状态不是"所有 AK 共有的"，而是"这一把 AK 独有的"，因此挂在
    /// <see cref="ItemInstance"/> 上，而不是 <see cref="IItemDefinition"/> 上。</para>
    ///
    /// <para><b>M2 阶段不实现任何具体子类，该字段恒为 null。</b>之所以现在就把类型留出来，
    /// 是因为堆叠规则必须从第一天起就写成"状态不相同则不可堆叠"。
    /// 如果等到 M3 才补，那么合并逻辑、存档格式与全部背包测试都要改一遍。</para>
    ///
    /// <para>关于"要不要给每件物品发全局唯一 ID"：本项目采用**内联状态**——
    /// 状态直接挂在实例上，**不引入全局实例注册表**。因为状态字段本身很便宜，
    /// 贵的是"拿 ID 去别处查表"带来的悬垂引用、ID 分配与持久化问题。
    /// <see cref="ItemInstance.InstanceId"/> 只用于同一会话内区分个体与事件寻址，它不是任何字典的键。</para>
    /// </remarks>
    public abstract class ItemState
    {
        /// <summary>
        /// 判断两份状态在内容上是否相等。
        /// </summary>
        /// <param name="other">与之比较的另一份状态，可能为 null。</param>
        /// <returns>内容相等返回 true。</returns>
        /// <remarks>
        /// 必须实现为**内容比较**而不是引用比较：两份独立的"满弹匣"状态在逻辑上是同一种东西，
        /// 应当允许堆叠；若用引用比较，它们会被判为不同，玩家会看到两个无法合并的满弹匣。
        /// </remarks>
        public abstract bool ContentEquals(ItemState other);
    }
}
