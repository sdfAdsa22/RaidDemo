using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 光标锁定策略测试：把"什么时候该隐藏鼠标"从界面实现里拆出来锁死。
    /// </summary>
    /// <remarks>
    /// 这组用例对应一次真实缺陷：主菜单与结算界面被当成战局操作状态，
    /// 每次点击后光标都会被重新锁定，菜单按钮因此无法再次点击。
    /// </remarks>
    [TestFixture]
    public sealed class CursorLockPolicyTests
    {
        [Test]
        public void 安全屋没有界面时锁定光标()
        {
            Assert.IsTrue(CursorLockPolicy.ShouldLockCursor(
                uiOpen: false,
                stateNeedsMouse: false));
        }

        [Test]
        public void 打开背包或商人界面时不锁光标()
        {
            Assert.IsFalse(CursorLockPolicy.ShouldLockCursor(
                uiOpen: true,
                stateNeedsMouse: false));
        }

        [Test]
        public void 主菜单与结算状态永远不锁光标()
        {
            Assert.IsFalse(CursorLockPolicy.ShouldLockCursor(
                uiOpen: false,
                stateNeedsMouse: true));
            Assert.IsFalse(CursorLockPolicy.ShouldLockCursor(
                uiOpen: true,
                stateNeedsMouse: true));
        }
    }
}
