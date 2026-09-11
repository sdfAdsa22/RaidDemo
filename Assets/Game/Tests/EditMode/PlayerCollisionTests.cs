using NUnit.Framework;
using RaidDemo.Shared;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 移动碰撞测试：碰撞修正发生在模拟层，但引擎实现属于表现层，
    /// 因此这里用一个内存里的假碰撞世界来验证规则。
    /// </summary>
    /// <remarks>
    /// <para>假世界是一道沿 X 轴无限延伸的墙：圆心不允许低于半径，
    /// 也就是「角色不能走进 y 小于半径的区域」。</para>
    ///
    /// <para>不依赖 UnityEngine 物理，因此这些用例是纯数学断言，跑得极快。</para>
    /// </remarks>
    [TestFixture]
    public sealed class PlayerCollisionTests
    {
        /// <summary>一道沿 X 轴、位于 y 等于 0 的无限长墙。</summary>
        private sealed class TestWallCollisionWorld : IMovementCollisionWorld
        {
            /// <inheritdoc />
            public bool TryResolveMove(Vector2F from, Vector2F delta, float radius, out Vector2F resolved)
            {
                resolved = delta;

                var limit = radius;
                if (from.Y + delta.Y >= limit)
                {
                    return false;
                }

                var allowedY = limit - from.Y;
                if (allowedY < 0f)
                {
                    allowedY = 0f;
                }

                resolved = new Vector2F(delta.X, allowedY);
                return true;
            }
        }

        private static PlayerMovementSimulator CreateSimulator(
            IMovementCollisionWorld collisionWorld,
            Vector2F start)
        {
            var profile = new PlayerMovementProfile();
            var facing = new Vector2F(0f, -1f);
            return new PlayerMovementSimulator(profile, start, facing, collisionWorld, 0.4f);
        }

        private static void StepTimes(PlayerMovementSimulator simulator, Vector2F move, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                simulator.Step(move, move, 0.05f, wantsToSprint: false);
            }
        }

        [Test]
        public void 正面撞墙时不会穿墙()
        {
            var simulator = CreateSimulator(new TestWallCollisionWorld(), new Vector2F(0f, 2f));
            StepTimes(simulator, new Vector2F(0f, -1f), 60);

            Assert.GreaterOrEqual(
                simulator.State.Position.Y,
                0.39f,
                "角色不应穿过 y 等于 0 的墙");
            Assert.AreEqual(0f, simulator.State.Position.X, 0.001f, "正面撞墙不该产生横向位移");
        }

        [Test]
        public void 斜向撞墙时保留切向分量()
        {
            var simulator = CreateSimulator(new TestWallCollisionWorld(), new Vector2F(0f, 2f));
            StepTimes(simulator, new Vector2F(-1f, -1f), 60);

            Assert.GreaterOrEqual(simulator.State.Position.Y, 0.39f, "不应穿墙");
            Assert.Less(
                simulator.State.Position.X,
                -0.5f,
                "贴墙时横向分量必须保留，否则角色会被墙粘住");
        }

        [Test]
        public void 没有碰撞世界时移动不受限制()
        {
            var simulator = CreateSimulator(null, new Vector2F(0f, 2f));
            StepTimes(simulator, new Vector2F(0f, -1f), 60);

            Assert.Less(
                simulator.State.Position.Y,
                -1f,
                "未注入碰撞世界时应保持旧的纯数学推进行为");
            Assert.IsNull(simulator.CollisionWorld);
            Assert.AreEqual(0.4f, simulator.BodyRadius, 0.001f);
        }
    }
}
