using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// AI 移动执行器测试：直线推进、沿路径行走、到达判定、边界夹取与重新寻路节流。
    /// </summary>
    /// <remarks>
    /// 这些用例全部使用假的寻路实现，因此断言的是"移动执行器如何消费路径"，
    /// 与导航网格烘焙结果无关。真导航网格的行为由运行时的实机验证覆盖。
    /// </remarks>
    [TestFixture]
    public sealed class AiMovementTests
    {
        private ScriptedPathfindingService m_Pathfinding;
        private AIPerceptionProfile m_Profile;
        private AiMovement m_Movement;

        [SetUp]
        public void SetUp()
        {
            m_Pathfinding = new ScriptedPathfindingService();
            m_Profile = new AIPerceptionProfile
            {
                RepathIntervalSeconds = 0.6f,
                WaypointReachDistance = 0.5f,
            };

            m_Movement = new AiMovement(m_Pathfinding, m_Profile);
        }

        [Test]
        public void Step_WithoutPath_MovesStraightTowardGoal()
        {
            m_Pathfinding.Succeeds = false;

            var result = m_Movement.Step(
                from: Vector2F.Zero,
                goal: new Vector2F(10f, 0f),
                speed: 2f,
                deltaTime: 0.5f);

            Assert.IsTrue(result.Moved);
            Assert.AreEqual(1f, result.Position.X, 1e-4f);
            Assert.AreEqual(0f, result.Position.Y, 1e-4f);
            Assert.IsFalse(result.UsedPath, "寻路失败时应当退化为直线推进。");
        }

        [Test]
        public void Step_FollowsPath_InsteadOfHeadingStraightToGoal()
        {
            // 目标在正右方，但路径要求先向上绕开掩体。
            m_Pathfinding.SetWaypoints(new Vector2F(0f, 5f), new Vector2F(10f, 5f));

            var result = m_Movement.Step(
                from: Vector2F.Zero,
                goal: new Vector2F(10f, 0f),
                speed: 2f,
                deltaTime: 0.5f);

            Assert.IsTrue(result.UsedPath, "有路径时应当沿路径行走。");
            Assert.AreEqual(0f, result.Position.X, 1e-4f, "第一步应当朝路径点走，而不是朝终点走。");
            Assert.AreEqual(1f, result.Position.Y, 1e-4f);
        }

        [Test]
        public void Step_ArrivingExactlyAtGoal_ReportsReached()
        {
            m_Pathfinding.Succeeds = false;

            var result = m_Movement.Step(
                from: Vector2F.Zero,
                goal: new Vector2F(1f, 0f),
                speed: 5f,
                deltaTime: 0.5f);

            Assert.AreEqual(1f, result.Position.X, 1e-4f);
            Assert.IsTrue(result.ReachedGoal, "这一步刚好走到终点时应当直接报告到达。");
        }

        [Test]
        public void Step_ClampsPositionInsideBounds()
        {
            m_Pathfinding.Succeeds = false;
            m_Movement.Bounds = new PlayAreaBounds(new Vector2F(-5f, -5f), new Vector2F(5f, 5f));

            var result = m_Movement.Step(
                from: new Vector2F(4f, 0f),
                goal: new Vector2F(20f, 0f),
                speed: 10f,
                deltaTime: 1f);

            Assert.AreEqual(5f, result.Position.X, 1e-4f, "位置必须被夹在活动范围内。");
        }

        [Test]
        public void Step_RepathsOnInterval_NotEveryFrame()
        {
            m_Pathfinding.SetWaypoints(new Vector2F(0f, 1f));

            var position = Vector2F.Zero;
            for (var i = 0; i < 10; i++)
            {
                // 目标是恒定的，因此只有重新寻路计时器到期时才会再次询问寻路服务。
                position = m_Movement.Step(position, new Vector2F(50f, 0f), speed: 1f, deltaTime: 0.1f).Position;
            }

            Assert.LessOrEqual(
                m_Pathfinding.CallCount,
                3,
                "一秒内不应当每帧都寻路：目标未变化时应当按间隔节流，否则大地图上会出现明显开销与路径抖动。");
            Assert.GreaterOrEqual(m_Pathfinding.CallCount, 1, "至少应当寻路一次。");
        }

        [Test]
        public void ResetPath_ForcesRepathNextStep()
        {
            m_Pathfinding.SetWaypoints(new Vector2F(0f, 1f));
            m_Movement.Step(Vector2F.Zero, new Vector2F(50f, 0f), 1f, 0.1f);

            var before = m_Pathfinding.CallCount;
            m_Movement.ResetPath();
            m_Movement.Step(Vector2F.Zero, new Vector2F(50f, 0f), 1f, 0.1f);

            Assert.AreEqual(before + 1, m_Pathfinding.CallCount, "丢弃路径之后应当立刻重新寻路。");
        }
    }
}
