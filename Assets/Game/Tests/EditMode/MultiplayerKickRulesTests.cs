using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// "被管理员移出房间"的判定与一次性提示测试（U-101 管理面板的客户端半边）。
    /// </summary>
    /// <remarks>
    /// 判定本身只有三个输入，但它决定"玩家会不会被错误地甩回主菜单"，
    /// 因此把每种组合都钉住；提示槽则要保证"取出即清空"，否则它会变成每次进菜单都弹的幽灵提示。
    /// </remarks>
    [TestFixture]
    public sealed class MultiplayerKickRulesTests
    {
        /// <summary>战局中、房间已空、自己不在名单：必须强制返回（解散房间的兜底）。</summary>
        [Test]
        public void 战局中房间没了要强制返回()
        {
            Assert.IsTrue(MultiplayerKickRules.ShouldForceOutFromRaid(
                selfInRoom: false,
                roomPhase: LobbyPhase.Empty,
                phase: MultiplayerClientPhase.InRaid));
        }

        /// <summary>其余组合一律不强制返回，避免误踢与重复处理。</summary>
        [Test]
        public void 其余情形不强制返回()
        {
            // 自己在名单里：房间广播只是还没刷新到我们（例如刚进房），绝不能误踢。
            Assert.IsFalse(MultiplayerKickRules.ShouldForceOutFromRaid(
                true, LobbyPhase.Empty, MultiplayerClientPhase.InRaid));

            // 房间还在（等待中 / 战局中）：自己被移出但房间没散——由结果消息那条入口处理。
            Assert.IsFalse(MultiplayerKickRules.ShouldForceOutFromRaid(
                false, LobbyPhase.Waiting, MultiplayerClientPhase.InRaid));
            Assert.IsFalse(MultiplayerKickRules.ShouldForceOutFromRaid(
                false, LobbyPhase.InRaid, MultiplayerClientPhase.InRaid));

            // 不在战局：大厅 / 安全屋里的正常路径自己会回到"已登录"。
            Assert.IsFalse(MultiplayerKickRules.ShouldForceOutFromRaid(
                false, LobbyPhase.Empty, MultiplayerClientPhase.InLobby));
            Assert.IsFalse(MultiplayerKickRules.ShouldForceOutFromRaid(
                false, LobbyPhase.Empty, MultiplayerClientPhase.InRoom));
            Assert.IsFalse(MultiplayerKickRules.ShouldForceOutFromRaid(
                false, LobbyPhase.Empty, MultiplayerClientPhase.Offline));
        }

        /// <summary>一次性提示：取出即清空、覆盖旧值、空白等于清除。</summary>
        [Test]
        public void 一次性提示的取值与清空()
        {
            SessionNotice.Set(null); // 先清干净，避免与其它用例的执行顺序耦合。

            SessionNotice.Set("你已被管理员移出房间。");
            Assert.AreEqual("你已被管理员移出房间。", SessionNotice.Consume());
            Assert.IsNull(SessionNotice.Consume(), "取出后必须清空，否则主菜单每次都会重复提示。");

            SessionNotice.Set("旧提示");
            SessionNotice.Set("新提示");
            Assert.AreEqual("新提示", SessionNotice.Consume(), "后写入的提示应当覆盖旧的。");

            SessionNotice.Set("   ");
            Assert.IsNull(SessionNotice.Consume(), "空白文本等价于没有提示。");
        }
    }
}
