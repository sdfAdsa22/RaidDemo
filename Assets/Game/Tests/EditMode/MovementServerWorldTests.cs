using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Shared;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器权威移动世界的行为测试（M9 · P1）。
    /// </summary>
    /// <remarks>
    /// 这些用例把「服务器怎么消费输入、怎么推进、什么时候发快照」钉死。
    /// 它们是客户端预测能够成立的前提：客户端复现的正是这里的行为。
    /// </remarks>
    [TestFixture]
    public sealed class MovementServerWorldTests
    {
        private const int PlayerId = 1;
        private const float Step = MovementServerWorld.FixedStepSeconds;

        private MovementServerWorld m_World;

        [SetUp]
        public void SetUp()
        {
            m_World = new MovementServerWorld();
        }

        /// <summary>加入玩家后可查询到，重复加入被拒绝。</summary>
        [Test]
        public void 加入与重复加入()
        {
            Assert.IsTrue(m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out var error), error);
            Assert.IsTrue(m_World.ContainsPlayer(PlayerId));
            Assert.AreEqual(1, m_World.PlayerCount);

            Assert.IsFalse(m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out var duplicateError));
            StringAssert.Contains("已在世界内", duplicateError);
        }

        /// <summary>不在世界里的玩家发来的输入必须被丢弃，而不是凭空建号。</summary>
        [Test]
        public void 未加入玩家的输入被拒绝()
        {
            var accepted = m_World.TrySubmitInput(Input(99, 1u, new Vector2F(1f, 0f)), out var rejection);

            Assert.IsFalse(accepted);
            StringAssert.Contains("不在世界内", rejection);
        }

        /// <summary>重复序号与回退序号都要拒绝——否则一次重传就能让角色倒退。</summary>
        [Test]
        public void 重复与乱序输入被拒绝()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);
            Assert.IsTrue(m_World.TrySubmitInput(Input(PlayerId, 5u, new Vector2F(1f, 0f)), out var ok), ok);

            Assert.IsFalse(
                m_World.TrySubmitInput(Input(PlayerId, 5u, new Vector2F(1f, 0f)), out var duplicate),
                "同序号的重复包应被拒绝。");
            StringAssert.Contains("重复或乱序", duplicate);

            // 更新的输入照常入队（与旧输入并列排队，而不是把旧输入顶掉——
            // 顶掉等于少执行一步，会让客户端位置稳定领先服务器一步）。
            Assert.IsTrue(m_World.TrySubmitInput(Input(PlayerId, 7u, new Vector2F(0f, 1f)), out var newer), newer);

            Assert.IsFalse(
                m_World.TrySubmitInput(Input(PlayerId, 6u, new Vector2F(0f, 1f)), out var stale),
                "晚于已处理、早于待处理的包应被拒绝。");
            StringAssert.Contains("重复或乱序", stale);
        }

        /// <summary>
        /// 一帧内补齐多步（低帧率客户端）时，连续提交的多条输入必须**逐条按序消费**，一条都不能丢。
        /// </summary>
        /// <remarks>
        /// <para><b>它防的是哪一类缺陷：</b>P-45。旧实现每个玩家只有一个待处理槽位，
        /// 更新的输入直接覆盖旧的：客户端一帧补 3 步就连发 3 条，服务器只留最新那条，
        /// 于是"已处理序号"与"实际执行步数"错开一步——客户端位置稳定领先服务器一步，
        /// 超过对账容差后每次快照都硬吸附一次，玩家看到的就是"移动一卡一卡、时不时瞬移"。</para>
        ///
        /// <para>断言分三层：序号逐条推进、丢弃计数为 0、位移等于"三条各一步"的距离。
        /// 只查序号会漏掉"虽然没有跳号、但少走了一步"这种情况。</para>
        /// </remarks>
        [Test]
        public void 一帧内补多步时输入逐条消费不丢步()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);

            // 模拟"客户端一帧补了三步"：三条输入在服务器推进之前到达。
            for (var sequence = 1u; sequence <= 3u; sequence++)
            {
                Assert.IsTrue(
                    m_World.TrySubmitInput(Input(PlayerId, sequence, new Vector2F(1f, 0f)), out var rejection),
                    $"第 {sequence} 条输入应当被接受：{rejection}");
            }

            // 每个固定步消费一条：第一条之后序号就是 1，而不是直接跳到 3。
            m_World.Advance(Step);
            m_World.TryGetSnapshot(PlayerId, out var afterFirstStep);
            Assert.AreEqual(1u, afterFirstStep.LastProcessedSequence, "一个固定步只应消费一条输入。");

            m_World.Advance(Step * 2f);
            m_World.TryGetSnapshot(PlayerId, out var afterThirdStep);
            Assert.AreEqual(3u, afterThirdStep.LastProcessedSequence, "三条输入应当逐条被消费完。");

            Assert.IsTrue(m_World.TryGetInputDiagnostics(PlayerId, out var dropped, out var pending));
            Assert.AreEqual(0, dropped, "正常到达的输入一条都不该丢。");
            Assert.AreEqual(0, pending, "三条输入都已被消费，队列里不该还有积压。");

            // 三步走路的总位移：3 × 3.5 m/s × (1/60 s) ≈ 0.175 米。
            var expected = 3f * 3.5f * Step;
            Assert.AreEqual(expected, afterThirdStep.State.Position.X, 0.005f,
                "少执行一步会让位移明显偏小，这一步偏差就是对账时被硬吸附的来源。");
        }

        /// <summary>推进按固定步长进行，仿真时间随之增长。</summary>
        [Test]
        public void 推进按固定步长进行()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);

            var steps = m_World.Advance(Step);

            Assert.AreEqual(1, steps);
            Assert.AreEqual(Step, (float)m_World.SimulationTime, 1e-6f);
        }

        /// <summary>单次推进有步数上限，避免卡顿之后追帧雪崩。</summary>
        [Test]
        public void 单次推进步数有上限()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);

            var steps = m_World.Advance(10f);

            Assert.LessOrEqual(steps, 8, "单次推进不应超过上限。");
            Assert.Greater(steps, 0);
        }

        /// <summary>提交输入之后，玩家会按输入移动，且已处理序号会推进。</summary>
        [Test]
        public void 输入生效并更新已处理序号()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);
            m_World.TrySubmitInput(Input(PlayerId, 3u, new Vector2F(1f, 0f)), out _);

            for (var i = 0; i < 10; i++)
            {
                m_World.Advance(Step);
            }

            Assert.IsTrue(m_World.TryGetSnapshot(PlayerId, out var snapshot));
            Assert.Greater(snapshot.State.Position.X, 0f, "向右输入应产生向右的位移。");
            Assert.AreEqual(0f, snapshot.State.Position.Y, 1e-4f);
            Assert.AreEqual(3u, snapshot.LastProcessedSequence);
        }

        /// <summary>没有新输入时沿用上一条输入，角色不会凭空停住。</summary>
        [Test]
        public void 无新输入时沿用上一条输入()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);
            m_World.TrySubmitInput(Input(PlayerId, 1u, new Vector2F(1f, 0f)), out _);

            m_World.Advance(Step);
            m_World.TryGetSnapshot(PlayerId, out var afterFirst);

            for (var i = 0; i < 5; i++)
            {
                m_World.Advance(Step);
            }

            m_World.TryGetSnapshot(PlayerId, out var afterMore);

            Assert.Greater(afterMore.State.Position.X, afterFirst.State.Position.X, "应当继续沿上一条输入移动。");
            Assert.AreEqual(1u, afterMore.LastProcessedSequence, "沿用旧输入不应改变已处理序号。");
        }

        /// <summary>快照按 20 Hz 节拍产出，取走之后同一批不会重复下发。</summary>
        [Test]
        public void 快照按节拍产出且只取一次()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);
            var snapshots = new List<PlayerSnapshot>();

            // 10 ms：未到 50 ms 的节拍。
            m_World.Advance(0.01f);
            Assert.AreEqual(0, m_World.CaptureSnapshots(snapshots));

            // 再推进 60 ms：累计超过节拍。
            m_World.Advance(0.06f);
            Assert.AreEqual(1, m_World.CaptureSnapshots(snapshots));
            Assert.AreEqual(PlayerId, snapshots[0].PlayerId);
            Assert.AreEqual(0, m_World.CaptureSnapshots(snapshots), "同一批快照不应被重复取走。");
        }

        /// <summary>快照里带服务器时间，远端插值靠它排序。</summary>
        [Test]
        public void 快照携带服务器时间()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);
            for (var i = 0; i < 6; i++)
            {
                m_World.Advance(Step);
            }

            var snapshots = new List<PlayerSnapshot>();
            Assert.Greater(m_World.CaptureSnapshots(snapshots), 0);
            Assert.Greater(snapshots[0].ServerTime, 0d);
            Assert.AreEqual((float)m_World.SimulationTime, (float)snapshots[0].ServerTime, 1e-6f);
        }

        /// <summary>移除玩家之后不再有它的快照。</summary>
        [Test]
        public void 移除玩家后不再产出其快照()
        {
            m_World.TryAddPlayer(PlayerId, Vector2F.Zero, Vector2F.Right, out _);
            Assert.IsTrue(m_World.RemovePlayer(PlayerId));
            Assert.IsFalse(m_World.ContainsPlayer(PlayerId));
            Assert.IsFalse(m_World.RemovePlayer(PlayerId), "重复移除应返回 false。");

            m_World.Advance(0.06f);
            var snapshots = new List<PlayerSnapshot>();

            Assert.AreEqual(0, m_World.CaptureSnapshots(snapshots));
        }

        /// <summary>多人同局时，每个人的输入只影响自己。</summary>
        [Test]
        public void 多人输入互不影响()
        {
            m_World.TryAddPlayer(1, Vector2F.Zero, Vector2F.Right, out _);
            m_World.TryAddPlayer(2, Vector2F.Zero, Vector2F.Right, out _);

            m_World.TrySubmitInput(Input(1, 1u, new Vector2F(1f, 0f)), out _);
            m_World.TrySubmitInput(Input(2, 1u, new Vector2F(0f, 1f)), out _);

            for (var i = 0; i < 10; i++)
            {
                m_World.Advance(Step);
            }

            m_World.TryGetSnapshot(1, out var first);
            m_World.TryGetSnapshot(2, out var second);

            Assert.Greater(first.State.Position.X, 0f);
            Assert.AreEqual(0f, first.State.Position.Y, 1e-4f);
            Assert.Greater(second.State.Position.Y, 0f);
            Assert.AreEqual(0f, second.State.Position.X, 1e-4f);
        }

        private static PlayerMoveIntent Input(int playerId, uint sequence, Vector2F direction)
        {
            return new PlayerMoveIntent(playerId, direction, direction, false, sequence);
        }
    }
}
