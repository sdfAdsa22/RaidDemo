using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 大厅取值约束的测试（昵称、房间名、房间密码、账号口令、token）。
    /// </summary>
    /// <remarks>
    /// <para>这些约束同时被三处使用：客户端预校验、服务器校验、网络消息的字段长度。
    /// 它们一旦不一致，典型症状是"界面上允许输入、提交却被服务器拒绝"。</para>
    /// </remarks>
    [TestFixture]
    public sealed class LobbyLimitsTests
    {
        [TestCase("小明")]
        [TestCase("Player1")]
        [TestCase("a")]
        [TestCase("一二三四五六七八九十十一十二十三")]
        public void Nickname_ValidValues_AreAccepted(string nickname)
        {
            Assert.IsTrue(LobbyLimits.IsValidNickname(nickname), $"昵称「{nickname}」应当合法。");
        }

        [Test]
        public void Nickname_InvalidValues_AreRejected()
        {
            Assert.IsFalse(LobbyLimits.IsValidNickname(null));
            Assert.IsFalse(LobbyLimits.IsValidNickname(string.Empty));
            Assert.IsFalse(LobbyLimits.IsValidNickname("   "));
            Assert.IsFalse(LobbyLimits.IsValidNickname("带|竖线"));
            Assert.IsFalse(LobbyLimits.IsValidNickname("带\n换行"));

            var tooLong = new string('名', LobbyLimits.MaxNicknameLength + 1);
            Assert.IsFalse(LobbyLimits.IsValidNickname(tooLong));
        }

        [Test]
        public void RoomName_RespectsLengthLimit()
        {
            var ok = new string('房', LobbyLimits.MaxRoomNameLength);
            var tooLong = new string('房', LobbyLimits.MaxRoomNameLength + 1);

            Assert.IsTrue(LobbyLimits.IsValidRoomName(ok));
            Assert.IsFalse(LobbyLimits.IsValidRoomName(tooLong));
            Assert.IsFalse(LobbyLimits.IsValidRoomName("  "));
        }

        [TestCase("", true)]
        [TestCase("1234", true)]
        [TestCase("0000", true)]
        [TestCase("123", false)]
        [TestCase("12345", false)]
        [TestCase("12a4", false)]
        public void RoomPassword_MustBeEmptyOrFourDigits(string password, bool expected)
        {
            Assert.AreEqual(expected, LobbyLimits.IsValidRoomPassword(password));
        }

        [TestCase("1234", true)]
        [TestCase("123456", true)]
        [TestCase("123", false)]
        [TestCase("1234567", false)]
        [TestCase("abcd", false)]
        [TestCase("", false)]
        public void Passphrase_MustBeFourToSixDigits(string passphrase, bool expected)
        {
            Assert.AreEqual(expected, LobbyLimits.IsValidPassphrase(passphrase));
        }

        [Test]
        public void Token_MustBeThirtyTwoHexCharacters()
        {
            Assert.IsTrue(LobbyLimits.IsValidToken("0123456789abcdef0123456789ABCDEF"));
            Assert.IsFalse(LobbyLimits.IsValidToken("0123456789abcdef0123456789abcde"));
            Assert.IsFalse(LobbyLimits.IsValidToken("0123456789abcdef0123456789abcg"));
            Assert.IsFalse(LobbyLimits.IsValidToken(null));
        }
    }
}
