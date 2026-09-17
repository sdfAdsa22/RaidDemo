using NUnit.Framework;
using RaidDemo.Simulation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 联机输入序号发号器的回归用例（U-98）。
    /// </summary>
    /// <remarks>
    /// 「进图几秒后闪回出生点」的根因是：换场景后新链路从 0 重新计数，
    /// 而服务器侧同一玩家的"已处理序号"已被旧链路顶高——新链路的前几百条输入
    /// 会被当重复包整片丢弃。这组用例把"跨场景仍单调递增"这条不变量钉死。
    /// </remarks>
    [TestFixture]
    public sealed class PlayerInputSequenceTests
    {
        [SetUp]
        public void SetUp()
        {
            // 静态状态会跨用例残留，显式归零，避免断言依赖执行顺序。
            PlayerInputSequence.ResetForTests();
        }

        /// <summary>从 1 开始，逐次递增。</summary>
        [Test]
        public void 从1开始且逐次递增()
        {
            Assert.AreEqual(1u, PlayerInputSequence.Next());
            Assert.AreEqual(2u, PlayerInputSequence.Next());
            Assert.AreEqual(3u, PlayerInputSequence.Next());
        }

        /// <summary>
        /// 模拟"安全屋链路跑到一半 → 换图 → 战局链路接管"：
        /// 新链路必须接着旧链路的号继续数，而不是从 1 重来。
        /// </summary>
        [Test]
        public void 换场景后新链路接着旧链路计数()
        {
            // 旧场景（安全屋链路）：已经发了 5 条输入。
            for (var i = 0; i < 5; i++)
            {
                PlayerInputSequence.Next();
            }

            // 新场景（战局链路）：下一条必须是 6。
            Assert.AreEqual(
                6u,
                PlayerInputSequence.Next(),
                "换场景后序号必须继续递增——重来会让服务器把新链路的前几百条输入当重复包丢弃（U-98）。");
        }
    }
}
