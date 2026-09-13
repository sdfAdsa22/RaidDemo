using NUnit.Framework;
using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 移动动画播放倍率的换算规则测试（A-02）。
    /// </summary>
    /// <remarks>
    /// 滑步是"看起来不对"的表现问题，没法用断言直接检查画面；能钉住的只有换算规则本身：
    /// 设计速度公式、倍率上下限、站定与缺数据时的回退。规则正确之后，
    /// 剩下的偏差只可能来自设计速度的取值，而那由角色动画接线测试去核对资产数据。
    /// </remarks>
    [TestFixture]
    public sealed class LocomotionAnimationRateTests
    {
        [Test]
        public void 设计速度按两步一周期的步幅除以剪辑长度()
        {
            // 1 秒的循环迈两步：走路 2×0.95=1.9 米，奔跑 2×1.55=3.1 米。
            var clip = CreateClipWithLength(1f);

            Assert.AreEqual(
                1.9f,
                LocomotionAnimationRate.DesignSpeed(clip, FootstepCadence.WalkStrideMeters),
                0.001f,
                "走路设计速度应为 2 × 步幅 ÷ 剪辑长度。");
            Assert.AreEqual(
                3.1f,
                LocomotionAnimationRate.DesignSpeed(clip, FootstepCadence.SprintStrideMeters),
                0.001f,
                "奔跑设计速度应为 2 × 步幅 ÷ 剪辑长度。");
        }

        [Test]
        public void 倍率等于实际速度除以设计速度()
        {
            // 玩家步行 3.5 m/s、Kenney walk 剪辑 0.667 秒 → 设计速度 2.849 m/s → 倍率 1.229。
            Assert.AreEqual(
                1.229f,
                LocomotionAnimationRate.Calculate(3.5f, 2.849f),
                0.001f,
                "倍率必须严格等于速度 ÷ 设计速度，不能引入额外的曲线或档位。");
        }

        [Test]
        public void 倍率限制在上下限之间()
        {
            Assert.AreEqual(
                LocomotionAnimationRate.MinRate,
                LocomotionAnimationRate.Calculate(0.5f, 2f),
                0.0001f,
                "低于下限应取 MinRate，防止超载时动作慢成定格。");
            Assert.AreEqual(
                LocomotionAnimationRate.MaxRate,
                LocomotionAnimationRate.Calculate(10f, 2f),
                0.0001f,
                "高于上限应取 MaxRate，防止异常速度把动作变成快进。");
        }

        [Test]
        public void 站定与缺少设计速度时不缩放()
        {
            Assert.AreEqual(
                1f,
                LocomotionAnimationRate.Calculate(LocomotionAnimationRate.IdleSpeedThreshold - 0.01f, 2f),
                0.0001f,
                "站定附近应返回 1，让状态机自己切回待机，而不是播放慢动作收尾。");
            Assert.AreEqual(
                1f,
                LocomotionAnimationRate.Calculate(3.5f, 0f),
                0.0001f,
                "设计速度缺失时应退回不缩放：漏接数据不能比修复前更糟。");
            Assert.AreEqual(
                1f,
                LocomotionAnimationRate.Calculate(3.5f, -1f),
                0.0001f,
                "设计速度为负数属于非法数据，同样退回不缩放。");
        }

        [Test]
        public void 剪辑或步幅非法时设计速度为零()
        {
            Assert.AreEqual(
                0f,
                LocomotionAnimationRate.DesignSpeed(null, FootstepCadence.WalkStrideMeters),
                0.0001f,
                "没有剪辑时设计速度不存在，应返回 0 交由调用方回退。");
            Assert.AreEqual(
                0f,
                LocomotionAnimationRate.DesignSpeed(CreateClipWithLength(1f), 0f),
                0.0001f,
                "步幅为 0 时无法推算设计速度，应返回 0。");
        }

        /// <summary>用一条从 0 到 <paramref name="length"/> 秒的曲线造出指定长度的剪辑。</summary>
        private static AnimationClip CreateClipWithLength(float length)
        {
            var clip = new AnimationClip();
            var curve = AnimationCurve.Linear(0f, 0f, length, 1f);
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", curve);
            return clip;
        }
    }
}
