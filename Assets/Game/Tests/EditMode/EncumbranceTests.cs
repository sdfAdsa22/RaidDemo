using NUnit.Framework;
using RaidDemo.Inventory;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 负重判定与移动修正的边界测试。
    /// </summary>
    /// <remarks>
    /// 三段式负重是"贪婪循环"的物理代价所在，因此每个阈值两侧都要有用例。
    /// 边界值写错时，玩家会感到"明明没超重却跑不动"，而这种问题极难在实机定位。
    /// </remarks>
    [TestFixture]
    public sealed class EncumbranceTests
    {
        private const float Capacity = 100f;

        private EncumbranceProfile m_Profile;

        [SetUp]
        public void SetUp()
        {
            m_Profile = new EncumbranceProfile { CapacityKg = Capacity };
        }

        [Test]
        public void Evaluate_BelowHeavyThreshold_IsLight()
        {
            var state = EncumbranceRules.Evaluate(69f, m_Profile);

            Assert.AreEqual(EncumbranceState.Light, state, "负重比 0.69 应判为轻装。");
        }

        [Test]
        public void Evaluate_ExactlyAtHeavyThreshold_IsHeavy()
        {
            var state = EncumbranceRules.Evaluate(70f, m_Profile);

            Assert.AreEqual(EncumbranceState.Heavy, state, "阈值取闭区间下界，恰好 0.70 应判为重装。");
        }

        [Test]
        public void Evaluate_ExactlyAtCapacity_IsStillHeavy()
        {
            var state = EncumbranceRules.Evaluate(100f, m_Profile);

            Assert.AreEqual(EncumbranceState.Heavy, state, "恰好装满不算超重，不应提前惩罚。");
        }

        [Test]
        public void Evaluate_AboveCapacity_IsOverloaded()
        {
            var state = EncumbranceRules.Evaluate(101f, m_Profile);

            Assert.AreEqual(EncumbranceState.Overloaded, state, "超过上限 1% 即进入超重。");
        }

        [Test]
        public void Light_HasNoPenalty()
        {
            var modifiers = EncumbranceRules.ResolveModifiers(EncumbranceState.Light, 0.5f);

            Assert.AreEqual(1f, modifiers.SpeedMultiplier, 1e-4f, "轻装不应减速。");
            Assert.AreEqual(1f, modifiers.StaminaRegenMultiplier, 1e-4f, "轻装不应影响体力恢复。");
            Assert.IsTrue(modifiers.CanSprint, "轻装应能奔跑。");
        }

        [Test]
        public void Heavy_ForbidsSprintAndHalvesRecovery()
        {
            var modifiers = EncumbranceRules.ResolveModifiers(EncumbranceState.Heavy, 0.85f);

            Assert.IsFalse(modifiers.CanSprint, "重装应失去奔跑能力。");
            Assert.AreEqual(0.5f, modifiers.StaminaRegenMultiplier, 1e-4f, "重装时体力恢复应减半。");
            Assert.AreEqual(1f, modifiers.SpeedMultiplier, 1e-4f, "重装还不应减速，减速是超重才有的惩罚。");
        }

        [Test]
        public void Overloaded_ForbidsSprintAndStopsRecovery()
        {
            var modifiers = EncumbranceRules.ResolveModifiers(EncumbranceState.Overloaded, 1.2f);

            Assert.IsFalse(modifiers.CanSprint, "超重应失去奔跑能力。");
            Assert.AreEqual(0f, modifiers.StaminaRegenMultiplier, 1e-4f, "超重时体力完全不恢复。");
        }

        [Test]
        public void OverloadedSpeed_ReachesFloorAtOneAndHalfCapacity()
        {
            var modifier = EncumbranceRules.ResolveSpeedMultiplier(1.5f);

            Assert.AreEqual(EncumbranceRules.OverloadSpeedFloor, modifier, 1e-4f,
                "负重达到上限的 1.5 倍时速度应触底到 0.4 倍。");
        }

        [Test]
        public void OverloadedSpeed_DoesNotDropBelowFloor()
        {
            var modifier = EncumbranceRules.ResolveSpeedMultiplier(3f);

            Assert.AreEqual(EncumbranceRules.OverloadSpeedFloor, modifier, 1e-4f,
                "继续增重不应让速度跌破下限，否则玩家会被钉在原地失去所有操作空间。");
        }

        [Test]
        public void OverloadedSpeed_AtCapacity_IsStillFullSpeed()
        {
            var modifier = EncumbranceRules.ResolveSpeedMultiplier(1f);

            Assert.AreEqual(1f, modifier, 1e-4f, "恰好装满时不应有任何减速。");
        }

        [Test]
        public void Resolve_FromWeight_ProducesConsistentState()
        {
            var heavy = EncumbranceRules.Resolve(80f, m_Profile);
            var light = EncumbranceRules.Resolve(10f, m_Profile);

            Assert.IsFalse(heavy.CanSprint, "80% 负重应判为重装并禁止奔跑。");
            Assert.IsTrue(light.CanSprint, "10% 负重不应有任何限制。");
        }

        [Test]
        public void Evaluate_WithZeroCapacity_DoesNotThrow()
        {
            var zero = new EncumbranceProfile { CapacityKg = 0f };

            var state = EncumbranceRules.Evaluate(10f, zero);
            var modifiers = EncumbranceRules.Resolve(10f, zero);

            Assert.AreEqual(EncumbranceState.Light, state,
                "上限非正属于配置错误，按没有负重约束处理，不应让玩家寸步难行。");
            Assert.AreEqual(1f, modifiers.SpeedMultiplier, 1e-4f, "配置缺失时不应减速。");
        }

        [Test]
        public void Evaluate_WithNullProfile_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EncumbranceRules.Evaluate(10f, null), "配置缺失不应抛异常。");
            Assert.AreEqual(
                EncumbranceState.Light,
                EncumbranceRules.Evaluate(10f, null),
                "没有配置时应视为没有负重约束。");
        }

        [Test]
        public void Profile_Validate_ReportsBadValues()
        {
            var bad = new EncumbranceProfile { CapacityKg = 0f };
            var badSpan = new EncumbranceProfile { CapacityKg = 30f, OverloadSpanRatio = 0f };

            Assert.IsNotNull(bad.Validate(), "承载上限非正应当被校验拦下。");
            Assert.IsNotNull(badSpan.Validate(), "衰减跨度为 0 时超重会瞬间触底，应当被校验拦下。");
            Assert.IsNull(new EncumbranceProfile().Validate(), "默认配置应当通过校验。");
        }
    }
}
