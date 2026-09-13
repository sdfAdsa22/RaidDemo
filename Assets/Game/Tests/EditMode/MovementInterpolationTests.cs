using NUnit.Framework;
using RaidDemo.Shared;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 远端角色插值缓冲的行为测试（M9 · P1）。
    /// </summary>
    /// <remarks>
    /// 远端玩家看到的是「20 Hz 快照 + 100 ms 延迟」重建出来的轨迹，
    /// 这些用例把"重建规则"钉死，避免以后调参数时把两端钳制、保留条数这类边界悄悄改坏。
    /// </remarks>
    [TestFixture]
    public sealed class MovementInterpolationTests
    {
        private const float Delay = 0.1f;

        private MovementInterpolationBuffer m_Buffer;

        [SetUp]
        public void SetUp()
        {
            m_Buffer = new MovementInterpolationBuffer();
        }

        /// <summary>空缓冲取不出样本，调用方据此跳过这一帧。</summary>
        [Test]
        public void 空缓冲取样失败()
        {
            var sampled = m_Buffer.TrySample(1d, Delay, out _);

            Assert.IsFalse(sampled);
        }

        /// <summary>两条快照之间按时间线性插值。</summary>
        [Test]
        public void 两条快照之间线性插值()
        {
            Push(0d, new Vector2F(0f, 0f));
            Push(1d, new Vector2F(10f, 0f));

            // 渲染时间 1.1 − 延迟 0.1 = 目标 1.0，恰好落在最新一条上。
            Assert.IsTrue(m_Buffer.TrySample(1.1d, Delay, out var atNewest));
            Assert.AreEqual(10f, atNewest.Position.X, 1e-4f);

            // 渲染时间 0.6 − 延迟 0.1 = 目标 0.5，应当取到一半。
            Assert.IsTrue(m_Buffer.TrySample(0.6d, Delay, out var middle));
            Assert.AreEqual(5f, middle.Position.X, 1e-4f);
        }

        /// <summary>目标时间早于最旧快照时取最旧的一条，而不是失败。</summary>
        [Test]
        public void 目标时间过早时钳制到最旧快照()
        {
            Push(10d, new Vector2F(1f, 1f));
            Push(11d, new Vector2F(2f, 2f));

            Assert.IsTrue(m_Buffer.TrySample(10.05d, Delay, out var sampled));
            Assert.AreEqual(1f, sampled.Position.X, 1e-4f);
        }

        /// <summary>目标时间晚于最新快照时取最新的一条（长时间没新快照也不消失）。</summary>
        [Test]
        public void 目标时间过晚时钳制到最新快照()
        {
            Push(0d, new Vector2F(1f, 1f));
            Push(0.05d, new Vector2F(2f, 2f));

            Assert.IsTrue(m_Buffer.TrySample(5d, Delay, out var sampled));
            Assert.AreEqual(2f, sampled.Position.X, 1e-4f);
        }

        /// <summary>时间戳必须严格递增，重复或乱序的快照被丢弃。</summary>
        [Test]
        public void 乱序或重复的快照被拒绝()
        {
            Push(1d, new Vector2F(1f, 0f));

            Assert.IsFalse(m_Buffer.Push(1d, State(new Vector2F(9f, 0f))), "同时间戳应被拒绝。");
            Assert.IsFalse(m_Buffer.Push(0.5d, State(new Vector2F(9f, 0f))), "回退时间戳应被拒绝。");
            Assert.AreEqual(1, m_Buffer.Count);
        }

        /// <summary>状态位取较早的一条：渲染时刻成立的状态才是画面该显示的状态。</summary>
        [Test]
        public void 状态位取较早的快照()
        {
            var older = State(new Vector2F(0f, 0f));
            older.IsSprinting = true;
            var newer = State(new Vector2F(10f, 0f));
            newer.IsSprinting = false;

            m_Buffer.Push(0d, older);
            m_Buffer.Push(1d, newer);

            Assert.IsTrue(m_Buffer.TrySample(0.6d, Delay, out var sampled));
            Assert.IsTrue(sampled.IsSprinting, "渲染时刻位于两条快照之间时应沿用较早的状态位。");
        }

        /// <summary>裁剪旧快照时必须留下插值所需的基线。</summary>
        [Test]
        public void 裁剪时至少保留指定条数()
        {
            for (var i = 0; i < 5; i++)
            {
                Push(i, new Vector2F(i, 0f));
            }

            var dropped = m_Buffer.TrimBefore(4d, keepAtLeast: 2);

            Assert.AreEqual(3, dropped);
            Assert.AreEqual(2, m_Buffer.Count);
            Assert.AreEqual(3d, m_Buffer.OldestTime, 1e-6f);
        }

        /// <summary>容量满时丢弃最旧的一条，最新数据永远保留。</summary>
        [Test]
        public void 容量满时丢弃最旧快照()
        {
            var buffer = new MovementInterpolationBuffer(3);
            for (var i = 0; i < 5; i++)
            {
                buffer.Push(i, State(new Vector2F(i, 0f)));
            }

            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(2d, buffer.OldestTime, 1e-6f);
            Assert.AreEqual(4d, buffer.NewestTime, 1e-6f);
        }

        /// <summary>清空之后不应再取到样本（角色离开视野时使用）。</summary>
        [Test]
        public void 清空后取样失败()
        {
            Push(0d, new Vector2F(1f, 0f));
            m_Buffer.Clear();

            Assert.IsFalse(m_Buffer.TrySample(1d, Delay, out _));
        }

        /// <summary>容量小于 2 时应拒绝构造——插值至少需要两条快照。</summary>
        [Test]
        public void 容量过小应拒绝构造()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MovementInterpolationBuffer(1));
        }

        private void Push(double time, Vector2F position)
        {
            var state = State(position);
            m_Buffer.Push(time, state);
        }

        private static PlayerMoveState State(Vector2F position)
        {
            return PlayerMoveState.CreateInitial(position, Vector2F.Right, 100f);
        }
    }
}
