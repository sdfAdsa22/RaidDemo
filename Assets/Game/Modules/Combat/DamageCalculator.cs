using UnityEngine;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 伤害结算：把"基础伤害 + 弹药穿透 + 目标护甲"算成最终伤害与护甲损耗。
    /// </summary>
    /// <remarks>
    /// <para><b>这是整个战斗系统里最需要被测试锁死的一段代码。</b>
    /// 它是纯函数、没有状态、不引用引擎，因此可以用精确的数值断言逐条验证。</para>
    ///
    /// <para>公式（详见 <c>Docs/Modules/03_Combat.md</c> 第 5 节）：</para>
    /// <code>
    /// 有效护甲值 = 等级 × 10 × (当前耐久 / 最大耐久)
    /// 穿透系数   = clamp((弹药穿透力 - 有效护甲值) / (等级 × 10), 0, 1)
    /// 最终伤害   = 基础伤害 × 部位倍率 × (1 - 减伤率 × (1 - 穿透系数))
    /// 护甲耐久  -= 基础伤害 × 磨损系数
    /// </code>
    ///
    /// <para>穿透系数是**连续**的，这是本模型与"击穿 / 未击穿"二值判定最重要的区别：
    /// 它让高穿透弹药打重甲有明确收益，同时又不产生一个"够用就行"的硬门槛。</para>
    /// </remarks>
    public static class DamageCalculator
    {
        /// <summary>
        /// 结算一次伤害。
        /// </summary>
        /// <param name="request">伤害请求。</param>
        /// <param name="tuning">全局调参。为 null 时使用默认值。</param>
        /// <returns>最终伤害、护甲损耗与穿透系数。任何输入都不会让本方法抛异常。</returns>
        public static DamageOutcome Resolve(in DamageRequest request, CombatTuning tuning)
        {
            var settings = tuning ?? CombatTuning.Default;

            // 基础伤害先做净化：负值与 NaN 一律按 0 处理。
            // 让"伤害为负"这种事根本不可能出现在结果里，比在下游到处防御要可靠。
            var baseDamage = Sanitize(request.BaseDamage);
            var multiplier = request.IsCritical ? Sanitize(settings.CriticalMultiplier) : 1f;
            if (multiplier < 1f)
            {
                // 暴击倍率被配成小于 1 是配置错误，此时按普通命中处理而不是削弱伤害。
                multiplier = 1f;
            }

            var rawDamage = baseDamage * multiplier;

            // 无护甲、或护甲已经打坏：穿透系数为 1，伤害全额，且不再产生磨损。
            var armor = request.Armor;
            if (!armor.IsEffective)
            {
                return new DamageOutcome(rawDamage, 0f, 1f);
            }

            var armorValue = ArmorTiers.GetArmorValue(armor.Level);
            if (armorValue <= 0f)
            {
                return new DamageOutcome(rawDamage, 0f, 1f);
            }

            var reduction = ArmorTiers.GetReduction(armor.Level);
            var durabilityRatio = Clamp01(armor.Durability / armor.MaxDurability);
            var effectiveArmor = armorValue * durabilityRatio;

            var penetrationFactor = Clamp01((Sanitize(request.Penetration) - effectiveArmor) / armorValue);
            var damage = rawDamage * (1f - (reduction * (1f - penetrationFactor)));
            if (damage < 0f)
            {
                damage = 0f;
            }

            var wearFactor = armor.WearFactor > 0f ? armor.WearFactor : settings.DefaultWearFactor;
            var armorDamage = baseDamage * Sanitize(wearFactor);

            // 磨损不会把耐久打成负数：护甲打坏了就停在 0，多余的伤害不会"欠账"。
            if (armorDamage > armor.Durability)
            {
                armorDamage = armor.Durability;
            }

            return new DamageOutcome(damage, armorDamage, penetrationFactor);
        }

        /// <summary>
        /// 判定一次命中是否构成暴击。
        /// </summary>
        /// <param name="hitPoint">命中点世界坐标。</param>
        /// <param name="targetCenter">目标中心世界坐标。</param>
        /// <param name="tuning">全局调参。为 null 时使用默认值。</param>
        /// <returns>命中点在暴击轴上的投影超过阈值时返回 true。</returns>
        /// <remarks>
        /// 判定逻辑独立成方法，是为了让怎么算暴击这件事只有一处实现：
        /// 命中判定与调试显示都调它，不会出现两处规则不一致。
        /// </remarks>
        public static bool IsCriticalHit(Vector3 hitPoint, Vector3 targetCenter, CombatTuning tuning)
        {
            var settings = tuning ?? CombatTuning.Default;
            var offset = Vector3.Dot(hitPoint - targetCenter, settings.CriticalAxis);
            return offset >= settings.CriticalOffsetMeters;
        }

        /// <summary>把负值与 NaN 归零。</summary>
        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || value < 0f)
            {
                return 0f;
            }

            return value;
        }

        /// <summary>把数值限制到 0 到 1。</summary>
        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
