using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Meta;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 任务系统测试：状态机、进度来源、上交与存档。
    /// </summary>
    /// <remarks>
    /// 任务系统的问题几乎都出在"状态转换"而不是数值：重复接取、
    /// 未完成就领奖、上交扣错数量。因此用例按状态转换组织，而不是按界面流程组织。
    /// </remarks>
    [TestFixture]
    public sealed class QuestSystemTests
    {
        private ItemFactory m_Factory;
        private MetaProgress m_Progress;
        private TestItemLookup m_Catalog;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Progress = new MetaProgress(0);
            m_Catalog = new TestItemLookup()
                .Add(new TestItemDefinition(
                    "ammo.5.45.standard", ItemCategory.Ammo, baseValue: 25, maxStack: 90, canRotate: false))
                .Add(new TestItemDefinition(
                    "loot.canister.fuel", ItemCategory.Loot, baseValue: 11000, maxStack: 1))
                .Add(new TestItemDefinition(
                    "medical.kit.field", ItemCategory.Medical, baseValue: 9000, maxStack: 1));
            m_Progress.AttachCatalog(m_Catalog);
        }

        [Test]
        public void 初始只有首次撤离可接取()
        {
            Assert.AreEqual(QuestState.Available, m_Progress.Quests.Get(QuestCatalog.FirstExtractId).State);

            var others = new[]
            {
                QuestCatalog.ScavengerId,
                QuestCatalog.FuelRecoveryId,
                QuestCatalog.FullHaulId,
                QuestCatalog.UntouchedId,
            };
            for (var i = 0; i < others.Length; i++)
            {
                Assert.AreEqual(
                    QuestState.Locked,
                    m_Progress.Quests.Get(others[i]).State,
                    others[i] + " 在首次撤离前应保持锁定。");
            }
        }

        [Test]
        public void 完成并领取首次撤离后解锁其余任务()
        {
            var quests = m_Progress.Quests;
            Assert.IsTrue(quests.TryAccept(QuestCatalog.FirstExtractId, out var problem), problem);
            Assert.IsTrue(quests.ReportExtraction(0, tookDamage: true), "成功撤离应推进撤离任务。");
            Assert.AreEqual(QuestState.Completed, quests.Get(QuestCatalog.FirstExtractId).State);

            Assert.IsTrue(quests.TryClaim(QuestCatalog.FirstExtractId, out problem), problem);
            Assert.AreEqual(5000, m_Progress.Money, "首次撤离奖励应即时到账。");
            Assert.AreEqual(QuestState.Available, quests.Get(QuestCatalog.ScavengerId).State);
            Assert.AreEqual(QuestState.Available, quests.Get(QuestCatalog.FuelRecoveryId).State);
            Assert.AreEqual(QuestState.Available, quests.Get(QuestCatalog.FullHaulId).State);
            Assert.AreEqual(QuestState.Available, quests.Get(QuestCatalog.UntouchedId).State);
        }

        [Test]
        public void 击杀进度达到目标后可以领奖()
        {
            CompleteFirstExtract();
            var quests = m_Progress.Quests;
            Assert.IsTrue(quests.TryAccept(QuestCatalog.ScavengerId, out var problem), problem);

            Assert.IsTrue(quests.NotifyKill(), "第一次击杀应推进进度。");
            quests.NotifyKill();
            Assert.IsTrue(quests.NotifyKill(), "第三次击杀应让任务达到完成条件。");
            Assert.AreEqual(QuestState.Completed, quests.Get(QuestCatalog.ScavengerId).State);

            Assert.IsTrue(quests.TryClaim(QuestCatalog.ScavengerId, out problem), problem);
            Assert.AreEqual(5000 + 8000, m_Progress.Money);
            Assert.AreEqual(30, CountStash("ammo.5.45.standard"), "奖励弹药应进入仓库。");
        }

        [Test]
        public void 上交任务扣除仓库物品并发放奖励()
        {
            CompleteFirstExtract();
            var quests = m_Progress.Quests;
            Assert.IsTrue(quests.TryAccept(QuestCatalog.FuelRecoveryId, out var problem), problem);

            m_Progress.Stash.AutoPlace(m_Factory.Create(m_Catalog.GetForTest("loot.canister.fuel")));
            Assert.AreEqual(1, quests.CountInStash("loot.canister.fuel"));
            Assert.IsTrue(quests.TryTurnIn(QuestCatalog.FuelRecoveryId, out problem), problem);
            Assert.AreEqual(0, CountStash("loot.canister.fuel"), "上交后物品必须从仓库扣除。");

            Assert.IsTrue(quests.TryClaim(QuestCatalog.FuelRecoveryId, out problem), problem);
            Assert.AreEqual(5000 + 15000, m_Progress.Money);
            Assert.AreEqual(20, CountStash("ammo.5.45.standard"));
        }

        [Test]
        public void 受伤的一局不计入无伤撤离()
        {
            CompleteFirstExtract();
            var quests = m_Progress.Quests;
            Assert.IsTrue(quests.TryAccept(QuestCatalog.UntouchedId, out var problem), problem);

            Assert.IsFalse(quests.ReportExtraction(0, tookDamage: true));
            Assert.AreEqual(QuestState.Active, quests.Get(QuestCatalog.UntouchedId).State);

            Assert.IsTrue(quests.ReportExtraction(0, tookDamage: false));
            Assert.AreEqual(QuestState.Completed, quests.Get(QuestCatalog.UntouchedId).State);
        }

        [Test]
        public void 单局带出价值达到阈值才完成()
        {
            CompleteFirstExtract();
            var quests = m_Progress.Quests;
            Assert.IsTrue(quests.TryAccept(QuestCatalog.FullHaulId, out var problem), problem);

            quests.ReportExtraction(10000, tookDamage: false);
            Assert.AreEqual(QuestState.Active, quests.Get(QuestCatalog.FullHaulId).State);

            Assert.IsTrue(quests.ReportExtraction(15000, tookDamage: false));
            Assert.AreEqual(QuestState.Completed, quests.Get(QuestCatalog.FullHaulId).State);
        }

        [Test]
        public void 任务进度存档往返一致()
        {
            CompleteFirstExtract();
            m_Progress.Quests.TryAccept(QuestCatalog.ScavengerId, out _);
            m_Progress.Quests.NotifyKill();
            m_Progress.Quests.NotifyKill();

            var records = m_Progress.Quests.CaptureState();
            var tracked = m_Progress.Quests.TrackedQuestId;

            var restored = new MetaProgress(0);
            restored.AttachCatalog(m_Catalog);
            var problems = new System.Collections.Generic.List<string>();
            restored.Quests.RestoreState(records, tracked, problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(QuestState.Claimed, restored.Quests.Get(QuestCatalog.FirstExtractId).State);
            Assert.AreEqual(QuestState.Active, restored.Quests.Get(QuestCatalog.ScavengerId).State);
            Assert.AreEqual(2, restored.Quests.Get(QuestCatalog.ScavengerId).Current);
        }

        [Test]
        public void 同时最多进行三个任务()
        {
            CompleteFirstExtract();
            var quests = m_Progress.Quests;

            Assert.IsTrue(quests.TryAccept(QuestCatalog.ScavengerId, out _));
            Assert.IsTrue(quests.TryAccept(QuestCatalog.FuelRecoveryId, out _));
            Assert.IsTrue(quests.TryAccept(QuestCatalog.FullHaulId, out _));

            Assert.IsFalse(
                quests.TryAccept(QuestCatalog.UntouchedId, out var problem),
                "第四个任务应当被同时进行数量上限拦下。");
            Assert.IsNotNull(problem);
            Assert.AreEqual(QuestState.Available, quests.Get(QuestCatalog.UntouchedId).State);
        }

        private void CompleteFirstExtract()
        {
            m_Progress.Quests.TryAccept(QuestCatalog.FirstExtractId, out _);
            m_Progress.Quests.ReportExtraction(0, tookDamage: true);
            m_Progress.Quests.TryClaim(QuestCatalog.FirstExtractId, out _);
        }

        private int CountStash(string itemId)
        {
            var total = 0;
            var items = m_Progress.Stash.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Definition.Id == itemId)
                {
                    total += items[i].StackCount;
                }
            }

            return total;
        }
    }
}
