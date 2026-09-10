using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 负重判定规则：把当前重量翻译成状态与一组移动修正。
    /// </summary>
    /// <remarks>
    /// <para>这是纯函数集合，没有任何状态，因此可以在测试里逐条穷举边界值。
    /// 联机时服务端与客户端跑的是同一份实现，不会出现自己看没超重、服务端认为超了的分歧。</para>
    ///
    /// <para>三段式的作用顺序是有讲究的：先拿走奔跑，再拿走速度。
    /// 玩家在 70% 处首先失去的是快速脱离战斗的能力，这是压力最大的一档；
    /// 越过 100% 之后才开始持续变慢。反过来设计的话，70% 到 100% 这段区间会毫无意义。</para>
    /// </remarks>
    public static class EncumbranceRules
    {
        /// <summary>
        /// 重装阈值：负重比达到该值时失去奔跑能力。
        /// </summary>
        /// <remarks>取闭区间下界，恰好 0.70 判为重装，避免边界上的归属含糊。</remarks>
        public const float HeavyRatio = 0.70f;

        /// <summary>超重状态下速度倍率的下限。无论多重都不会低于这个值。</summary>
        public const float OverloadSpeedFloor = 0.4f;

        /// <summary>重装状态下的体力恢复倍率。</summary>
        public const float HeavyStaminaRegenMultiplier = 0.5f;

        /// <summary>默认的超重衰减跨度，与 <see cref="EncumbranceProfile.OverloadSpanRatio"/> 的默认值一致。</summary>
        public const float DefaultOverloadSpanRatio = 0.5f;

        /// <summary>
        /// 判定负重状态。
        /// </summary>
        /// <param name="weightKg">当前总重量（千克）。</param>
        /// <param name="profile">负重配置。为 null 或上限非正时按没有负重约束处理。</param>
        /// <returns>负重状态。</returns>
        /// <remarks>
        /// 配置缺失时返回轻装而不是超重：那属于开发期问题，不该让玩家在正常场景里寸步难行。
        /// 配置错误由启动阶段的 Validate 单独报出。
        /// </remarks>
        public static EncumbranceState Evaluate(float weightKg, EncumbranceProfile profile)
        {
            if (profile == null || profile.CapacityKg <= 0f)
            {
                return EncumbranceState.Light;
            }

            var ratio = profile.RatioFor(weightKg);

            if (ratio > 1f)
            {
                return EncumbranceState.Overloaded;
            }

            return ratio >= HeavyRatio ? EncumbranceState.Heavy : EncumbranceState.Light;
        }

        /// <summary>
        /// 把负重状态翻译成移动修正。
        /// </summary>
        /// <param name="state">负重状态。</param>
        /// <param name="ratio">负重比，仅在超重时参与速度衰减计算。</param>
        /// <returns>可供 <c>PlayerMovementProfile.ApplyModifiers</c> 直接使用的修正。</returns>
        public static MovementModifiers ResolveModifiers(EncumbranceState state, float ratio)
        {
            switch (state)
            {
                case EncumbranceState.Heavy:
                    return new MovementModifiers(1f, HeavyStaminaRegenMultiplier, false);

                case EncumbranceState.Overloaded:
                    return new MovementModifiers(ResolveSpeedMultiplier(ratio), 0f, false);

                default:
                    return MovementModifiers.Default;
            }
        }

        /// <summary>
        /// 计算超重状态下的速度倍率。
        /// </summary>
        /// <param name="ratio">负重比。</param>
        /// <param name="spanRatio">衰减跨度，默认 0.5，即 1.5 倍上限时触底。</param>
        /// <returns>速度倍率，落在下限与 1.0 之间。</returns>
        /// <remarks>
        /// 公式为 1.0 加上 (下限减 1.0) 乘以 clamp01((ratio - 1.0) 除以 span)。
        /// 刚超过上限时几乎不减速，达到上限的 1.5 倍时降到下限并保持。
        /// 这样越贪越慢是一个连续可感知的过程，而不是某一点上的突变。
        /// </remarks>
        public static float ResolveSpeedMultiplier(float ratio, float spanRatio = DefaultOverloadSpanRatio)
        {
            if (float.IsPositiveInfinity(ratio) || float.IsNaN(ratio))
            {
                return OverloadSpeedFloor;
            }

            if (ratio <= 1f)
            {
                return 1f;
            }

            if (spanRatio <= 0f)
            {
                return OverloadSpeedFloor;
            }

            var progress = (ratio - 1f) / spanRatio;
            if (progress > 1f)
            {
                progress = 1f;
            }

            return 1f + ((OverloadSpeedFloor - 1f) * progress);
        }

        /// <summary>
        /// 一步到位：由重量直接得到移动修正。
        /// </summary>
        /// <param name="weightKg">当前总重量（千克）。</param>
        /// <param name="profile">负重配置。</param>
        /// <returns>移动修正。</returns>
        /// <remarks>装配层每帧调用它，避免在调用处重复写先判定再取值的两步流程。</remarks>
        public static MovementModifiers Resolve(float weightKg, EncumbranceProfile profile)
        {
            var state = Evaluate(weightKg, profile);
            var ratio = profile == null ? 0f : profile.RatioFor(weightKg);
            return ResolveModifiers(state, ratio);
        }
    }
}
