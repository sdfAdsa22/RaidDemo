using System.Text;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// P4.5-b（共享安全屋 + 出口门禁）的协议与场景约定测试。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这些约定值得钉住：</b>它们全都是"错了不会报错"的那一类——
    /// 场景名拼错只会让 <c>LoadScene</c> 抛一条运行时异常；消息里漏写字段只会让
    /// 服务器拿到空地图名；两个场景名在构建列表里缺失只会在出包之后才暴露。
    /// 三条用例把这三处都变成编辑模式里立刻可见的失败。</para>
    /// </remarks>
    [TestFixture]
    public sealed class P45SharedSafeHouseTests
    {
        /// <summary>安全屋与默认战局地图必须在 Build Settings 里，且名字与常量完全一致。</summary>
        /// <remarks>
        /// 服务器要在两张场景之间切换，客户端也跟着服务器的通知切图；
        /// 构建列表里少一张，或者场景被改名，联机流程会在"进房"或"开局"时直接断掉。
        /// </remarks>
        [Test]
        public void SceneConstants_MatchEnabledBuildSettings()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled)
                {
                    continue;
                }

                names.Add(System.IO.Path.GetFileNameWithoutExtension(scene.path));
            }

            Assert.Contains(GameScenes.SafeHouse, names, "构建列表里缺少安全屋场景（联机首站）。");
            Assert.Contains(GameScenes.DefaultRaid, names, "构建列表里缺少默认战局地图。");
        }

        /// <summary>场景名必须装得下大厅消息里的固定字符串（64 字节）。</summary>
        [Test]
        public void SceneNames_FitIntoFixedStringField()
        {
            Assert.LessOrEqual(
                Encoding.UTF8.GetByteCount(GameScenes.SafeHouse), 64,
                "安全屋场景名超出 LobbyRequestMessage.RaidStart / RaidEndMessage 的字段容量。");
            Assert.LessOrEqual(
                Encoding.UTF8.GetByteCount(GameScenes.DefaultRaid), 64,
                "默认战局地图名超出字段容量。");
        }

        /// <summary>开局请求用字段 A 携带地图名：往返之后必须原样回来。</summary>
        /// <remarks>
        /// 漏写这个字段的后果是"房主选了图、服务器用了默认图"——两边都以为自己对，
        /// 而且没有任何报错。
        /// </remarks>
        [Test]
        public void StartRaidRequest_RoundTripsMapName()
        {
            var sent = new LobbyRequestMessage
            {
                Kind = (byte)LobbyRequestKind.StartRaid,
                FieldA = GameScenes.DefaultRaid,
                FieldB = string.Empty,
                Sequence = 7,
            };

            var received = default(LobbyRequestMessage);
            using (var writer = new FastBufferWriter(192, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out received);
                }
            }

            Assert.AreEqual((byte)LobbyRequestKind.StartRaid, received.Kind);
            Assert.AreEqual(GameScenes.DefaultRaid, received.FieldA.ToString());
            Assert.AreEqual(sent.Sequence, received.Sequence);
        }

        /// <summary>战局结束通知要带回安全屋场景名：往返之后必须原样回来。</summary>
        [Test]
        public void RaidEndMessage_RoundTripsSceneName()
        {
            var sent = new RaidEndMessage { SceneName = GameScenes.SafeHouse };

            var received = default(RaidEndMessage);
            using (var writer = new FastBufferWriter(80, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out received);
                }
            }

            Assert.AreEqual(GameScenes.SafeHouse, received.SceneName.ToString());
        }
    }
}
