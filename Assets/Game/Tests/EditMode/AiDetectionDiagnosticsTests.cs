using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Diagnostics;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 开发者模式的"为什么看不见"判定测试。
    /// </summary>
    /// <remarks>
    /// <para>这类调试显示最容易出的问题是**说错原因**：面板写着"被掩体挡住"，
    /// 实际只是距离太远。开发者会照着错误的提示去改地图或掩体，白费半天。
    /// 因此四档原因逐条断言。</para>
    /// </remarks>
    [TestFixture]
    public sealed class AiDetectionDiagnosticsTests
    {
        private const int TargetId = 7;

        private AIPerceptionProfile m_Profile;
        private ScriptedHitProbe m_Probe;

        [SetUp]
        public void SetUp()
        {
            m_Profile = new AIPerceptionProfile
            {
                ViewAngleDegrees = 100f,
                ViewDistanceMeters = 20f,
                EyeHeightMeters = 1.45f,
            };

            m_Probe = new ScriptedHitProbe();
        }

        [Test]
        public void TargetInFront_WithClearLine_IsVisible()
        {
            m_Probe.HitTarget(TargetId);

            var result = Evaluate(new Vector2F(10f, 0f));

            Assert.AreEqual(AiDetectionFailure.Visible, result.Failure, "正前方无遮挡时应当判定为可见。");
            Assert.IsTrue(result.Sees);
            Assert.AreEqual(10f, result.DistanceMeters, 1e-3f);
        }

        [Test]
        public void TargetBlockedByEnvironment_ReportsBlocked()
        {
            m_Probe.ReturnsHit = true;
            // 标识 0 表示只打中了环境（掩体）。
            m_Probe.Result = new HitInfo(0, new Vector3(3f, 1f, 0f), Vector3.zero, 3f);

            var result = Evaluate(new Vector2F(10f, 0f));

            Assert.AreEqual(AiDetectionFailure.Blocked, result.Failure, "视线被掩体截断时应当报告'被掩体挡住'。");
            Assert.AreEqual("被掩体挡住", result.Describe());
        }

        [Test]
        public void TargetBeyondViewDistance_ReportsOutOfRange()
        {
            m_Probe.HitTarget(TargetId);

            var result = Evaluate(new Vector2F(25f, 0f));

            Assert.AreEqual(AiDetectionFailure.OutOfRange, result.Failure, "超出视距时应当报告'超距'，而不是'被挡住'。");
        }

        [Test]
        public void TargetBehindObserver_ReportsOutsideViewCone()
        {
            m_Probe.HitTarget(TargetId);

            var result = Evaluate(new Vector2F(-10f, 0f));

            Assert.AreEqual(AiDetectionFailure.OutsideViewCone, result.Failure, "背后的目标应当报告'在视野锥外'。");
        }

        [Test]
        public void TargetAtNinetyDegrees_ReportsAngleInResult()
        {
            m_Probe.HitTarget(TargetId);

            // 视野全角 100 度，因此 90 度方向在锥外，且报告的夹角应当接近 90 度。
            var result = Evaluate(new Vector2F(0f, 10f));

            Assert.AreEqual(AiDetectionFailure.OutsideViewCone, result.Failure);
            Assert.AreEqual(90f, result.AngleDegrees, 0.5f, "夹角应当如实报告，供面板显示。");
        }

        [Test]
        public void TargetInsideConeEdge_IsVisible()
        {
            m_Probe.HitTarget(TargetId);

            // 45 度在 100 度视野的半角（50 度）以内。
            var position = Vector2F.FromDegrees(45f) * 10f;
            var result = Evaluate(position);

            Assert.AreEqual(AiDetectionFailure.Visible, result.Failure, "锥内目标应当可见。");
        }

        [Test]
        public void NoTarget_ReportsNoTarget()
        {
            m_Probe.HitTarget(TargetId);

            var target = new AiTargetInfo(TargetId, new Vector2F(10f, 0f), new Vector3(10f, 1f, 0f), isAlive: false);
            var result = AiDetectionDiagnostics.Evaluate(
                target,
                Vector2F.Zero,
                Vector2F.Right,
                m_Profile,
                m_Probe);

            Assert.AreEqual(AiDetectionFailure.NoTarget, result.Failure, "目标阵亡后应当报告'无目标'。");
        }

        [Test]
        public void MissingProbe_ReportsBlocked_NotVisible()
        {
            var result = AiDetectionDiagnostics.Evaluate(
                CreateTarget(),
                Vector2F.Zero,
                Vector2F.Right,
                m_Profile,
                probe: null);

            Assert.AreEqual(
                AiDetectionFailure.Blocked,
                result.Failure,
                "没有射线能力时不能谎报可见：宁可显示'被挡住'，也不要让面板与 AI 的真实判定不一致。");
        }

        /// <summary>用默认朝向（正右）与指定目标位置做一次判定。</summary>
        private AiDetectionResult Evaluate(Vector2F targetPosition)
        {
            return AiDetectionDiagnostics.Evaluate(
                CreateTarget(targetPosition),
                Vector2F.Zero,
                Vector2F.Right,
                m_Profile,
                m_Probe);
        }

        private static AiTargetInfo CreateTarget(Vector2F position = default)
        {
            var actual = position.IsNearlyZero ? new Vector2F(10f, 0f) : position;
            return new AiTargetInfo(
                TargetId,
                actual,
                new Vector3(actual.X, 1f, actual.Y),
                isAlive: true);
        }
    }
}
