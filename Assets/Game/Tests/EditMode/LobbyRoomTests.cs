using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 房间状态机的规则测试。
    /// </summary>
    /// <remarks>
    /// <para>这些规则全部是"玩家能看见的边界"：房间满没满、密码对不对、房主走了谁开局。
    /// 它们靠开三个进程去验证成本极高，而抽成纯逻辑之后可以在这里逐条钉死。</para>
    /// </remarks>
    [TestFixture]
    public sealed class LobbyRoomTests
    {
        /// <summary>创建一个空房间。</summary>
        private static LobbyRoom NewRoom()
        {
            return new LobbyRoom();
        }

        [Test]
        public void Create_OnEmptyServer_SucceedsAndMakesCreatorHost()
        {
            var room = NewRoom();

            var created = room.TryCreate(1, "小明", "测试房间", string.Empty, out var error);

            Assert.IsTrue(created);
            Assert.AreEqual(LobbyError.None, error);
            Assert.AreEqual(LobbyPhase.Waiting, room.Phase);
            Assert.AreEqual(1, room.HostClientId);
            Assert.AreEqual(1, room.MemberCount);
            Assert.IsTrue(room.Members[0].IsHost);
            Assert.IsFalse(room.HasPassword);
        }

        [Test]
        public void Create_WhenRoomExists_IsRejected()
        {
            var room = NewRoom();
            room.TryCreate(1, "小明", "房间", string.Empty, out _);

            var created = room.TryCreate(2, "小红", "另一个房间", string.Empty, out var error);

            Assert.IsFalse(created);
            Assert.AreEqual(LobbyError.RoomExists, error);
            Assert.AreEqual(1, room.MemberCount);
        }

        [Test]
        public void Create_WithBadRoomNameOrPassword_IsRejected()
        {
            var room = NewRoom();

            Assert.IsFalse(room.TryCreate(1, "小明", "  ", string.Empty, out var nameError));
            Assert.AreEqual(LobbyError.BadRoomName, nameError);

            Assert.IsFalse(room.TryCreate(1, "小明", "房间", "12", out var passwordError));
            Assert.AreEqual(LobbyError.BadPasswordFormat, passwordError);

            Assert.AreEqual(LobbyPhase.Empty, room.Phase);
        }

        [Test]
        public void Join_RequiresMatchingPassword()
        {
            var room = NewRoom();
            room.TryCreate(1, "小明", "房间", "1234", out _);

            Assert.IsFalse(room.TryJoin(2, "小红", "9999", out var wrongPassword));
            Assert.AreEqual(LobbyError.WrongPassword, wrongPassword);

            Assert.IsTrue(room.TryJoin(2, "小红", "1234", out var ok));
            Assert.AreEqual(LobbyError.None, ok);
            Assert.AreEqual(2, room.MemberCount);
            Assert.IsFalse(room.Members[1].IsHost);
        }

        [Test]
        public void Join_OnEmptyServer_ReportsRoomNotFound()
        {
            var room = NewRoom();

            Assert.IsFalse(room.TryJoin(1, "小明", string.Empty, out var error));
            Assert.AreEqual(LobbyError.RoomNotFound, error);
        }

        [Test]
        public void Join_WhenFull_IsRejected()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);
            room.TryJoin(2, "二号", string.Empty, out _);
            room.TryJoin(3, "三号", string.Empty, out _);
            room.TryJoin(4, "四号", string.Empty, out _);

            Assert.IsFalse(room.TryJoin(5, "五号", string.Empty, out var error));
            Assert.AreEqual(LobbyError.RoomFull, error);
            Assert.AreEqual(LobbyLimits.MaxPlayers, room.MemberCount);
        }

        [Test]
        public void Join_AfterRaidStarts_IsRejected()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);
            room.TryStartRaid(1, out _);

            Assert.IsFalse(room.TryJoin(2, "迟到者", string.Empty, out var error));
            Assert.AreEqual(LobbyError.RaidRunning, error);
        }

        [Test]
        public void Join_WhenAlreadyMember_IsRejected()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);

            Assert.IsFalse(room.TryJoin(1, "房主", string.Empty, out var error));
            Assert.AreEqual(LobbyError.AlreadyInRoom, error);
        }

        [Test]
        public void Leave_WhenHostLeaves_TransfersHostToNextMember()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);
            room.TryJoin(2, "二号", string.Empty, out _);
            room.TryJoin(3, "三号", string.Empty, out _);

            Assert.IsTrue(room.TryLeave(1, out var roomEnded));

            Assert.IsFalse(roomEnded);
            Assert.AreEqual(2, room.HostClientId);
            Assert.IsTrue(room.Members[0].IsHost);
            Assert.AreEqual("二号", room.Members[0].Nickname);
        }

        [Test]
        public void Leave_WhenLastMemberLeaves_DissolvesRoom()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", "1234", out _);

            Assert.IsTrue(room.TryLeave(1, out var roomEnded));

            Assert.IsTrue(roomEnded);
            Assert.IsFalse(room.Exists);
            Assert.AreEqual(LobbyPhase.Empty, room.Phase);
            Assert.AreEqual(-1, room.HostClientId);
            Assert.AreEqual(0, room.MemberCount);
        }

        [Test]
        public void Leave_WhenNotMember_ReturnsFalse()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);

            Assert.IsFalse(room.TryLeave(9, out var roomEnded));
            Assert.IsFalse(roomEnded);
            Assert.AreEqual(1, room.MemberCount);
        }

        [Test]
        public void StartRaid_OnlyHostCanStart()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);
            room.TryJoin(2, "二号", string.Empty, out _);

            Assert.IsFalse(room.TryStartRaid(2, out var notHost));
            Assert.AreEqual(LobbyError.NotHost, notHost);
            Assert.AreEqual(LobbyPhase.Waiting, room.Phase);

            Assert.IsTrue(room.TryStartRaid(1, out var ok));
            Assert.AreEqual(LobbyError.None, ok);
            Assert.AreEqual(LobbyPhase.InRaid, room.Phase);
        }

        [Test]
        public void StartRaid_SoloPlayerIsAllowed()
        {
            var room = NewRoom();
            room.TryCreate(1, "独狼", "房间", string.Empty, out _);

            Assert.IsTrue(room.TryStartRaid(1, out _));
            Assert.AreEqual(LobbyPhase.InRaid, room.Phase);
        }

        [Test]
        public void StartRaid_WhileAlreadyInRaid_IsRejected()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);
            room.TryStartRaid(1, out _);

            Assert.IsFalse(room.TryStartRaid(1, out var error));
            Assert.AreEqual(LobbyError.StartRejected, error);
        }

        [Test]
        public void EndRaid_ReturnsToWaitingAndKeepsMembers()
        {
            var room = NewRoom();
            room.TryCreate(1, "房主", "房间", string.Empty, out _);
            room.TryJoin(2, "二号", string.Empty, out _);
            room.TryStartRaid(1, out _);

            room.EndRaid();

            Assert.AreEqual(LobbyPhase.Waiting, room.Phase);
            Assert.AreEqual(2, room.MemberCount);
            Assert.AreEqual(1, room.HostClientId);

            // 回到等待后房主可以再开一局。
            Assert.IsTrue(room.TryStartRaid(1, out _));
        }

        [Test]
        public void ToMessage_CarriesPhaseMembersAndHostFlag()
        {
            var room = NewRoom();
            room.TryCreate(1, "小明", "工业区", "1234", out _);
            room.TryJoin(2, "小红", "1234", out _);

            var message = room.ToMessage();

            Assert.AreEqual((byte)LobbyPhase.Waiting, message.Phase);
            Assert.AreEqual("工业区", message.RoomName.ToString());
            Assert.IsTrue(message.HasPassword);
            Assert.AreEqual(1, message.HostClientId);
            Assert.AreEqual(2, message.MemberCount);
            Assert.AreEqual("小明", message.GetMember(0).Nickname.ToString());
            Assert.IsTrue(message.GetMember(0).IsHost);
            Assert.AreEqual("小红", message.GetMember(1).Nickname.ToString());
            Assert.IsFalse(message.GetMember(1).IsHost);
        }
    }
}
