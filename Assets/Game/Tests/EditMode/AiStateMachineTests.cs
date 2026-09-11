using NUnit.Framework;
using RaidDemo.AI;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 通用状态机测试：迁移、重入、未注册状态、计时与理由。
    /// </summary>
    /// <remarks>
    /// <para>状态机是整个 AI 的地基。它出问题时的症状是"AI 卡住"或"AI 抽搐"，
    /// 这两种现象在场景里都极难反推到状态机本身，因此必须在这里用最小用例钉死。</para>
    ///
    /// <para>用 <c>int</c> 作为上下文类型：这组用例验证的是状态机本身，
    /// 与 AI 的上下文无关，换成真实上下文只会引入无关的依赖。</para>
    /// </remarks>
    [TestFixture]
    public sealed class AiStateMachineTests
    {
        /// <summary>可编排的测试状态。</summary>
        private sealed class StubState : IState<int>
        {
            public StubState(AiStateId id)
            {
                Id = id;
            }

            public AiStateId Id { get; }

            public int EnterCount { get; private set; }

            public int ExitCount { get; private set; }

            public int TickCount { get; private set; }

            /// <summary>本状态在 Tick 时返回的迁移请求。</summary>
            public AiTransition Next { get; set; } = AiTransition.None;

            public void Enter(int context)
            {
                EnterCount++;
            }

            public void Exit(int context)
            {
                ExitCount++;
            }

            public AiTransition Tick(int context, float deltaTime)
            {
                TickCount++;
                return Next;
            }
        }

        private StubState m_Patrol;
        private StubState m_Engage;
        private StateMachine<int> m_Machine;

        [SetUp]
        public void SetUp()
        {
            m_Patrol = new StubState(AiStateId.Patrol);
            m_Engage = new StubState(AiStateId.Engage);

            m_Machine = new StateMachine<int>(
                context: 0,
                states: new IState<int>[] { m_Patrol, m_Engage });
        }

        [Test]
        public void Start_EntersInitialState()
        {
            m_Machine.Start(AiStateId.Patrol);

            Assert.IsTrue(m_Machine.HasStarted, "启动后状态机应当处于运行状态。");
            Assert.AreEqual(AiStateId.Patrol, m_Machine.CurrentId);
            Assert.AreEqual(1, m_Patrol.EnterCount, "初始状态应当被进入一次。");
        }

        [Test]
        public void Tick_TicksCurrentStateOnly()
        {
            m_Machine.Start(AiStateId.Patrol);
            m_Machine.Tick(0.1f);
            m_Machine.Tick(0.1f);

            Assert.AreEqual(2, m_Patrol.TickCount, "当前状态每帧应当被推进一次。");
            Assert.AreEqual(0, m_Engage.TickCount, "未激活的状态不应当被推进。");
        }

        [Test]
        public void Tick_AppliesTransition_AndCallsExitThenEnter()
        {
            m_Machine.Start(AiStateId.Patrol);
            m_Patrol.Next = new AiTransition(AiStateId.Engage, "看到目标");

            m_Machine.Tick(0.1f);

            Assert.AreEqual(AiStateId.Engage, m_Machine.CurrentId);
            Assert.AreEqual(1, m_Patrol.ExitCount, "离开旧状态时应当调用 Exit。");
            Assert.AreEqual(1, m_Engage.EnterCount, "进入新状态时应当调用 Enter。");
            Assert.AreEqual("看到目标", m_Machine.LastTransitionReason);
            Assert.AreEqual(1, m_Machine.TransitionCount);
        }

        [Test]
        public void Tick_IgnoresSelfTransition()
        {
            m_Machine.Start(AiStateId.Patrol);
            m_Patrol.Next = new AiTransition(AiStateId.Patrol, "误写为自身");

            m_Machine.Tick(0.1f);

            Assert.AreEqual(1, m_Patrol.EnterCount, "返回自身状态不应当重新进入，否则计时器会被反复清零。");
            Assert.AreEqual(0, m_Patrol.ExitCount);
            Assert.AreEqual(0, m_Machine.TransitionCount, "自迁移不应当计入迁移次数。");
        }

        [Test]
        public void Tick_UnregisteredTarget_KeepsCurrentState()
        {
            m_Machine.Start(AiStateId.Patrol);
            m_Patrol.Next = new AiTransition(AiStateId.Retreat, "撤退（该状态未注册）");

            m_Machine.Tick(0.1f);

            Assert.AreEqual(AiStateId.Patrol, m_Machine.CurrentId, "目标状态未注册时应当保持原状态而不是崩溃。");
            StringAssert.Contains("未注册", m_Machine.LastTransitionReason);
        }

        [Test]
        public void TimeInState_ResetsOnTransition()
        {
            m_Machine.Start(AiStateId.Patrol);
            m_Machine.Tick(0.5f);
            m_Machine.Tick(0.5f);

            Assert.AreEqual(1f, m_Machine.TimeInState, 1e-4f, "停留时间应当累加。");

            m_Patrol.Next = new AiTransition(AiStateId.Engage, "看到目标");
            m_Machine.Tick(0.5f);

            Assert.AreEqual(0f, m_Machine.TimeInState, 1e-4f, "迁移之后停留时间应当清零。");
        }

        [Test]
        public void TryChange_FromOutside_UsesSamePathAndCallback()
        {
            var reported = AiStateId.Patrol;
            var reason = string.Empty;
            var machine = new StateMachine<int>(
                context: 0,
                states: new IState<int>[] { m_Patrol, m_Engage },
                onStateChanged: (state, why) =>
                {
                    reported = state;
                    reason = why;
                });

            machine.Start(AiStateId.Patrol);
            var changed = machine.TryChange(AiStateId.Engage, "被打断");

            Assert.IsTrue(changed);
            Assert.AreEqual(AiStateId.Engage, reported, "外部打断也应当触发状态变化回调。");
            Assert.AreEqual("被打断", reason);

            Assert.IsFalse(
                machine.TryChange(AiStateId.Engage, "重复请求"),
                "已经是目标状态时应当返回 false，不产生多余迁移。");
        }
    }
}
