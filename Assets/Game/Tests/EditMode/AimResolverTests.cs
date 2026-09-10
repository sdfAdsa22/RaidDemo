using NUnit.Framework;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 瞄准方向求解的测试。
    /// </summary>
    /// <remarks>
    /// 重点覆盖死区边界：这是"鼠标移到角色身上时角色乱转"问题的修复点，
    /// 属于纯几何计算，适合用单元测试而非手工在场景中验证。
    /// </remarks>
    [TestFixture]
    public sealed class AimResolverTests
    {
        private const float DeadZone = 0.5f;

        private static readonly Vector2F Origin = new Vector2F(10f, 20f);

        [Test]
        public void Resolve_ReturnsDirectionTowardAimPoint()
        {
            var aimPoint = Origin + new Vector2F(5f, 0f);

            var result = AimResolver.Resolve(aimPoint, Origin, DeadZone);

            Assert.AreEqual(1f, result.X, 1e-4f);
            Assert.AreEqual(0f, result.Y, 1e-4f);
        }

        [Test]
        public void Resolve_ReturnsUnitLengthVector()
        {
            var aimPoint = Origin + new Vector2F(30f, 40f);

            var result = AimResolver.Resolve(aimPoint, Origin, DeadZone);

            Assert.AreEqual(1f, result.Magnitude, 1e-4f, "返回的朝向必须是单位向量。");
        }

        [Test]
        public void Resolve_WithinDeadZone_ReturnsZero()
        {
            // 瞄准点落在死区内（距离 0.25，小于 0.5）。
            var aimPoint = Origin + new Vector2F(0.25f, 0f);

            var result = AimResolver.Resolve(aimPoint, Origin, DeadZone);

            Assert.IsTrue(result.IsNearlyZero, "死区内应返回零向量，表示保持上一次朝向。");
        }

        [Test]
        public void Resolve_ExactlyAtOrigin_ReturnsZero()
        {
            var result = AimResolver.Resolve(Origin, Origin, DeadZone);

            Assert.IsTrue(result.IsNearlyZero, "瞄准点与角色完全重合时必须返回零向量，否则方向未定义。");
        }

        /// <summary>
        /// 死区边界外侧应当给出有效方向。
        /// </summary>
        /// <remarks>
        /// 这条与上一条构成边界对：死区内为零，死区外必须有值。
        /// 若边界判断写反，两条会同时失败。
        /// </remarks>
        [Test]
        public void Resolve_JustOutsideDeadZone_ReturnsValidDirection()
        {
            var aimPoint = Origin + new Vector2F(0.6f, 0f);

            var result = AimResolver.Resolve(aimPoint, Origin, DeadZone);

            Assert.IsFalse(result.IsNearlyZero, "死区外应给出有效朝向。");
            Assert.AreEqual(1f, result.X, 1e-4f);
        }

        /// <summary>
        /// 死区内的方向必须是稳定的零向量，而不是随位置变化的噪声。
        /// </summary>
        /// <remarks>
        /// 这正是原始缺陷的表现：鼠标靠近角色时方向向量极短，
        /// 微小位移就让方向剧烈跳变。死区内必须始终返回零向量。
        /// </remarks>
        [Test]
        public void Resolve_StaysStableWhileMovingInsideDeadZone()
        {
            var samples = new[]
            {
                Origin + new Vector2F(0.01f, 0f),
                Origin + new Vector2F(0f, 0.2f),
                Origin + new Vector2F(-0.3f, 0.1f),
                Origin + new Vector2F(0.3f, -0.3f)
            };

            foreach (var sample in samples)
            {
                var result = AimResolver.Resolve(sample, Origin, DeadZone);
                Assert.IsTrue(
                    result.IsNearlyZero,
                    $"死区内的点 {sample} 必须返回零向量，否则角色会抖动。");
            }
        }

        /// <summary>
        /// 死区是圆形而不是方形。
        /// </summary>
        /// <remarks>
        /// 对角线方向上的点即使两个分量都小于死区半径，只要合距离超出半径就应视为死区外。
        /// 若误用分量比较实现判定，就会把死区变成一个正方形，
        /// 表现为斜向靠近角色时瞄准手感与其他方向不一致。
        /// </remarks>
        [Test]
        public void Resolve_DeadZoneIsCircular_NotSquare()
        {
            // 两个分量都小于 0.5，但合距离约 0.566，已在死区之外。
            var diagonalOutside = Origin + new Vector2F(0.4f, -0.4f);
            // 合距离约 0.424，仍在死区之内。
            var diagonalInside = Origin + new Vector2F(0.3f, -0.3f);

            Assert.IsFalse(
                AimResolver.Resolve(diagonalOutside, Origin, DeadZone).IsNearlyZero,
                "合距离超出死区半径的对角点应视为死区外。");

            Assert.IsTrue(
                AimResolver.Resolve(diagonalInside, Origin, DeadZone).IsNearlyZero,
                "合距离在死区半径内的对角点应视为死区内。");
        }

        [Test]
        public void Resolve_ZeroDeadZone_AlwaysReturnsDirection()
        {
            // 死区为 0 时，只有完全重合才返回零向量。
            var aimPoint = Origin + new Vector2F(0.001f, 0f);

            var result = AimResolver.Resolve(aimPoint, Origin, 0f);

            Assert.IsFalse(result.IsNearlyZero, "死区为 0 时，非重合点都应给出方向。");
        }

        [Test]
        public void Resolve_NegativeDeadZone_IsTreatedAsZero()
        {
            var aimPoint = Origin + new Vector2F(1f, 0f);

            var result = AimResolver.Resolve(aimPoint, Origin, -5f);

            Assert.IsFalse(result.IsNearlyZero, "负数死区应按 0 处理，而不是产生异常结果。");
            Assert.AreEqual(1f, result.X, 1e-4f);
        }
    }
}
