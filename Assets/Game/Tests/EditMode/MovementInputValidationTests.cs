using NUnit.Framework;
using RaidDemo.Bootstrap;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 上行移动输入的合法性判定（`RD-AUD-047`）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么值得单独测：</b>NaN 一旦进入权威仿真，位置会被污染并通过快照广播给所有人，
    /// 而现象是"某个玩家在瞬移"——从外部完全看不出是输入校验的问题。
    /// 判定本身是四个分量的有限性检查，放成纯函数就能在 1 毫秒内钉住。</para>
    /// </remarks>
    [TestFixture]
    public sealed class MovementInputValidationTests
    {
        /// <summary>正常输入（含全零）应当被接受。</summary>
        [Test]
        public void 正常方向_判定为合法()
        {
            var message = new PlayerInputMessage
            {
                Move = new Vector2(0.5f, -0.5f),
                Look = new Vector2(1f, 0f),
            };

            Assert.IsTrue(message.HasFiniteDirections());

            message.Move = Vector2.zero;
            message.Look = Vector2.zero;
            Assert.IsTrue(message.HasFiniteDirections(), "零向量表示'保持原状'，是合法输入。");
        }

        /// <summary>NaN 与 ±Infinity 都要被拒绝。</summary>
        [Test]
        public void 非有限分量_判定为非法()
        {
            var nan = new PlayerInputMessage { Move = new Vector2(float.NaN, 0f), Look = Vector2.right };
            Assert.IsFalse(nan.HasFiniteDirections(), "移动分量是 NaN 时必须拒绝。");

            var nanLook = new PlayerInputMessage { Move = Vector2.right, Look = new Vector2(0f, float.NaN) };
            Assert.IsFalse(nanLook.HasFiniteDirections(), "朝向分量是 NaN 时必须拒绝。");

            var infinity = new PlayerInputMessage { Move = Vector2.right, Look = new Vector2(float.PositiveInfinity, 0f) };
            Assert.IsFalse(infinity.HasFiniteDirections(), "Infinity 同样会污染仿真。");

            var negativeInfinity = new PlayerInputMessage { Move = new Vector2(0f, float.NegativeInfinity), Look = Vector2.right };
            Assert.IsFalse(negativeInfinity.HasFiniteDirections());
        }
    }
}
