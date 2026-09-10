namespace RaidDemo.Inventory
{
    /// <summary>
    /// 负重配置：承载上限与超重衰减区间。
    /// </summary>
    /// <remarks>
    /// <para>承载上限在 M2 是固定值。M6 会把它改为由背包与装备共同提供，
    /// 也就是更好的背包等于更高的上限、更强的贪婪能力。把它做成独立对象而不是常量，
    /// 目的就是那时候只换数据、不动规则。</para>
    ///
    /// <para>为什么用普通类而不是 ScriptableObject：本程序集需要能在 EditMode 测试中
    /// 直接 new 出来；资产与配置的转换放在启动层完成。</para>
    /// </remarks>
    public sealed class EncumbranceProfile
    {
        /// <summary>
        /// 承载上限（千克）。低于或等于 0 时按无法承载任何东西处理。
        /// </summary>
        /// <remarks>
        /// 取 20 的依据是灰盒物品表：把护甲、头盔、步枪与背包都带上大约 18 千克，
        /// 再塞一件值钱小物件就会越过上限。上限必须落在"稍微贪一点就会超"的位置，
        /// 否则负重系统在对局里永远看不出效果。
        /// </remarks>
        public float CapacityKg = 20f;

        /// <summary>
        /// 超重的衰减跨度：从 1.0 倍上限衰减到 1.5 倍上限触底。
        /// </summary>
        /// <remarks>
        /// 取值 0.5 意味着超出上限 50% 时速度降到最低。
        /// 这个区间不能太窄，否则超重立刻变成钉在原地，玩家失去再撑两步的余地；
        /// 也不能太宽，否则超重的体感会消失。
        /// </remarks>
        public float OverloadSpanRatio = 0.5f;

        /// <summary>
        /// 计算负重比：当前重量除以承载上限。
        /// </summary>
        /// <param name="weightKg">当前总重量（千克）。</param>
        /// <returns>
        /// 负重比。承载上限非正时返回正无穷，让调用方自然落入超重分支，
        /// 而不需要在自己那边再写一次除零判断。
        /// </returns>
        public float RatioFor(float weightKg)
        {
            if (CapacityKg <= 0f)
            {
                return float.PositiveInfinity;
            }

            return weightKg / CapacityKg;
        }

        /// <summary>
        /// 校验配置是否处于合理范围，供启动阶段尽早发现错误配置。
        /// </summary>
        /// <returns>校验通过返回 null，否则返回描述问题的信息。</returns>
        public string Validate()
        {
            if (CapacityKg <= 0f)
            {
                return "CapacityKg 必须大于 0，否则玩家任何负重都会立刻超重。";
            }

            if (OverloadSpanRatio <= 0f)
            {
                return "OverloadSpanRatio 必须大于 0，否则超重会瞬间跌到速度下限。";
            }

            return null;
        }
    }
}
