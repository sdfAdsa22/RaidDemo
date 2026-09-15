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
        public void RoomName_RespectsCharacterLimit()
        {
            var ok = new string('R', LobbyLimits.MaxRoomNameLength);
            var tooLong = new string('R', LobbyLimits.MaxRoomNameLength + 1);

            Assert.IsTrue(LobbyLimits.IsValidRoomName(ok));
            Assert.IsFalse(LobbyLimits.IsValidRoomName(tooLong));
            Assert.IsFalse(LobbyLimits.IsValidRoomName("  "));
        }

        /// <summary>
        /// 房间名还要过 UTF-8 字节容量这一关（`RD-AUD-053`）。
        /// </summary>
        /// <remarks>
        /// 房间名会被原样写进 <c>FixedString64Bytes</c>（可用 61 字节），而汉字在 UTF-8 下是 3 字节：
        /// 24 个汉字约 72 字节，写进协议会被**静默截断**——玩家看到的是"名字少了几个字"，
        /// 而日志里什么都没有。因此字符数与字节数两道都要校验。
        /// </remarks>
        [Test]
        public void RoomName_RejectsTextBeyondTheProtocolByteCapacity()
        {
            var bytesPerHan = System.Text.Encoding.UTF8.GetByteCount("房");
            var fitCount = LobbyLimits.MaxFixedString64ByteCapacity / bytesPerHan;
            var fit = new string('房', fitCount);
            var overflow = new string('房', fitCount + 1);

            Assert.LessOrEqual(fit.Length, LobbyLimits.MaxRoomNameLength, "这条用例的前提是字符数本身没超限。");
            Assert.IsTrue(
                LobbyLimits.IsValidRoomName(fit),
                $"{fit.Length} 个汉字（约 {fit.Length * bytesPerHan} 字节）应当合法。");
            Assert.IsFalse(
                LobbyLimits.IsValidRoomName(overflow),
                $"{overflow.Length} 个汉字超过 {LobbyLimits.MaxFixedString64ByteCapacity} 字节，必须拒绝而不是截断。");
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
