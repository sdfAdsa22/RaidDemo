using NUnit.Framework;
using RaidDemo.UI;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 「本帧 Esc 已被界面消费」标记的行为测试。
    /// </summary>
    /// <remarks>
    /// <para>它保护的是一条很容易被"顺手改掉"的规则：界面按 Esc 关掉自己之后，
    /// 同一帧里装配层不能再把暂停菜单弹出来。这个缺陷曾经真实存在
    /// （负责人反馈：从操作说明返回后出现暂停菜单），而且它**只在脚本执行顺序恰好合适时复现**，
    /// 靠人工点是抓不稳的，所以把它钉成测试。</para>
    /// </remarks>
    [TestFixture]
    public sealed class UiEscapeGuardTests
    {
        [SetUp]
        public void SetUp()
        {
            UiEscapeGuard.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            UiEscapeGuard.ResetForTests();
        }

        /// <summary>没人消费时，装配层应当照常处理 Esc（打开暂停菜单）。</summary>
        [Test]
        public void 未消费时标记为假()
        {
            Assert.IsFalse(UiEscapeGuard.WasConsumedThisFrame);
        }

        /// <summary>界面消费之后，同帧内装配层必须让路。</summary>
        [Test]
        public void 消费后本帧标记为真()
        {
            UiEscapeGuard.Consume();

            Assert.IsTrue(UiEscapeGuard.WasConsumedThisFrame);
        }

        /// <summary>重置（下一帧语义）之后回到未消费状态。</summary>
        [Test]
        public void 重置后回到未消费状态()
        {
            UiEscapeGuard.Consume();
            UiEscapeGuard.ResetForTests();

            Assert.IsFalse(UiEscapeGuard.WasConsumedThisFrame);
        }
    }
}
