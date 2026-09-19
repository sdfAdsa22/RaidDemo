using NUnit.Framework;
using RaidDemo.Bootstrap;
using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// M10 第 13.1 节：版本标识（buildId）的组装与联机握手判定。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这块必须单测：</b>真机上复现一次"版本不一致"要准备两个不同版本的构建，
    /// 代价高到没人会为每次改动重跑。而它的规则（怎么拼、什么算一致、缺失怎么办）是纯字符串逻辑，
    /// 放在 EditMode 里可以逐条钉死。真机演练（版本握手验收脚本，见 <c>Tools/UpdateSource/README.md</c>）
    /// 只需要验证"这条规则确实接进了联机链路"。</para>
    ///
    /// <para><b>协议那两条同理：</b>"加了字段但忘了写进 <c>NetworkSerialize</c>"不会报错，
    /// 字段永远是被序列化一方的默认值，症状是"版本明明一样却被拒"或"永远不拒"。
    /// 往返一次序列化就能把它钉死（与 P55 的交易消息同一类教训）。</para>
    /// </remarks>
    [TestFixture]
    public sealed class BuildHandshakeTests
    {
        /// <summary>三段齐备时按"本体+资源+代码"拼装，资源段只取内容版本的最后一段。</summary>
        [Test]
        public void 组装_按本体加资源加代码拼装()
        {
            Assert.AreEqual(
                "0.10.0+c6f6221e7",
                BuildIdentity.Compose("0.10.0", "0.10.0.c6f6221e7", string.Empty),
                "资源段的语义是'这批资源是哪一份'，重复的本体前缀不该出现在标识里。");

            Assert.AreEqual(
                "0.10.0+c6f6221e7+d41b77ee",
                BuildIdentity.Compose("0.10.0", "0.10.0.c6f6221e7", "d41b77ee"),
                "代码段当前恒为空，但填上时必须落在第三段。");
        }

        /// <summary>没有内容层（服务器、未配置更新源的客户端）时只留本体段。</summary>
        [Test]
        public void 组装_没有内容层时只留本体段()
        {
            Assert.AreEqual("0.10.1", BuildIdentity.Compose("0.10.1", string.Empty, string.Empty));
            Assert.AreEqual("0.10.1", BuildIdentity.Compose("0.10.1", null, null));
        }

        /// <summary>段内的分隔符与空白要过滤掉，否则"第一个加号之前是本体版本"这条解析规则不再成立。</summary>
        [Test]
        public void 组装_过滤段内非法字符()
        {
            Assert.AreEqual(
                "0.10.0bad",
                BuildIdentity.Compose("0.10.0+bad", string.Empty, string.Empty),
                "本体版本里混进分隔符会让解析出歧义，必须清洗。");
        }

        /// <summary>
        /// 三段都顶满时，总长按协议字段上限收口——避免写进消息时被静默截掉半截。
        /// </summary>
        [Test]
        public void 组装_超长时截断到协议上限()
        {
            var composed = BuildIdentity.Compose(
                new string('a', 200),
                new string('b', 200),
                new string('c', 200));

            Assert.AreEqual(
                BuildIdentity.MaxLength,
                composed.Length,
                "三段都超长时必须收口到协议上限，否则写进消息会被静默截断。");
            Assert.LessOrEqual(composed.Length, 61, "FixedString64Bytes 的可用容量是 61 字节。");

            // 只超长一段时只截那一段：本体段上限 32。
            var bodyOnly = BuildIdentity.Compose(new string('a', 200), string.Empty, string.Empty);

            Assert.AreEqual(32, bodyOnly.Length, "本体段单独超长时按 32 截断。");
        }

        /// <summary>本体版本取第一个加号之前的部分。</summary>
        [Test]
        public void 解析_取第一个加号之前的部分为本体版本()
        {
            Assert.AreEqual("0.10.0", BuildIdentity.BodyVersionOf("0.10.0+c6f6221e7"));
            Assert.AreEqual("0.10.0", BuildIdentity.BodyVersionOf("0.10.0"));
            Assert.AreEqual(string.Empty, BuildIdentity.BodyVersionOf(null));
            Assert.AreEqual(string.Empty, BuildIdentity.BodyVersionOf("   "));
        }

        /// <summary>客户端没上报版本（老客户端）时按"未知"放行，而不是当成不兼容。</summary>
        [Test]
        public void 握手_客户端未上报_按未知放行()
        {
            Assert.IsTrue(
                BuildIdentity.CheckCompatibility(string.Empty, "0.10.1+c6f6221e7", out var detail),
                "老客户端没有这个字段，拒掉等于'升级服务器后全体进不去'。");
            Assert.AreEqual(string.Empty, detail);
        }

        /// <summary>服务器侧没有版本信息时同样放行（例如手工构建的测试服务器）。</summary>
        [Test]
        public void 握手_本机无版本信息_放行()
        {
            Assert.IsTrue(BuildIdentity.CheckCompatibility("0.10.1+c6f6221e7", string.Empty, out _));
        }

        /// <summary>本体一致即通过；资源段不同不影响判定（资源差异由 Addressables 吸收）。</summary>
        [Test]
        public void 握手_本体一致_资源段不同也放行()
        {
            Assert.IsTrue(
                BuildIdentity.CheckCompatibility("0.10.1+aaaa1111", "0.10.1+bbbb2222", out var detail),
                "资源热更过的客户端必须还能连上没有内容层的服务器。");
            Assert.AreEqual(string.Empty, detail);
        }

        /// <summary>本体不一致时拒绝，且说明里必须同时出现两个版本号。</summary>
        [Test]
        public void 握手_本体不一致_拒绝并给出两个版本号()
        {
            Assert.IsFalse(
                BuildIdentity.CheckCompatibility("0.10.0+c6f6221e7", "0.10.1+c6f6221e7", out var detail));

            StringAssert.Contains("0.10.0", detail);
            StringAssert.Contains("0.10.1", detail);
            StringAssert.Contains("更新", detail, "文案要告诉玩家怎么办，而不只是拒绝。");
        }

        /// <summary>当前进程的标识必须能自我握手通过（防止拼装出来的标识解析不回本体版本）。</summary>
        [Test]
        public void 握手_本机标识与自身一致()
        {
            var current = BuildIdentity.Current;

            Assert.IsFalse(string.IsNullOrEmpty(current), "本机标识不该为空。");
            Assert.IsTrue(BuildIdentity.CheckCompatibility(current, current, out _));
            Assert.IsTrue(BuildIdentity.CheckCompatibility(current, current + "+extra", out _));
        }

        /// <summary>错误码进了网络协议：这里把它钉成显式数值，提醒不要改动。</summary>
        [Test]
        public void 错误码_版本不一致的值不再变动()
        {
            Assert.AreEqual(
                18,
                (byte)LobbyError.VersionMismatch,
                "错误码一经发布不改动，否则新旧版本之间会出现'不认识的错'。");
        }

        /// <summary>请求消息往返：版本标识必须与其他字段一起原样回来。</summary>
        [Test]
        public void 请求消息_往返保持版本标识()
        {
            var sent = new LobbyRequestMessage
            {
                Kind = (byte)LobbyRequestKind.JoinRoom,
                FieldA = "房间名",
                FieldB = "1234",
                Sequence = 12,
                BuildId = "0.10.1+c6f6221e7",
            };

            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    var received = LobbyRequestMessage.Read(reader);

                    Assert.AreEqual(sent.Kind, received.Kind);
                    Assert.AreEqual(sent.FieldA.ToString(), received.FieldA.ToString());
                    Assert.AreEqual(sent.FieldB.ToString(), received.FieldB.ToString());
                    Assert.AreEqual(sent.Sequence, received.Sequence);
                    Assert.AreEqual(
                        "0.10.1+c6f6221e7",
                        received.BuildId.ToString(),
                        "漏写进 NetworkSerialize 时这里会拿到空串，握手就永远不会拒绝。");
                }
            }
        }

        /// <summary>
        /// 老客户端布局：消息在序号之后就结束。读取必须不抛异常，并按"版本未知"放行。
        /// </summary>
        /// <remarks>
        /// 这条测的是"新服务器 + 老客户端"这一半错配。老客户端是改动之前构建的包，
        /// 它发的消息里没有尾部字段；整体 <c>ReadValueSafe</c> 会因缓冲区不足抛异常，
        /// 玩家看到的是"点了没反应"，而且日志里只有一条缓冲区报错——所以这里专门钉住。
        /// </remarks>
        [Test]
        public void 请求消息_老客户端缺少尾部字段_读取不抛异常且视为未知()
        {
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                // 手工写出"旧版布局"：只有改动之前就存在的四个字段。
                writer.WriteValueSafe((byte)LobbyRequestKind.JoinRoom);
                writer.WriteValueSafe(new FixedString64Bytes("老客户端"));
                writer.WriteValueSafe(new FixedString64Bytes("1234"));
                writer.WriteValueSafe(7u);

                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    LobbyRequestMessage received = default;
                    Assert.DoesNotThrow(() => received = LobbyRequestMessage.Read(reader));

                    Assert.AreEqual((byte)LobbyRequestKind.JoinRoom, received.Kind);
                    Assert.AreEqual("1234", received.FieldB.ToString());
                    Assert.AreEqual(7u, received.Sequence);
                    Assert.AreEqual(string.Empty, received.BuildId.ToString());
                    Assert.IsTrue(
                        BuildIdentity.CheckCompatibility(received.BuildId.ToString(), "0.10.1", out _),
                        "老客户端按未知放行，否则升级服务器等于把老客户端全部挡在门外。");
                }
            }
        }
    }
}
