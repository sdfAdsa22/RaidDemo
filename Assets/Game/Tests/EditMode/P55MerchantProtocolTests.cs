using NUnit.Framework;
using RaidDemo.Bootstrap;
using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// P5.5：商人交易与任务快照消息的序列化测试。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么值得单独测：</b>与背包命令消息同一条教训——"加了字段但忘了写进
    /// <c>NetworkSerialize</c>"不会报错，字段永远是被序列化的一方默认值，
    /// 症状是"某条交易总是按默认参数执行"（例如购买数量恒为 0、批量出售只卖第一件）。
    /// 往返一次就能把它钉死。</para>
    ///
    /// <para>格子列表（<c>FixedList</c>）与任务槽位（固定字段）是两种不同的编码方式，
    /// 各测各的：前者怕"循环上界两端不对称"，后者怕"越界访问抛异常把消息处理打断"。</para>
    /// </remarks>
    [TestFixture]
    public sealed class P55MerchantProtocolTests
    {
        /// <summary>购买消息：写入再读出，所有字段必须原样回来。</summary>
        [Test]
        public void 交易消息_往返保持全部字段()
        {
            var sent = new MerchantTradeMessage
            {
                Kind = MerchantTradeKinds.Buy,
                ItemId = "ammo.9x19.standard",
                Count = 30,
                Sequence = 7,
            };

            using (var writer = new FastBufferWriter(640, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out MerchantTradeMessage received);

                    Assert.AreEqual(sent.Kind, received.Kind);
                    Assert.AreEqual(sent.ItemId.ToString(), received.ItemId.ToString());
                    Assert.AreEqual(sent.Count, received.Count);
                    Assert.AreEqual(sent.Sequence, received.Sequence);
                }
            }
        }

        /// <summary>批量出售：每个格子的坐标都要原样回来（顺序也不能乱）。</summary>
        [Test]
        public void 批量出售消息_往返保持全部格子()
        {
            var sent = new MerchantTradeMessage
            {
                Kind = MerchantTradeKinds.SellBatch,
                Sequence = 9,
            };
            sent.AddCell(0, 0);
            sent.AddCell(3, 5);
            sent.AddCell(6, 6);

            using (var writer = new FastBufferWriter(640, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out MerchantTradeMessage received);

                    Assert.AreEqual(3, received.CellCount, "格子数量必须原样回来。");
                    Assert.AreEqual(0, received.Cells[0].X);
                    Assert.AreEqual(0, received.Cells[0].Y);
                    Assert.AreEqual(3, received.Cells[1].X);
                    Assert.AreEqual(5, received.Cells[1].Y);
                    Assert.AreEqual(6, received.Cells[2].X);
                    Assert.AreEqual(6, received.Cells[2].Y);
                }
            }
        }

        /// <summary>格子列表写满之后再追加要被拒绝，而不是越界写坏内存。</summary>
        [Test]
        public void 格子列表_超过容量时拒绝追加()
        {
            var message = new MerchantTradeMessage { Kind = MerchantTradeKinds.SellBatch };

            for (var i = 0; i < MerchantTradeMessage.MaxSellCells; i++)
            {
                Assert.IsTrue(message.AddCell(i % 7, i / 7), $"第 {i} 个格子应当写入成功。");
            }

            Assert.IsFalse(message.AddCell(0, 0), "超过上限的追加必须被拒绝。");
            Assert.AreEqual(MerchantTradeMessage.MaxSellCells, message.CellCount);
        }

        /// <summary>任务快照：槽位与追踪目标必须原样回来。</summary>
        [Test]
        public void 任务快照_往返保持全部槽位()
        {
            var sent = new MerchantQuestStateMessage
            {
                QuestCount = 3,
                TrackedQuestId = "quest.first_extract",
            };
            sent.SetQuest(0, new MerchantQuestEntry
            {
                QuestId = "quest.first_extract",
                State = 1,
                Progress = 0,
            });
            sent.SetQuest(1, new MerchantQuestEntry
            {
                QuestId = "quest.scavenger",
                State = 2,
                Progress = 1,
            });
            sent.SetQuest(2, new MerchantQuestEntry
            {
                QuestId = "quest.fuel_recovery",
                State = 3,
                Progress = 1,
            });

            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out MerchantQuestStateMessage received);

                    Assert.AreEqual(3, received.QuestCount);
                    Assert.AreEqual("quest.first_extract", received.TrackedQuestId.ToString());

                    var first = received.GetQuest(0);
                    Assert.AreEqual("quest.first_extract", first.QuestId.ToString());
                    Assert.AreEqual(1, first.State);

                    var second = received.GetQuest(1);
                    Assert.AreEqual("quest.scavenger", second.QuestId.ToString());
                    Assert.AreEqual(2, second.State);
                    Assert.AreEqual(1, second.Progress);

                    var third = received.GetQuest(2);
                    Assert.AreEqual(3, third.State);
                }
            }
        }

        /// <summary>越界访问任务槽位要安全：读到默认值、写入被忽略，而不是抛异常。</summary>
        [Test]
        public void 任务槽位_越界访问安全()
        {
            var message = new MerchantQuestStateMessage();

            Assert.AreEqual(default(MerchantQuestEntry).State, message.GetQuest(99).State);
            Assert.AreEqual(default(MerchantQuestEntry).Progress, message.GetQuest(-1).Progress);

            message.SetQuest(99, new MerchantQuestEntry { State = 4 });
            Assert.AreEqual(0, message.QuestCount, "越界写入不应影响任何有效槽位。");
        }

        /// <summary>结果消息：成败与中文文案必须原样回来。</summary>
        [Test]
        public void 结果消息_往返保持文案()
        {
            var sent = new MerchantResultMessage
            {
                Kind = MerchantTradeKinds.QuestClaim,
                Success = false,
                Detail = "金币不足：需要 8,250，当前 100。",
            };

            using (var writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out MerchantResultMessage received);

                    Assert.AreEqual(sent.Kind, received.Kind);
                    Assert.IsFalse(received.Success);
                    Assert.AreEqual(sent.Detail.ToString(), received.Detail.ToString());
                }
            }
        }

        /// <summary>任务种类判定：只认任务区间，其余种类为 false。</summary>
        [Test]
        public void 任务种类判定_只认任务区间()
        {
            Assert.IsFalse(MerchantTradeKinds.IsQuest(MerchantTradeKinds.Buy));
            Assert.IsFalse(MerchantTradeKinds.IsQuest(MerchantTradeKinds.Sell));
            Assert.IsFalse(MerchantTradeKinds.IsQuest(MerchantTradeKinds.SellBatch));
            Assert.IsTrue(MerchantTradeKinds.IsQuest(MerchantTradeKinds.QuestAccept));
            Assert.IsTrue(MerchantTradeKinds.IsQuest(MerchantTradeKinds.QuestClaim));
        }
    }
}
