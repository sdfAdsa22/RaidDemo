using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// P5：掉线宽限与重连的规则测试。
    /// </summary>
    /// <remarks>
    /// <para>这一批的服务器逻辑大多要在多进程里才能验证，但"名册换绑"与"宽限参数"是纯逻辑，
    /// 可以在编辑模式里逐条钉死——它们恰好也是最容易写错、且错了最难查的两处：
    /// 换绑漏了房主身份会让房间失去开局能力，宽限参数写错会让验收脚本永远等不到清场。</para>
    /// </remarks>
    [TestFixture]
    public sealed class P5ReconnectTests
    {
        /// <summary>重连换绑：成员还在名册里，只是换了连接编号。</summary>
        [Test]
        public void 换绑把成员的连接编号改到新连接()
        {
            var room = new LobbyRoom();
            room.TryCreate(1, "甲", "测试房间", string.Empty, out _);
            room.TryJoin(2, "乙", string.Empty, out _);

            Assert.IsTrue(room.TryRebind(2, 20, out var error), error.ToString());

            Assert.IsNull(room.Find(2), "旧连接不该还留在名册里。");
            Assert.IsNotNull(room.Find(20), "新连接应当接管成员身份。");
            Assert.AreEqual(2, room.MemberCount, "换绑不改变人数。");
        }

        /// <summary>房主掉线后重连，房主身份要一起带过来。</summary>
        [Test]
        public void 房主换绑后仍然是房主()
        {
            var room = new LobbyRoom();
            room.TryCreate(1, "甲", "测试房间", string.Empty, out _);
            room.TryJoin(2, "乙", string.Empty, out _);

            Assert.IsTrue(room.TryRebind(1, 10, out _));

            Assert.AreEqual(10, room.HostClientId, "房主身份必须跟着换到新连接。");
            Assert.IsTrue(room.Find(10).IsHost);

            // 换绑之后房间仍然可以被房主开局——这是"重连不影响房间"的核心判据。
            Assert.IsTrue(room.TryStartRaid(10, out var startError), startError.ToString());
        }

        /// <summary>不在名册里的连接无法换绑。</summary>
        [Test]
        public void 未知连接不能换绑()
        {
            var room = new LobbyRoom();
            room.TryCreate(1, "甲", "测试房间", string.Empty, out _);

            Assert.IsFalse(room.TryRebind(99, 100, out var error));
            Assert.AreEqual(LobbyError.NotInRoom, error);
        }

        /// <summary>宽限时长参数：合法值生效。</summary>
        [Test]
        public void 宽限参数生效()
        {
            var ok = LaunchOptions.TryParse(
                new[] { "-server", "-grace", "20" }, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(20f, options.ReconnectGraceSeconds);
        }

        /// <summary>宽限时长参数：越界要报错，而不是悄悄取默认值。</summary>
        [Test]
        public void 宽限参数越界被拒绝()
        {
            var ok = LaunchOptions.TryParse(
                new[] { "-server", "-grace", "1" }, out _, out var error);

            Assert.IsFalse(ok, "低于下界的宽限时长应当被拒绝。");
            Assert.IsNotNull(error);
        }
    }
}
