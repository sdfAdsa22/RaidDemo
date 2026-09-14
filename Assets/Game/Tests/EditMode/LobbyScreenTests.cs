using NUnit.Framework;
using RaidDemo.UI;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 联机界面与房间界面的纯逻辑测试。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么只测这两块：</b>工程没有 EventSystem，界面上的点击、输入框与光标
    /// 都是每帧轮询出来的，那部分只能靠实机看。但"文本怎么编辑""一行字怎么拼"
    /// 是纯规则，把它们从界面里拆出来单独测，是这批改动能被验证的前提（R-3：逻辑跑 EditMode）。</para>
    /// </remarks>
    public sealed class LobbyScreenTests
    {
        [Test]
        public void TextModel_StopsAcceptingCharactersAtMaxLength()
        {
            var model = new UiTextEditModel(string.Empty, 4);

            Assert.IsTrue(model.ApplyCharacter('A'));
            Assert.IsTrue(model.ApplyCharacter('B'));
            Assert.IsTrue(model.ApplyCharacter('C'));
            Assert.IsTrue(model.ApplyCharacter('D'));
            Assert.IsFalse(model.ApplyCharacter('E'), "到达上限后不应再接受字符");
            Assert.AreEqual("ABCD", model.Value);
            Assert.AreEqual(4, model.CaretIndex);
        }

        [Test]
        public void TextModel_RejectsCharactersOutsideTheAsciiAllowList()
        {
            Assert.IsFalse(UiTextEditModel.IsAllowedCharacter('|'), "竖线是局域网文本协议的分隔符");
            Assert.IsFalse(UiTextEditModel.IsAllowedCharacter('中'), "输入法不在本批范围内（中文昵称走 -nickname）");
            Assert.IsFalse(UiTextEditModel.IsAllowedCharacter('\n'));
            Assert.IsTrue(UiTextEditModel.IsAllowedCharacter('_'));
            Assert.IsTrue(UiTextEditModel.IsAllowedCharacter('.'));
            Assert.IsTrue(UiTextEditModel.IsAllowedCharacter('7'));
        }

        [Test]
        public void TextModel_BackspaceRemovesCharacterBeforeCaret()
        {
            var model = new UiTextEditModel("1234", 8);
            model.MoveCaret(-2);

            Assert.IsTrue(model.Backspace());
            Assert.AreEqual("134", model.Value);
            Assert.AreEqual(1, model.CaretIndex);
        }

        [Test]
        public void TextModel_BackspaceAtStartDoesNothing()
        {
            var model = new UiTextEditModel("12", 8);
            model.MoveCaretTo(0);

            Assert.IsFalse(model.Backspace());
            Assert.AreEqual("12", model.Value);
            Assert.AreEqual(0, model.CaretIndex);
        }

        [Test]
        public void TextModel_DeleteRemovesCharacterAfterCaret()
        {
            var model = new UiTextEditModel("1234", 8);
            model.MoveCaretTo(1);

            Assert.IsTrue(model.DeleteForward());
            Assert.AreEqual("134", model.Value);
            Assert.AreEqual(1, model.CaretIndex);
        }

        [Test]
        public void TextModel_DeleteAtEndDoesNothing()
        {
            var model = new UiTextEditModel("12", 8);

            Assert.IsFalse(model.DeleteForward());
            Assert.AreEqual("12", model.Value);
        }

        [Test]
        public void TextModel_CaretStaysInsideTheText()
        {
            var model = new UiTextEditModel("12", 8);

            Assert.IsFalse(model.MoveCaret(5), "已经在末尾时不应再移动");
            Assert.AreEqual(2, model.CaretIndex);

            Assert.IsTrue(model.MoveCaret(-9), "越界的位移应该被夹到开头，而不是被丢掉");
            Assert.AreEqual(0, model.CaretIndex);

            Assert.IsFalse(model.MoveCaret(-1), "已经在开头时不应再移动");
            Assert.AreEqual(0, model.CaretIndex);

            Assert.IsTrue(model.MoveCaret(1));
            Assert.AreEqual(1, model.CaretIndex);
        }

        [Test]
        public void TextModel_SetValueFiltersAndTruncates()
        {
            var model = new UiTextEditModel(string.Empty, 4);

            Assert.IsTrue(model.SetValue("a|b中文cd"));
            Assert.AreEqual("abcd", model.Value);
            Assert.AreEqual(4, model.CaretIndex);
        }

        [Test]
        public void TextModel_ShrinkingMaxLengthClampsValueAndCaret()
        {
            var model = new UiTextEditModel("abcdef", 6);

            model.SetMaxLength(3);

            Assert.AreEqual(3, model.MaxLength);
            Assert.AreEqual("abc", model.Value);
            Assert.AreEqual(3, model.CaretIndex);
        }

        [Test]
        public void Mask_ReplacesEveryCharacterWithABullet()
        {
            Assert.AreEqual("●●●●", LobbyText.Mask("1234"));
            Assert.AreEqual(string.Empty, LobbyText.Mask(null));
            Assert.AreEqual(string.Empty, LobbyText.Mask(string.Empty));
        }

        [Test]
        public void Truncate_AddsEllipsisOnlyWhenTooLong()
        {
            Assert.AreEqual("abc", LobbyText.Truncate("abc", 5));
            Assert.AreEqual("ab…", LobbyText.Truncate("abcdef", 3));
            Assert.AreEqual("…", LobbyText.Truncate("abcdef", 1));
            Assert.AreEqual(string.Empty, LobbyText.Truncate(null, 5));
        }

        [Test]
        public void DescribeScanRow_FallsBackToAddressAndMarksUnjoinableRooms()
        {
            var joinable = new MultiplayerMenuRoom
            {
                Address = "192.168.1.10",
                Port = 7777,
                Description = "验收房间（1/4 · 等待中）",
                Joinable = true,
            };
            StringAssert.Contains("验收房间", LobbyText.DescribeScanRow(joinable));
            StringAssert.DoesNotContain("不可加入", LobbyText.DescribeScanRow(joinable));

            var full = joinable;
            full.Joinable = false;
            full.Description = string.Empty;
            var text = LobbyText.DescribeScanRow(full);
            StringAssert.Contains("192.168.1.10:7777", text);
            StringAssert.Contains("不可加入", text);
        }

        [Test]
        public void DescribeRoomList_ExplainsTheEmptyCase()
        {
            StringAssert.Contains("未发现房间", LobbyText.DescribeRoomList(0));
            StringAssert.Contains("3", LobbyText.DescribeRoomList(3));
        }

        [Test]
        public void DescribeRoomState_CoversTheThreePlayerFacingCases()
        {
            StringAssert.Contains("尚未加入", LobbyText.DescribeRoomState(null, false, false, 0));

            var waiting = LobbyText.DescribeRoomState("验收房间", true, false, 2);
            StringAssert.Contains("验收房间", waiting);
            StringAssert.Contains("2/4", waiting);
            StringAssert.Contains("等待中", waiting);
            StringAssert.Contains("有密码", waiting);

            var inRaid = LobbyText.DescribeRoomState("验收房间", false, true, 4);
            StringAssert.Contains("战局中", inRaid);
            StringAssert.DoesNotContain("有密码", inRaid);
        }

        [Test]
        public void DescribeMember_MarksHostAndSelf()
        {
            Assert.AreEqual("小明", LobbyText.DescribeMember("小明", false, false));
            StringAssert.Contains("房主", LobbyText.DescribeMember("小明", true, false));
            StringAssert.Contains("我", LobbyText.DescribeMember("小明", false, true));
            StringAssert.Contains("未登录", LobbyText.DescribeMember(null, false, false));
        }

        [Test]
        public void Validators_MatchTheServerSideLobbyLimits()
        {
            Assert.IsTrue(LobbyText.IsValidNickname("Player1"));
            Assert.IsFalse(LobbyText.IsValidNickname(string.Empty));
            Assert.IsFalse(LobbyText.IsValidNickname(new string('a', LobbyText.MaxNicknameLength + 1)));
            Assert.IsFalse(LobbyText.IsValidNickname("a|b"), "竖线会破坏局域网文本协议");

            Assert.IsTrue(LobbyText.IsValidPassphrase("1234"));
            Assert.IsTrue(LobbyText.IsValidPassphrase("123456"));
            Assert.IsFalse(LobbyText.IsValidPassphrase("123"), "口令至少 4 位");
            Assert.IsFalse(LobbyText.IsValidPassphrase("1234567"), "口令最多 6 位");
            Assert.IsFalse(LobbyText.IsValidPassphrase("12ab"));

            Assert.IsTrue(LobbyText.IsValidRoomPassword(string.Empty), "留空表示不设密码");
            Assert.IsTrue(LobbyText.IsValidRoomPassword("0000"));
            Assert.IsFalse(LobbyText.IsValidRoomPassword("123"), "设了密码就必须是 4 位");
            Assert.IsFalse(LobbyText.IsValidRoomPassword("12345"));

            Assert.IsTrue(LobbyText.IsValidRoomName("验收房间"));
            Assert.IsFalse(LobbyText.IsValidRoomName("   "));
            Assert.IsFalse(LobbyText.IsValidRoomName(new string('a', LobbyText.MaxRoomNameLength + 1)));
        }

        [Test]
        public void SuggestNickname_IsWithinLimitsAndDiffersBetweenCalls()
        {
            var first = LobbyText.SuggestNickname();
            Assert.IsTrue(LobbyText.IsValidNickname(first), "随机昵称必须是服务器会接受的格式");
            Assert.LessOrEqual(first.Length, LobbyText.MaxNicknameLength, "默认昵称不能超过昵称长度上限");

            // 连续两次必须不同：同机开两个客户端时，重名会被服务器直接拒绝。
            var second = LobbyText.SuggestNickname();
            Assert.AreNotEqual(first, second, "连续两次生成的默认昵称不能相同");

            // 形状固定：Player + 四位数字（界面里要截断显示，长度必须可预期）。
            Assert.IsTrue(second.StartsWith("Player", System.StringComparison.Ordinal), "默认昵称应以 Player 开头");
            Assert.AreEqual(10, second.Length, "默认昵称应为 Player + 4 位数字");
        }
    }
}
