using System.Collections;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using UnityEngine;
using UnityEngine.TestTools;

namespace RaidDemo.Tests.PlayMode
{
    /// <summary>
    /// AR-06 的回归用例：主动断开后的联机会话不能再被当成"联机进程"。
    /// </summary>
    /// <remarks>
    /// 必须放 PlayMode：会话的 <c>Awake</c> 才会执行（编辑模式不触发生命周期回调），
    /// 而本用例验证的正是"断开 → 静态活动标记失效 → 重新取会话得到新实例"这条链。
    /// 这条链断了的表现就是：联机里返回主界面后重载安全屋，主菜单不出现（负责人反馈的问题 9）。
    /// </remarks>
    [TestFixture]
    public sealed class MultiplayerSessionLifecyclePlayModeTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var current = MultiplayerClientSession.Current;
            if (current != null)
            {
                Object.Destroy(current.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator 主动断开后不再算活动会话且Ensure会重建()
        {
            var session = MultiplayerClientSession.Ensure();
            yield return null;

            Assert.IsTrue(MultiplayerClientSession.IsActive, "新会话应当是活动会话。");

            session.Disconnect();
            yield return null;

            Assert.IsTrue(session.IsDisconnected, "主动断开必须进入终态。");
            Assert.IsFalse(
                MultiplayerClientSession.IsActive,
                "主动断开后 IsActive 必须为 false —— 否则安全屋会按联机进程装配，主菜单不出现（AR-06）。");

            var next = MultiplayerClientSession.Ensure();
            yield return null;

            Assert.AreNotSame(session, next, "断开后的旧会话不应被 Ensure 复用。");
            Assert.IsTrue(MultiplayerClientSession.IsActive, "重建后的会话应当重新成为活动会话。");
        }
    }
}
