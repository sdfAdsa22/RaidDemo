using NUnit.Framework;
using RaidDemo.Shared;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 移动预测与和解的行为测试（M9 · P1）。
    /// </summary>
    /// <remarks>
    /// 这些用例覆盖联机手感最关键的一段逻辑：客户端先跑一遍、服务器随后裁定。
    /// 全部用纯逻辑构造（两个模拟器 + 一个缓冲），不需要网络、不需要场景。
    /// </remarks>
    [TestFixture]
    public sealed class MovementPredictionTests
    {
        private const float Step = 0.1f;

        private PlayerMovementProfile m_Profile;
        private PlayerMovementSimulator m_Server;
        private PlayerMovementSimulator m_Client;
        private MovementPredictionBuffer m_Buffer;

        [SetUp]
        public void SetUp()
        {
            m_Profile = new PlayerMovementProfile();
            m_Server = new PlayerMovementSimulator(m_Profile);
            m_Client = new PlayerMovementSimulator(m_Profile);
            m_Buffer = new MovementPredictionBuffer();
        }

        /// <summary>快照必须能精确往返，含体力恢复计时器这种内部状态。</summary>
        [Test]
        public void 快照往返后状态完全一致()
        {
            var simulator = new PlayerMovementSimulator(new PlayerMovementProfile());
            var intent = new PlayerMoveIntent(1, new Vector2F(0f, 1f), new Vector2F(0f, 1f), true, 1u);

            // 先跑几步让内部计时器处于非初始值。
            simulator.Step(intent.MoveDirection, intent.LookDirection, Step, true);
            simulator.Step(intent.MoveDirection, intent.LookDirection, Step, false);
            var before = simulator.CaptureSnapshot();

            var other = new PlayerMovementSimulator(new PlayerMovementProfile());
            other.RestoreSnapshot(before);
            var after = other.CaptureSnapshot();

            Assert.AreEqual(before.State.Position.X, after.State.Position.X, 1e-6f);
            Assert.AreEqual(before.State.Position.Y, after.State.Position.Y, 1e-6f);
            Assert.AreEqual(before.State.CurrentSpeed, after.State.CurrentSpeed, 1e-6f);
            Assert.AreEqual(before.State.Stamina, after.State.Stamina, 1e-6f);
            Assert.AreEqual(before.TimeSinceSprintEnd, after.TimeSinceSprintEnd, 1e-6f);
        }

        /// <summary>记录之后应能查到待确认条数与首尾序号。</summary>
        [Test]
        public void 记录后可查询待确认条数()
        {
            RecordStep(1u, new Vector2F(1f, 0f));
            RecordStep(2u, new Vector2F(1f, 0f));

            Assert.AreEqual(2, m_Buffer.PendingCount);
            Assert.AreEqual(1u, m_Buffer.OldestSequence);
            Assert.AreEqual(2u, m_Buffer.LastRecordedSequence);
        }

        /// <summary>序号不递增的记录必须被拒绝，否则历史无法按序重放。</summary>
        [Test]
        public void 序号不递增的记录被拒绝()
        {
            RecordStep(5u, new Vector2F(1f, 0f));
            RecordStep(3u, new Vector2F(1f, 0f));

            Assert.AreEqual(1, m_Buffer.PendingCount);
            Assert.AreEqual(5u, m_Buffer.LastRecordedSequence);
        }

        /// <summary>预测与权威一致时不应触发回滚，但历史里已确认的条目要被丢弃。</summary>
        [Test]
        public void 预测一致时不回滚并丢弃已确认条目()
        {
            RunBoth(1u, new Vector2F(1f, 0f));
            RecordStep(2u, new Vector2F(1f, 0f));

            var result = m_Buffer.Reconcile(1u, m_Server.CaptureSnapshot(), 0.001f);

            Assert.IsTrue(result.HasBaseline);
            Assert.IsFalse(result.NeedsRollback, $"误差 {result.PositionError:F4} 应小于容差。");
            Assert.AreEqual(1, m_Buffer.PendingCount);
            Assert.AreEqual(2u, m_Buffer.OldestSequence);
        }

        /// <summary>
        /// 预测与权威不一致时必须报告需要回滚。
        /// </summary>
        /// <remarks>
        /// 用「服务器侧被障碍完全挡住、客户端预测能自由通过」来构造分歧——
        /// 这正是联机里最常见的对不上：碰撞判定的细微差异、或客户端漏判了某个障碍。
        /// </remarks>
        [Test]
        public void 预测偏差超容差时要求回滚()
        {
            // 客户端预测向右走 0.35 米（3.5 m/s × 0.1 s）。
            RecordStep(1u, new Vector2F(1f, 0f));

            // 服务器处理同一输入时被障碍挡住，位置没动。
            var blockedServer = new PlayerMovementSimulator(
                m_Profile,
                collisionWorld: new BlockingCollisionWorld());
            blockedServer.Step(new Vector2F(1f, 0f), new Vector2F(1f, 0f), Step, false);

            var result = m_Buffer.Reconcile(1u, blockedServer.CaptureSnapshot(), 0.01f);

            Assert.IsTrue(result.HasBaseline);
            Assert.IsTrue(result.NeedsRollback);
            Assert.AreEqual(0.35f, result.PositionError, 1e-3f);
        }

        /// <summary>
        /// 回滚重放之后，客户端轨迹必须与「一直按服务器节奏跑」的结果一致。
        /// </summary>
        /// <remarks>
        /// 构造方式贴近真实分歧：客户端预测两步，服务器第一步被障碍挡住（客户端没料到）、
        /// 第二步畅通。对账发现分歧后应回滚到权威状态，并且只重放尚未确认的第二步。
        /// </remarks>
        [Test]
        public void 回滚重放后与权威轨迹一致()
        {
            var collision = new ToggleCollisionWorld();
            var server = new PlayerMovementSimulator(m_Profile, collisionWorld: collision);

            RecordStep(1u, new Vector2F(1f, 0f));
            RecordStep(2u, new Vector2F(0f, 1f));

            // 服务器处理第一步时被挡住：位置不动，但速度照样按输入推进。
            collision.Blocked = true;
            server.Step(new Vector2F(1f, 0f), new Vector2F(1f, 0f), Step, false);
            var authoritative = server.CaptureSnapshot();

            var result = m_Buffer.Reconcile(1u, authoritative, 0.01f);
            Assert.IsTrue(
                result.NeedsRollback,
                $"客户端预测与权威相差 {result.PositionError:F3} 米，应当要求回滚。");

            // 回滚重放：从权威状态出发，只重放第二条输入。
            var replayed = m_Buffer.Replay(m_Client, authoritative);
            Assert.AreEqual(1, replayed, "只有第二条输入需要重放。");

            // 期望值：服务器第二步畅通，按同一输入推进后的结果。
            collision.Blocked = false;
            server.Step(new Vector2F(0f, 1f), new Vector2F(0f, 1f), Step, false);
            var expected = server.CaptureSnapshot();

            Assert.AreEqual(expected.State.Position.X, m_Client.State.Position.X, 1e-4f);
            Assert.AreEqual(expected.State.Position.Y, m_Client.State.Position.Y, 1e-4f);
            Assert.AreEqual(expected.State.CurrentSpeed, m_Client.State.CurrentSpeed, 1e-4f);
        }

        /// <summary>服务器确认的序号比历史还旧时，不应丢弃历史（对账来得太晚）。</summary>
        [Test]
        public void 序号过旧时不丢弃历史()
        {
            RecordStep(10u, new Vector2F(1f, 0f));
            RecordStep(11u, new Vector2F(1f, 0f));

            var result = m_Buffer.Reconcile(3u, m_Server.CaptureSnapshot(), 0.01f);

            Assert.IsFalse(result.HasBaseline);
            Assert.AreEqual(2, m_Buffer.PendingCount);
        }

        /// <summary>服务器确认的序号超过全部记录时，历史已无价值，可以清空。</summary>
        [Test]
        public void 序号超过全部记录时清空历史()
        {
            RecordStep(1u, new Vector2F(1f, 0f));
            RecordStep(2u, new Vector2F(1f, 0f));

            var result = m_Buffer.Reconcile(99u, m_Server.CaptureSnapshot(), 0.01f);

            Assert.IsFalse(result.HasBaseline);
            Assert.AreEqual(0, m_Buffer.PendingCount);
        }

        /// <summary>容量满时覆盖最旧的一条，而不是丢弃新记录。</summary>
        [Test]
        public void 容量满时覆盖最旧记录()
        {
            var buffer = new MovementPredictionBuffer(3);
            var simulator = new PlayerMovementSimulator(new PlayerMovementProfile());

            for (uint sequence = 1u; sequence <= 5u; sequence++)
            {
                simulator.Step(new Vector2F(1f, 0f), new Vector2F(1f, 0f), Step, false);
                buffer.Record(
                    new PlayerMoveIntent(1, new Vector2F(1f, 0f), new Vector2F(1f, 0f), false, sequence),
                    Step,
                    simulator.CaptureSnapshot());
            }

            Assert.AreEqual(3, buffer.PendingCount);
            Assert.AreEqual(3u, buffer.OldestSequence);
            Assert.AreEqual(5u, buffer.LastRecordedSequence);
        }

        /// <summary>清空后一切归零，避免断线重连时重放上一局的输入。</summary>
        [Test]
        public void 清空后历史为空()
        {
            RecordStep(1u, new Vector2F(1f, 0f));

            m_Buffer.Clear();

            Assert.AreEqual(0, m_Buffer.PendingCount);
            Assert.AreEqual(0u, m_Buffer.LastRecordedSequence);
        }

        /// <summary>客户端走一步并记录；服务器只走不记录。</summary>
        private void RecordStep(uint sequence, Vector2F direction)
        {
            var intent = new PlayerMoveIntent(1, direction, direction, false, sequence);
            m_Client.Step(intent.MoveDirection, intent.LookDirection, Step, intent.WantsToSprint);
            m_Buffer.Record(intent, Step, m_Client.CaptureSnapshot());
        }

        /// <summary>两端各走一步且记录，用于构造"预测一致"的场景。</summary>
        private void RunBoth(uint sequence, Vector2F direction)
        {
            RecordStep(sequence, direction);
            m_Server.Step(direction, direction, Step, false);
        }

        /// <summary>
        /// 把所有位移都挡住的碰撞世界，用于制造「客户端预测通过、服务器被挡住」的分歧。
        /// </summary>
        private sealed class BlockingCollisionWorld : IMovementCollisionWorld
        {
            public bool TryResolveMove(Vector2F from, Vector2F delta, float radius, out Vector2F resolved)
            {
                resolved = Vector2F.Zero;
                return true;
            }
        }

        /// <summary>
        /// 可开关的碰撞世界，用于构造「服务器先被挡住、随后畅通」的时序。
        /// </summary>
        private sealed class ToggleCollisionWorld : IMovementCollisionWorld
        {
            /// <summary>为 true 时挡下全部位移。</summary>
            public bool Blocked;

            public bool TryResolveMove(Vector2F from, Vector2F delta, float radius, out Vector2F resolved)
            {
                resolved = Blocked ? Vector2F.Zero : delta;
                return Blocked;
            }
        }
    }
}
