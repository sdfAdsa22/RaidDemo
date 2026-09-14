using NUnit.Framework;
using RaidDemo.Bootstrap;
using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 背包上行消息的序列化测试。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么值得单独测：</b>网络消息里"加了字段但忘了写进 <c>NetworkSerialize</c>"
    /// 是一类**不会报错**的缺陷——字段永远是被序列化的一方默认值，
    /// 症状是"某条命令在服务器上总是按默认参数执行"（例如快速转移被当成拖拽移动）。
    /// 往返一次就能把它钉死。</para>
    /// </remarks>
    [TestFixture]
    public sealed class InventoryCommandMessageTests
    {
        /// <summary>写入再读出，所有字段必须原样回来。</summary>
        [Test]
        public void Message_RoundTripsEveryField()
        {
            var sent = new InventoryMoveCommandMessage
            {
                Kind = InventoryCommandKinds.Split,
                SourceContainerId = 100,
                TargetContainerId = 1,
                SourceCellX = 3,
                SourceCellY = 4,
                TargetCellX = 5,
                TargetCellY = 6,
                Rotated = true,
                Count = 7,
                Sequence = 42,
            };

            var received = default(InventoryMoveCommandMessage);
            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out received);
                }
            }

            Assert.AreEqual(sent.Kind, received.Kind);
            Assert.AreEqual(sent.SourceContainerId, received.SourceContainerId);
            Assert.AreEqual(sent.TargetContainerId, received.TargetContainerId);
            Assert.AreEqual(sent.SourceCellX, received.SourceCellX);
            Assert.AreEqual(sent.SourceCellY, received.SourceCellY);
            Assert.AreEqual(sent.TargetCellX, received.TargetCellX);
            Assert.AreEqual(sent.TargetCellY, received.TargetCellY);
            Assert.AreEqual(sent.Rotated, received.Rotated);
            Assert.AreEqual(sent.Count, received.Count);
            Assert.AreEqual(sent.Sequence, received.Sequence);
        }

        /// <summary>种类取值必须互不相同，否则服务器会分派到错误的意图。</summary>
        [Test]
        public void CommandKinds_AreDistinct()
        {
            var kinds = new[]
            {
                InventoryCommandKinds.Move,
                InventoryCommandKinds.QuickTransfer,
                InventoryCommandKinds.Rotate,
                InventoryCommandKinds.Sort,
                InventoryCommandKinds.Split,
            };

            for (var i = 0; i < kinds.Length; i++)
            {
                for (var j = i + 1; j < kinds.Length; j++)
                {
                    Assert.AreNotEqual(
                        kinds[i],
                        kinds[j],
                        $"命令种类取值重复：{kinds[i]} 与 {kinds[j]}。");
                }
            }
        }
    }
}
