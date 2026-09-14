using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Combat;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 倒地与救援规则的测试（M9 · P3.5）。
    /// </summary>
    /// <remarks>
    /// 这里钉住的是三条规则：**倒计时走到 0 才死**、**救满时长才起得来**、**松手即清零**。
    /// 它们都是"数值 + 时序"，靠实机观察很难验证（要盯着秒表看），因此必须在逻辑层测。
    /// </remarks>
    [TestFixture]
    public sealed class PlayerLifeStateTests
    {
        private const int RescuerId = 1;
        private const int TargetId = 2;

        private PlayerLifeStateTracker m_Tracker;
        private List<int> m_BledOut;
        private List<int> m_Revived;

        [SetUp]
        public void SetUp()
        {
            m_Tracker = new PlayerLifeStateTracker();
            m_Tracker.Register(RescuerId);
            m_Tracker.Register(TargetId);
            m_BledOut = new List<int>();
            m_Revived = new List<int>();
        }

        /// <summary>推进指定的秒数（按 1/60 秒一步，模拟真实调用节奏）。</summary>
        private void Advance(float seconds)
        {
            const float step = 1f / 60f;
            for (var elapsed = 0f; elapsed < seconds; elapsed += step)
            {
                m_Tracker.Tick(step, m_BledOut, m_Revived);
            }
        }

        /// <summary>倒地之后不会立刻死，而是进入失能。</summary>
        [Test]
        public void 倒地进入失能而不是死亡()
        {
            Assert.IsTrue(m_Tracker.MarkDowned(TargetId));
            Assert.AreEqual(PlayerLifeState.Downed, m_Tracker.GetState(TargetId));
            Assert.AreEqual(
                PlayerLifeStateTracker.BleedOutSeconds,
                m_Tracker.GetBleedOutRemaining(TargetId),
                0.001f);
        }

        /// <summary>倒计时走完才死亡，而且只报一次。</summary>
        [Test]
        public void 倒计时走完才死亡()
        {
            m_Tracker.MarkDowned(TargetId);

            Advance(PlayerLifeStateTracker.BleedOutSeconds * 0.5f);
            Assert.AreEqual(PlayerLifeState.Downed, m_Tracker.GetState(TargetId), "还没到时间不该死。");
            Assert.AreEqual(0, m_BledOut.Count);

            Advance(PlayerLifeStateTracker.BleedOutSeconds * 0.6f);
            Assert.AreEqual(PlayerLifeState.Dead, m_Tracker.GetState(TargetId));
            Assert.AreEqual(1, m_BledOut.Count, "死亡只应上报一次。");
        }

        /// <summary>救满时长即被救起，并恢复可行动状态。</summary>
        [Test]
        public void 救满时长后被救起()
        {
            m_Tracker.MarkDowned(TargetId);

            m_Tracker.AddReviveProgress(TargetId, PlayerLifeStateTracker.ReviveSeconds - 0.1f);
            Advance(0.05f);
            Assert.AreEqual(PlayerLifeState.Downed, m_Tracker.GetState(TargetId), "差一点不该起来。");

            m_Tracker.AddReviveProgress(TargetId, 0.2f);
            Advance(0.05f);
            Assert.AreEqual(PlayerLifeState.Alive, m_Tracker.GetState(TargetId));
            Assert.AreEqual(1, m_Revived.Count);
            Assert.AreEqual(0f, m_Tracker.GetBleedOutRemaining(TargetId), "起来之后不应再有倒计时。");
        }

        /// <summary>松手（中断）会把进度清零，不能"救一半留着"。</summary>
        [Test]
        public void 中断施救会清空进度()
        {
            m_Tracker.MarkDowned(TargetId);
            m_Tracker.AddReviveProgress(TargetId, PlayerLifeStateTracker.ReviveSeconds * 0.8f);
            m_Tracker.InterruptRevive(TargetId);

            Assert.AreEqual(0f, m_Tracker.GetReviveProgress(TargetId), 0.001f);

            Advance(0.2f);
            Assert.AreEqual(PlayerLifeState.Downed, m_Tracker.GetState(TargetId), "进度清零后不该自己起来。");
        }

        /// <summary>施救距离：远处不能救，近处可以；自己不能救自己。</summary>
        [Test]
        public void 施救距离与自身限制()
        {
            m_Tracker.MarkDowned(TargetId);

            var target = new Vector2F(0f, 0f);
            var near = new Vector2F(PlayerLifeStateTracker.ReviveRangeMeters - 0.1f, 0f);
            var far = new Vector2F(PlayerLifeStateTracker.ReviveRangeMeters + 0.1f, 0f);

            Assert.IsTrue(m_Tracker.CanRevive(RescuerId, TargetId, near, target), "贴身的队友应当能救。");
            Assert.IsFalse(m_Tracker.CanRevive(RescuerId, TargetId, far, target), "离得太远不该能救。");
            Assert.IsFalse(m_Tracker.CanRevive(TargetId, TargetId, target, target), "不能自己救自己。");
        }

        /// <summary>死亡之后不能再被救起；存活的人不会被打成"失能"。</summary>
        [Test]
        public void 死亡不可逆且存活者不会二次倒地()
        {
            m_Tracker.MarkDowned(TargetId);
            Advance(PlayerLifeStateTracker.BleedOutSeconds + 0.5f);
            Assert.AreEqual(PlayerLifeState.Dead, m_Tracker.GetState(TargetId));

            Assert.IsFalse(m_Tracker.MarkDowned(TargetId), "已经死亡的人不该再进入失能。");
            Assert.IsFalse(
                m_Tracker.AddReviveProgress(TargetId, 1f),
                "死亡之后不该再累积施救进度。");
            Assert.IsFalse(
                m_Tracker.CanRevive(RescuerId, TargetId, Vector2F.Zero, Vector2F.Zero),
                "死亡的人不能被救。");
        }
    }
}
