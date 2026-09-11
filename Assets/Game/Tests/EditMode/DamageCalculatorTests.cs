using NUnit.Framework;
using RaidDemo.Combat;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 伤害结算测试。
    /// </summary>
    /// <remarks>
    /// <para>这是 M3 的核心测试套件。伤害公式是全项目最容易算错、也最难靠肉眼发现问题的一段：
    /// 数值错了 15% 谁都不会察觉，但它决定了整个战斗系统的平衡。</para>
    /// <para>所有用例都是纯数学断言，不依赖场景、不依赖资产，因此可以在极短时间内穷举边界。</para>
    /// </remarks>
    [TestFixture]
    public sealed class DamageCalculatorTests
    {
        /// <summary>测试用的基础伤害。</summary>
        private const float BaseDamage = 25f;

        private CombatTuning m_Tuning;

        [SetUp]
        public void SetUp()
        {
            m_Tuning = new CombatTuning();
        }

        /// <summary>构造一次伤害请求。</summary>
        private static DamageRequest Request(
            float penetration,
            int armorLevel = 0,
            float durability = 0f,
            float maxDurability = 0f,
            bool isCritical = false,
            float wearFactor = 0.35f)
        {
            var armor = new ArmorSnapshot(armorLevel, durability, maxDurability, wearFactor);
            return new DamageRequest(BaseDamage, penetration, armor, isCritical);
        }

        [Test]
        public void NoArmor_TakesFullDamage()
        {
            var outcome = DamageCalculator.Resolve(Request(penetration: 0f), m_Tuning);

            Assert.AreEqual(BaseDamage, outcome.Damage, 1e-3f, "无护甲时应当承受全额基础伤害。");
            Assert.AreEqual(0f, outcome.ArmorDamage, 1e-3f, "无护甲时不应产生护甲损耗。");
            Assert.AreEqual(1f, outcome.PenetrationFactor, 1e-3f, "无护甲时穿透系数视为 1。");
        }

        [Test]
        public void PenetrationBelowArmor_FullReductionApplies()
        {
            // 3 级甲：护甲值 30、减伤 50%。穿透力 15 明显不足。
            var outcome = DamageCalculator.Resolve(
                Request(15f, armorLevel: 3, durability: 30f, maxDurability: 30f), m_Tuning);

            Assert.AreEqual(0f, outcome.PenetrationFactor, 1e-3f, "穿透力低于护甲值时穿透系数为 0。");
            Assert.AreEqual(BaseDamage * 0.5f, outcome.Damage, 1e-3f, "减伤 50% 应当全额生效。");
        }

        [Test]
        public void PenetrationAboveArmor_IgnoresReduction()
        {
            // 穿透力 60 是护甲值 30 的两倍，穿透系数达到上限 1。
            var outcome = DamageCalculator.Resolve(
                Request(60f, armorLevel: 3, durability: 30f, maxDurability: 30f), m_Tuning);

            Assert.AreEqual(1f, outcome.PenetrationFactor, 1e-3f, "穿透力达到护甲值两倍时完全穿透。");
            Assert.AreEqual(BaseDamage, outcome.Damage, 1e-3f, "完全穿透时伤害不被减免。");
            Assert.IsTrue(outcome.IgnoredArmor, "穿透系数为 1 时应当被视为无视护甲。");
        }

        [Test]
        public void PenetrationExactlyEqualsArmor_StillFullReduction()
        {
            // 边界：穿透力恰好等于护甲值时穿透系数为 0，与"完全打不穿"一致。
            var outcome = DamageCalculator.Resolve(
                Request(30f, armorLevel: 3, durability: 30f, maxDurability: 30f), m_Tuning);

            Assert.AreEqual(0f, outcome.PenetrationFactor, 1e-3f, "穿透力等于护甲值时穿透系数应当是 0。");
            Assert.AreEqual(BaseDamage * 0.5f, outcome.Damage, 1e-3f, "此时减伤仍应全额生效。");
        }

        [Test]
        public void PartialPenetration_ScalesDamageBetweenBothExtremes()
        {
            // 穿透力 45：比护甲值高一半，穿透系数 0.5，减伤只生效一半。
            var outcome = DamageCalculator.Resolve(
                Request(45f, armorLevel: 3, durability: 30f, maxDurability: 30f), m_Tuning);

            Assert.AreEqual(0.5f, outcome.PenetrationFactor, 1e-3f, "穿透系数应当是 0.5。");

            var expected = BaseDamage * (1f - (0.5f * 0.5f));
            Assert.AreEqual(expected, outcome.Damage, 1e-3f, "部分穿透时伤害应当落在两个极端之间。");
        }

        [Test]
        public void WornArmor_LetsMoreDamageThrough()
        {
            var fresh = DamageCalculator.Resolve(
                Request(25f, armorLevel: 3, durability: 30f, maxDurability: 30f), m_Tuning);
            var worn = DamageCalculator.Resolve(
                Request(25f, armorLevel: 3, durability: 15f, maxDurability: 30f), m_Tuning);

            Assert.Greater(worn.PenetrationFactor, fresh.PenetrationFactor,
                "耐久减半后有效护甲值下降，穿透系数应当上升。");
            Assert.Greater(worn.Damage, fresh.Damage,
                "打旧了的护甲应当让更多伤害透过去，这就是越打越脆的可感知表现。");
        }

        [Test]
        public void BrokenArmor_BehavesLikeNoArmor()
        {
            var outcome = DamageCalculator.Resolve(
                Request(0f, armorLevel: 3, durability: 0f, maxDurability: 30f), m_Tuning);

            Assert.AreEqual(1f, outcome.PenetrationFactor, 1e-3f, "耐久归零后穿透系数为 1。");
            Assert.AreEqual(BaseDamage, outcome.Damage, 1e-3f, "护甲打坏后等同于无甲。");
            Assert.AreEqual(0f, outcome.ArmorDamage, 1e-3f, "已经坏掉的护甲不应继续扣除耐久。");
        }

        [Test]
        public void CriticalHit_DoublesDamageBeforeArmor()
        {
            var normal = DamageCalculator.Resolve(
                Request(15f, armorLevel: 3, durability: 30f, maxDurability: 30f), m_Tuning);
            var critical = DamageCalculator.Resolve(
                Request(15f, armorLevel: 3, durability: 30f, maxDurability: 30f, isCritical: true), m_Tuning);

            Assert.AreEqual(normal.Damage * 2f, critical.Damage, 1e-3f,
                "暴击倍率应当在护甲结算之前生效，而不是与减伤互相抵消。");
        }

        [Test]
        public void ArmorWear_FollowsBaseDamageTimesWearFactor()
        {
            var outcome = DamageCalculator.Resolve(
                Request(15f, armorLevel: 2, durability: 60f, maxDurability: 60f, wearFactor: 0.4f),
                m_Tuning);

            Assert.AreEqual(BaseDamage * 0.4f, outcome.ArmorDamage, 1e-3f,
                "护甲损耗等于基础伤害乘以磨损系数，与被减免后的最终伤害无关。");
        }

        [Test]
        public void ArmorWear_NeverExceedsRemainingDurability()
        {
            // 只剩 1 点耐久，但一次命中按磨损系数会扣掉 8.75。
            var outcome = DamageCalculator.Resolve(
                Request(15f, armorLevel: 2, durability: 1f, maxDurability: 60f), m_Tuning);

            Assert.AreEqual(1f, outcome.ArmorDamage, 1e-3f, "护甲损耗不应当超过剩余耐久。");
        }

        [Test]
        public void ExtremeValues_DoNotProduceNegativeDamageOrThrow()
        {
            var zeroDamage = DamageCalculator.Resolve(
                new DamageRequest(0f, 0f, ArmorSnapshot.None), m_Tuning);
            var negativeDurability = DamageCalculator.Resolve(
                Request(15f, armorLevel: 3, durability: -5f, maxDurability: 30f), m_Tuning);
            var nanBase = DamageCalculator.Resolve(
                new DamageRequest(float.NaN, 0f, ArmorSnapshot.None), m_Tuning);
            var negativePenetration = DamageCalculator.Resolve(
                Request(-100f, armorLevel: 3, durability: 30f, maxDurability: 30f), m_Tuning);

            Assert.AreEqual(0f, zeroDamage.Damage, 1e-3f, "0 伤害应当算出 0。");
            Assert.GreaterOrEqual(negativeDurability.Damage, 0f, "负耐久不应算出负伤害。");
            Assert.AreEqual(0f, nanBase.Damage, 1e-3f, "NaN 输入应当归零而不是传播出去。");
            Assert.GreaterOrEqual(negativePenetration.Damage, 0f, "负穿透力不应算出负伤害。");
        }

        [Test]
        public void ArmorTiers_MatchTheDesignTable()
        {
            Assert.AreEqual(0f, ArmorTiers.GetReduction(0), 1e-4f, "无甲不减伤。");
            Assert.AreEqual(0.20f, ArmorTiers.GetReduction(1), 1e-4f, "1 级甲 20% 减伤。");
            Assert.AreEqual(0.35f, ArmorTiers.GetReduction(2), 1e-4f, "2 级甲 35% 减伤。");
            Assert.AreEqual(0.50f, ArmorTiers.GetReduction(3), 1e-4f, "3 级甲 50% 减伤。");
            Assert.AreEqual(0.65f, ArmorTiers.GetReduction(4), 1e-4f, "4 级甲 65% 减伤。");

            Assert.AreEqual(30f, ArmorTiers.GetArmorValue(3), 1e-4f, "3 级甲的护甲值是 30。");
            Assert.AreEqual(0f, ArmorTiers.GetArmorValue(0), 1e-4f, "无甲的护甲值是 0。");
        }

        [Test]
        public void ArmorTiers_ClampOutOfRangeLevels()
        {
            Assert.AreEqual(40f, ArmorTiers.GetArmorValue(9), 1e-4f, "超过 4 级的等级应当被截断到 4 级。");
            Assert.AreEqual(0f, ArmorTiers.GetReduction(-3), 1e-4f, "负等级视为无甲。");
        }

        [Test]
        public void CriticalHit_UsesScreenUpAxis()
        {
            m_Tuning.CriticalAxis = new Vector3(0f, 0f, 1f);
            m_Tuning.CriticalOffsetMeters = 0.25f;

            var center = new Vector3(10f, 0.9f, 4f);
            var upperPart = center + new Vector3(0f, 0f, 0.4f);
            var lowerPart = center + new Vector3(0f, 0f, -0.4f);

            Assert.IsTrue(
                DamageCalculator.IsCriticalHit(upperPart, center, m_Tuning),
                "命中点偏向暴击轴正方向时应当判定为暴击。");
            Assert.IsFalse(
                DamageCalculator.IsCriticalHit(lowerPart, center, m_Tuning),
                "命中点偏向反方向时不应当判定为暴击。");
            Assert.IsFalse(
                DamageCalculator.IsCriticalHit(center, center, m_Tuning),
                "正中目标中心时偏移为 0，不构成暴击。");
        }

        [Test]
        public void CriticalAxis_FollowsCameraOrientation()
        {
            // 相机转向之后，暴击方向应当跟着变——这正是把轴做成可配置项的原因。
            m_Tuning.CriticalAxis = new Vector3(1f, 0f, 0f);
            m_Tuning.CriticalOffsetMeters = 0.25f;

            var center = Vector3.zero;
            var alongNewAxis = new Vector3(0.4f, 0f, 0f);
            var alongOldAxis = new Vector3(0f, 0f, 0.4f);

            Assert.IsTrue(
                DamageCalculator.IsCriticalHit(alongNewAxis, center, m_Tuning),
                "沿新的暴击轴偏移应当判定为暴击。");
            Assert.IsFalse(
                DamageCalculator.IsCriticalHit(alongOldAxis, center, m_Tuning),
                "沿旧的暴击轴偏移不再构成暴击。");
        }
    }
}
