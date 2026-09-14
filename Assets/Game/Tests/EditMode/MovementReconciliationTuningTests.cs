using NUnit.Framework;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 客户端对账容差的取值规则测试。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>P-45。容差曾经写死 0.08 米，
    /// 比"冲刺一步"（6.5 ÷ 60 = 0.108 米）还小，于是冲刺时每一步的时序抖动都被判成错误，
    /// 客户端每次快照都硬吸附一次——表现就是"移动一卡一卡、时不时短距离瞬移"；
    /// 而走路一步只有 0.058 米，同样写死 0.08 米时走路一切正常，这就是"只有冲刺才抖"的原因。</para>
    ///
    /// <para>因此这里钉两条：**一步的位移必须落在容差内**、**大偏差必须触发硬瞬移**。</para>
    /// </remarks>
    [TestFixture]
    public sealed class MovementReconciliationTuningTests
    {
        /// <summary>客户端固定步长（与 SceneBootstrap.Multiplayer 的 NetworkFixedStep 一致）。</summary>
        private const float Step = 1f / 60f;

        /// <summary>走路速度（与 PlayerMovementProfile 默认值一致）。</summary>
        private const float WalkSpeed = 3.5f;

        /// <summary>冲刺速度（与 PlayerMovementProfile 默认值一致）。</summary>
        private const float SprintSpeed = 6.5f;

        /// <summary>冲刺时"一步"的位移约为 0.108 米——它必须被容差覆盖，否则每次快照都会回滚。</summary>
        [Test]
        public void 容差覆盖冲刺一步的位移()
        {
            var oneSprintStep = SprintSpeed * Step;
            var tolerance = MovementReconciliationTuning.ToleranceFor(SprintSpeed, Step);

            Assert.Greater(tolerance, oneSprintStep,
                "容差必须大于冲刺一步的位移，否则正常的时序抖动会被当成错误来修（P-45）。");
            Assert.AreEqual(oneSprintStep * MovementReconciliationTuning.ToleranceSteps, tolerance, 1e-5f);
        }

        /// <summary>走路时容差落到下限：一步 0.058 米远小于 0.12 米，不会反复回滚。</summary>
        [Test]
        public void 走路时容差落到下限()
        {
            var tolerance = MovementReconciliationTuning.ToleranceFor(WalkSpeed, Step);

            Assert.AreEqual(MovementReconciliationTuning.MinimumToleranceMeters, tolerance, 1e-5f);
            Assert.Greater(tolerance, WalkSpeed * Step * 2f, "走路两步的位移也应当在容差内。");
        }

        /// <summary>静止或速度异常时不能把容差算成 0，否则毫米级差异会触发无限回滚。</summary>
        [Test]
        public void 静止与非法速度回落到下限()
        {
            Assert.AreEqual(
                MovementReconciliationTuning.MinimumToleranceMeters,
                MovementReconciliationTuning.ToleranceFor(0f, Step),
                1e-5f);
            Assert.AreEqual(
                MovementReconciliationTuning.MinimumToleranceMeters,
                MovementReconciliationTuning.ToleranceFor(-1f, Step),
                1e-5f);
            Assert.AreEqual(
                MovementReconciliationTuning.MinimumToleranceMeters,
                MovementReconciliationTuning.ToleranceFor(SprintSpeed, 0f),
                1e-5f);
        }

        /// <summary>一步级误差走"平滑吸收"，大跨度位移（传送、重开）才硬瞬移。</summary>
        [Test]
        public void 一步级误差不瞬移而大跨度位移瞬移()
        {
            var oneSprintStep = SprintSpeed * Step;

            Assert.IsFalse(MovementReconciliationTuning.RequiresHardSnap(oneSprintStep),
                "一步级误差应当交给模型自己贴上，而不是瞬移。");
            Assert.IsTrue(MovementReconciliationTuning.RequiresHardSnap(MovementReconciliationTuning.HardSnapMeters),
                "到达硬瞬移阈值时必须瞬移。");
            Assert.IsTrue(MovementReconciliationTuning.RequiresHardSnap(5.6f),
                "出生 / 重开这种大跨度位移必须瞬移，否则模型会飞过一整张地图。");
        }
    }
}
