namespace RaidDemo.Data
{
    /// <summary>
    /// 容器（背包中的背包）相关规则常量。
    /// </summary>
    /// <remarks>
    /// 限制嵌套深度的原因不是性能，而是**可理解性**：无限嵌套会让"我的东西在哪"
    /// 变成一种负担，而本项目的乐趣来自"要多拿一件还是现在就撤"，不是整理迷宫。
    ///
    /// 它同时是一条防护：容器被放入自身时会形成环，深度上限让环最多只能绕两层，
    /// 即便规则层漏掉一处检查，也不会出现无限递归。
    /// </remarks>
    public static class ContainerRules
    {
        /// <summary>
        /// 允许的最大嵌套深度。
        /// </summary>
        /// <remarks>
        /// 深度按"容器所在层数"计：直接放在角色背包里的容器，其内部网格深度为 1；
        /// 容器内的容器，其内部网格深度为 2。深度达到该值的网格不再接受新的容器物品。
        /// </remarks>
        public const int MaxNestingDepth = 2;
    }
}
