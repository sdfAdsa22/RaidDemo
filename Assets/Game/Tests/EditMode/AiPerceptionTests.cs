using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 感知测试：视野锥、遮挡、听觉半径与参数校验。
    /// </summary>
    /// <remarks>
    /// 感知是 AI 的输入。它错了，后面所有状态都会"合逻辑地做错事"，
    /// 因此这里逐条钉死边界：锥外、锥内、超距、有遮挡、无声、超听距。
    /// </remarks>
    [TestFixture]
    public sealed class AiPerceptionTests
    {
        private AIPerceptionProfile m_Profile;

        [SetUp]
        public void SetUp()
        {
            m_Profile = new AIPerceptionProfile
            {
                ViewAngleDegrees = 100f,
                ViewDistanceMeters = 20f,
                HearingRadiusWalk = 8f,
                HearingRadiusSprint = 18f,
                HearingRadiusOverloaded = 26f,
            };
        }

        [Test]
        public void ViewCone_TargetAhead_IsVisible()
        {
            var visible = m_Profile.IsInsideViewCone(
                Vector2F.Zero,
                Vector2F.Right,
                new Vector2F(10f, 0f));

            Assert.IsTrue(visible, "正前方 10 米处的目标应当可见。");
        }

        [Test]
        public void ViewCone_TargetBehind_IsNotVisible()
        {
            var visible = m_Profile.IsInsideViewCone(
                Vector2F.Zero,
                Vector2F.Right,
                new Vector2F(-10f, 0f));

            Assert.IsFalse(visible, "背后的目标必须看不见，否则绕后毫无意义。");
        }

        [Test]
        public void ViewCone_BeyondDistance_IsNotVisible()
        {
            var visible = m_Profile.IsInsideViewCone(
                Vector2F.Zero,
                Vector2F.Right,
                new Vector2F(25f, 0f));

            Assert.IsFalse(visible, "超出视距的目标应当看不见。");
        }

        [Test]
        public void ViewCone_AtConeEdge_IsVisible()
        {
            // 视野全角 100 度，因此边界在正前方左右各 50 度处。
            var inside = m_Profile.IsInsideViewCone(
                Vector2F.Zero,
                Vector2F.Right,
                Vector2F.FromDegrees(49f) * 10f);

            var outside = m_Profile.IsInsideViewCone(
                Vector2F.Zero,
                Vector2F.Right,
                Vector2F.FromDegrees(51f) * 10f);

            Assert.IsTrue(inside, "锥内 49 度应当可见。");
            Assert.IsFalse(outside, "锥外 51 度应当不可见。");
        }

        [Test]
        public void LineOfSight_FirstHitIsTarget_IsVisible()
        {
            var probe = new ScriptedHitProbe();
            probe.HitTarget(targetId: 7);

            var visible = AISensor.HasLineOfSight(
                probe,
                new Vector3(0f, 1.4f, 0f),
                new Vector3(0f, 1f, 5f),
                targetId: 7,
                maxDistance: 20f);

            Assert.IsTrue(visible, "射线第一个命中的就是目标时应当判定为可见。");
        }

        [Test]
        public void LineOfSight_WallBlocked_IsNotVisible()
        {
            var probe = new ScriptedHitProbe
            {
                ReturnsHit = true,
                // 标识 0 表示只打中了环境，也就是掩体。
                Result = new HitInfo(0, new Vector3(0f, 1f, 2f), Vector3.zero, 2f),
            };

            var visible = AISensor.HasLineOfSight(
                probe,
                new Vector3(0f, 1.4f, 0f),
                new Vector3(0f, 1f, 5f),
                targetId: 7,
                maxDistance: 20f);

            Assert.IsFalse(visible, "掩体挡住时必须判定为不可见，否则掩体形同虚设。");
        }

        [Test]
        public void LineOfSight_NothingHit_IsNotVisible()
        {
            var probe = new ScriptedHitProbe();
            probe.Miss();

            var visible = AISensor.HasLineOfSight(
                probe,
                new Vector3(0f, 1.4f, 0f),
                new Vector3(0f, 1f, 5f),
                targetId: 7,
                maxDistance: 20f);

            Assert.IsFalse(visible, "射线什么都没打中属于异常装配，保守地当作看不见。");
        }

        [Test]
        public void Hearing_RadiusFollowsTier()
        {
            Assert.AreEqual(0f, m_Profile.HearingRadiusFor(MovementNoiseTier.Silent), "静止应当完全静默。");
            Assert.AreEqual(8f, m_Profile.HearingRadiusFor(MovementNoiseTier.Walk));
            Assert.AreEqual(18f, m_Profile.HearingRadiusFor(MovementNoiseTier.Sprint));
            Assert.AreEqual(26f, m_Profile.HearingRadiusFor(MovementNoiseTier.Overloaded));
        }

        [Test]
        public void Hearing_SprintTravelsFurtherThanWalk()
        {
            var noise = new Vector2F(12f, 0f);

            Assert.IsTrue(
                AISensor.CanHear(Vector2F.Zero, noise, MovementNoiseTier.Sprint, m_Profile),
                "12 米处在奔跑噪音（18 米）范围内。");
            Assert.IsFalse(
                AISensor.CanHear(Vector2F.Zero, noise, MovementNoiseTier.Walk, m_Profile),
                "12 米处超出了步行噪音（8 米）范围。");
        }

        [Test]
        public void NoiseClassifier_OverloadedWinsOverSpeed()
        {
            // 超载会把速度压到奔跑阈值以下，若先判速度就永远命中不了超载档。
            var tier = MovementNoiseRules.Classify(
                speed: 3f,
                sprintSpeedThreshold: 4.5f,
                isOverloaded: true);

            Assert.AreEqual(MovementNoiseTier.Overloaded, tier, "超载移动应当按超载档计噪。");
        }

        [Test]
        public void NoiseClassifier_StandingStillIsSilent()
        {
            var tier = MovementNoiseRules.Classify(
                speed: 0f,
                sprintSpeedThreshold: 4.5f,
                isOverloaded: true);

            Assert.AreEqual(
                MovementNoiseTier.Silent,
                tier,
                "站着不动必须静音，否则玩家无法通过停下来摆脱追踪。");
        }

        [Test]
        public void Profile_Validate_RejectsIllegalHearingOrder()
        {
            var broken = new AIPerceptionProfile
            {
                HearingRadiusWalk = 20f,
                HearingRadiusSprint = 10f,
            };

            Assert.IsNotNull(broken.Validate(), "听觉半径顺序颠倒时应当被校验拦下。");
        }

        [Test]
        public void Profile_Validate_AcceptsDefaults()
        {
            Assert.IsNull(new AIPerceptionProfile().Validate(), "默认参数必须合法，否则每次启动都会报配置错误。");
        }
    }
}
