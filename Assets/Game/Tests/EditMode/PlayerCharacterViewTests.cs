using NUnit.Framework;
using RaidDemo.Presentation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 玩家角色视图的规则测试。
    /// </summary>
    /// <remarks>
    /// 用静态规则方法而不是直接测 MonoBehaviour：动画播放本身没法断言，
    /// 但"该不该播"可以——而这里正好出过一次运算符优先级写错导致的缺陷（A-03）。
    /// </remarks>
    [TestFixture]
    public sealed class PlayerCharacterViewTests
    {
        [Test]
        public void 本地玩家开火时播放开火动画()
        {
            Assert.IsTrue(PlayerCharacterView.ShouldPlayShootAnimation(
                hasAnimator: true,
                isDead: false,
                isLocalPlayer: true));
        }

        [Test]
        public void 他人开火不触发本地玩家的开火动画()
        {
            Assert.IsFalse(PlayerCharacterView.ShouldPlayShootAnimation(
                hasAnimator: true,
                isDead: false,
                isLocalPlayer: false));
        }

        [Test]
        public void 阵亡后开火不再打断倒地动画()
        {
            // 这是 A-03 的回归测试：原实现的布尔表达式把"已阵亡"与"是否本地玩家"
            // 用 && 连在一起，导致本地玩家阵亡后条件为假，倒地动画被自己的枪声打断。
            Assert.IsFalse(PlayerCharacterView.ShouldPlayShootAnimation(
                hasAnimator: true,
                isDead: true,
                isLocalPlayer: true),
                "阵亡后不该再触发开火动画——尸体会站起来开枪。");
        }

        [Test]
        public void 没有动画控制器时不报错也不播放()
        {
            Assert.IsFalse(PlayerCharacterView.ShouldPlayShootAnimation(
                hasAnimator: false,
                isDead: false,
                isLocalPlayer: true));
        }
    }
}
