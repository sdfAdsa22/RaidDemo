namespace RaidDemo.Combat
{
    /// <summary>
    /// 护甲等级表：等级到护甲值与减伤率的映射。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么减伤率是一张全局表，而不是逐件配置在护甲资产里：</b>
    /// 因为可读性优先是本项目护甲模型的立身之本。玩家必须能记住"3 级甲就是 50% 减伤"，
    /// 捡到装备时一眼判断价值。</para>
    /// <para>如果每件护甲各自配置，两件同为 3 级的护甲可能是 48% 和 53%，
    /// 玩家就只能每次打开面板去对比，等级这个概念的沟通价值随之消失。</para>
    /// <para>护甲资产只负责两件真正因装备而异的事：最大耐久与磨损系数。
    /// 一件 3 级甲的档次由等级决定，它的耐操程度由耐久决定，两者互不干扰。</para>
    /// </remarks>
    public static class ArmorTiers
    {
        /// <summary>最高防护等级。</summary>
        public const int MaxLevel = 4;

        /// <summary>每一级对应的护甲值。穿透力与它比较得出穿透系数。</summary>
        public const float ArmorValuePerLevel = 10f;

        /// <summary>
        /// 取指定等级的护甲值。
        /// </summary>
        /// <param name="level">防护等级。超出范围会被截断到合法区间。</param>
        /// <returns>护甲值。0 级返回 0。</returns>
        public static float GetArmorValue(int level)
        {
            return ClampLevel(level) * ArmorValuePerLevel;
        }

        /// <summary>
        /// 取指定等级的减伤率。
        /// </summary>
        /// <param name="level">防护等级。超出范围会被截断到合法区间。</param>
        /// <returns>减伤率，取值 0 到 1。</returns>
        /// <remarks>
        /// 数值刻意取好记的整数百分比，而不是按某种曲线算出来的精确值：
        /// 20 / 35 / 50 / 65 每一档之间的差值玩家能感知到，也便于口口相传。
        /// </remarks>
        public static float GetReduction(int level)
        {
            switch (ClampLevel(level))
            {
                case 1:
                    return 0.20f;
                case 2:
                    return 0.35f;
                case 3:
                    return 0.50f;
                case 4:
                    return 0.65f;
                default:
                    return 0f;
            }
        }

        /// <summary>把等级截断到 0 到 <see cref="MaxLevel"/> 之间。</summary>
        public static int ClampLevel(int level)
        {
            if (level < 0)
            {
                return 0;
            }

            return level > MaxLevel ? MaxLevel : level;
        }
    }
}
