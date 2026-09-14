using System.Text;
using NUnit.Framework;
using RaidDemo.Bootstrap;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 局域网发现协议的编解码测试。
    /// </summary>
    /// <remarks>
    /// <para>只测纯逻辑，不开真实 socket：广播测试依赖网卡、防火墙与同网段环境，
    /// 换台机器就必然不稳定；而这里真正需要钉住的是"包长什么样、什么算合法包"。</para>
    ///
    /// <para>真实链路（广播 → 回包 → 界面列表）由本机多进程与双机的人工验收覆盖。</para>
    /// </remarks>
    [TestFixture]
    public sealed class LanDiscoveryTests
    {
        /// <summary>造一条正常的房间公告。</summary>
        private static LanRoomInfo NewInfo()
        {
            return new LanRoomInfo
            {
                RoomName = "工业区仓库",
                GamePort = LaunchOptions.DefaultPort,
                PlayerCount = 2,
                MaxPlayers = LobbyLimits.MaxPlayers,
                HasPassword = true,
                Phase = (byte)LobbyPhase.Waiting,
            };
        }

        [Test]
        public void EncodeProbe_MatchesProbeToken()
        {
            var expected = Encoding.UTF8.GetBytes(LanDiscoveryConstants.ProbeToken);
            var actual = LanDiscoveryCodec.EncodeProbe();

            Assert.AreEqual(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], actual[i], $"探测包第 {i} 个字节不一致。");
            }
        }

        [Test]
        public void Reply_RoundTripsAllFields()
        {
            var info = NewInfo();
            var payload = LanDiscoveryCodec.EncodeReply(info);

            var ok = LanDiscoveryCodec.TryParseReply(payload, "192.168.1.7", out var parsed);

            Assert.IsTrue(ok);
            Assert.AreEqual(info.RoomName, parsed.RoomName);
            Assert.AreEqual(info.GamePort, parsed.GamePort);
            Assert.AreEqual(info.PlayerCount, parsed.PlayerCount);
            Assert.AreEqual(info.MaxPlayers, parsed.MaxPlayers);
            Assert.AreEqual(info.HasPassword, parsed.HasPassword);
            Assert.AreEqual(info.Phase, parsed.Phase);
        }

        [Test]
        public void Reply_AddressComesFromSocketNotPayload()
        {
            var payload = LanDiscoveryCodec.EncodeReply(NewInfo());

            Assert.IsTrue(LanDiscoveryCodec.TryParseReply(payload, "10.0.0.9", out var parsed));
            Assert.AreEqual("10.0.0.9", parsed.Address);
        }

        [Test]
        public void Reply_WithWrongToken_IsRejected()
        {
            var payload = LanDiscoveryCodec.EncodeReply(NewInfo()).Replace(LanDiscoveryConstants.ReplyToken, "OTHER");

            Assert.IsFalse(LanDiscoveryCodec.TryParseReply(payload, "127.0.0.1", out _));
        }

        [TestCase("")]
        [TestCase("RAIDDEMO-LAN-1-ROOM")]
        [TestCase("RAIDDEMO-LAN-1-ROOM|房间|7777|1")]
        [TestCase("RAIDDEMO-LAN-1-ROOM|房间|abc|1|4|0|1")]
        [TestCase("RAIDDEMO-LAN-1-ROOM|房间|7777|1|9|0|1")]
        [TestCase("RAIDDEMO-LAN-1-ROOM|房间|7777|1|4|2|1")]
        [TestCase("RAIDDEMO-LAN-1-ROOM|房间|7777|1|4|0|9")]
        public void Reply_MalformedPayloads_AreRejected(string payload)
        {
            Assert.IsFalse(
                LanDiscoveryCodec.TryParseReply(payload, "127.0.0.1", out _),
                $"这条载荷不该被接受：{payload}");
        }

        [Test]
        public void SanitizeField_RemovesSeparatorAndControlCharacters()
        {
            var cleaned = LanDiscoveryCodec.SanitizeField("房间|带竖线\n带换行");

            Assert.AreEqual("房间带竖线带换行", cleaned);
        }

        [Test]
        public void EncodeReply_SanitizesRoomName_SoItStillParses()
        {
            var info = NewInfo();
            info.RoomName = "坏|名字";

            var payload = LanDiscoveryCodec.EncodeReply(info);
            var fieldCount = payload.Split(LanDiscoveryCodec.Separator).Length;

            Assert.AreEqual(LanDiscoveryCodec.ReplyFieldCount, fieldCount, "房间名里的分隔符没有被过滤掉。");
            Assert.IsTrue(LanDiscoveryCodec.TryParseReply(payload, "127.0.0.1", out var parsed));
            Assert.AreEqual("坏名字", parsed.RoomName);
        }

        [Test]
        public void Describe_ShowsPhasePasswordAndCount()
        {
            var info = NewInfo();
            info.RoomName = "测试房";

            var text = info.Describe();

            Assert.IsTrue(text.Contains("测试房"), text);
            Assert.IsTrue(text.Contains("等待中"), text);
            Assert.IsTrue(text.Contains("有密码"), text);
            Assert.IsTrue(info.IsJoinable, "等待中且未满员的房间应可加入。");
        }

        [Test]
        public void IsJoinable_IsFalseDuringRaidOrWhenFull()
        {
            var inRaid = NewInfo();
            inRaid.Phase = (byte)LobbyPhase.InRaid;
            Assert.IsFalse(inRaid.IsJoinable);

            var full = NewInfo();
            full.PlayerCount = LobbyLimits.MaxPlayers;
            Assert.IsFalse(full.IsJoinable);
        }
    }
}
