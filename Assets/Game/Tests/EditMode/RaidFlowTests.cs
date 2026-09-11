using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Raid;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 战局闭环测试：计时、撤离、搜刮读条、结算。
    /// </summary>
    /// <remarks>
    /// 这四项对应玩家从主菜单打到撤离的完整路径。每一段都能单独断言，
    /// 出问题时就立刻知道是计时、撤离、搜刮还是结算坏了。
    /// </remarks>
    [TestFixture]
    public sealed class RaidFlowTests
    {
        private const int PlayerCombatantId = 7;

        private EventBus m_Bus;
        private RaidSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Bus = new EventBus();
            m_Settings = new RaidSettings
            {
                RaidDurationSeconds = 100f,
                ExtractionDurationSeconds = 10f,
            };
        }

        [Test]
        public void 时间耗尽时战局以超时结束()
        {
            var session = new RaidSession(m_Settings, m_Bus, PlayerCombatantId);
            session.Start(broughtInValue: 1000);

            RaidEndedEvent? received = null;
            using (m_Bus.Subscribe<RaidEndedEvent>(evt => received = evt))
            {
                for (var i = 0; i < 101; i++)
                {
                    session.Tick(1f);
                }
            }

            Assert.AreEqual(RaidOutcome.TimeExpired, session.Outcome);
            Assert.IsFalse(session.IsActive, "超时之后战局不应仍在进行");
            Assert.AreEqual(0f, session.RemainingSeconds, 0.001f, "剩余时间不应出现负数");
            Assert.IsTrue(received.HasValue, "超时必须广播结束事件");
            Assert.AreEqual(RaidOutcome.TimeExpired, received.Value.Outcome);
            Assert.AreEqual(100f, received.Value.ElapsedSeconds, 0.001f, "时长应被截断到总时长");
        }

        [Test]
        public void 在撤离区读满时间后撤离成功()
        {
            var zones = new List<ExtractionZone>
            {
                new ExtractionZone(1, "北门", new Vector2F(0f, 25f), 3.5f),
            };
            var tracker = new ExtractionTracker(zones, m_Settings.ExtractionDurationSeconds, m_Bus);
            var session = new RaidSession(m_Settings, m_Bus, PlayerCombatantId);
            session.Start(broughtInValue: 0);

            using (m_Bus.Subscribe<ExtractionCompletedEvent>(_ => session.NotifyExtracted()))
            {
                // 先在区外走一步：不应开始读秒
                tracker.Tick(1f, new Vector2F(20f, 0f), playerAlive: true);
                Assert.IsNull(tracker.ActiveZone, "区外不应开始撤离读秒");

                var inside = new Vector2F(0f, 25f);
                for (var i = 0; i < 9; i++)
                {
                    tracker.Tick(1f, inside, playerAlive: true);
                }

                Assert.AreEqual(RaidOutcome.InProgress, session.Outcome, "读秒未满时不应结束战局");
                Assert.AreEqual(0.9f, tracker.Progress01, 0.01f);

                tracker.Tick(1f, inside, playerAlive: true);
            }

            Assert.IsTrue(tracker.HasCompleted);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
        }

        [Test]
        public void 搜刮被打断后进度清零必须重新开始()
        {
            var search = new LootSearchInteraction(2f, 0.35f, m_Bus);
            var origin = new Vector2F(4f, -2f);
            var completed = 0;

            using (m_Bus.Subscribe<LootSearchCompletedEvent>(_ => completed++))
            {
                Assert.IsTrue(search.TryBegin(containerId: 5, origin));
                Assert.IsFalse(search.TryBegin(containerId: 6, origin), "搜刮中不应再开一个");

                search.Tick(1f, origin, playerAlive: true, wantsToMove: false);
                Assert.AreEqual(0.5f, search.Progress01, 0.01f);

                // 移动输入打断：进度清零
                search.Tick(0.1f, origin, playerAlive: true, wantsToMove: true);
                Assert.IsFalse(search.IsSearching);
                Assert.AreEqual(0f, search.Progress01);

                // 重新开始必须从头累计
                Assert.IsTrue(search.TryBegin(containerId: 5, origin));
                search.Tick(1f, origin, playerAlive: true, wantsToMove: false);
                Assert.AreEqual(0.5f, search.Progress01, 0.01f, "重开不应继承上次进度");

                search.Tick(1f, origin, playerAlive: true, wantsToMove: false);
            }

            Assert.AreEqual(1, completed, "读满两秒应恰好完成一次");
            Assert.IsFalse(search.IsSearching, "完成后状态必须清空");
            Assert.AreEqual(0, search.TargetContainerId);
        }

        [Test]
        public void 阵亡结算把全部携带物计入损失()
        {
            var backpack = new InventoryGrid(3, 3, "测试背包");
            var factory = new ItemFactory();
            var gold = new TestItemDefinition(
                "loot.watch.gold", ItemCategory.Loot, baseValue: 32000, rarity: RarityTier.Epic);
            backpack.AutoPlace(factory.Create(gold, 1));
            var loadout = new PlayerLoadout(backpack, new EquipmentLoadout());

            Assert.AreEqual(32000, RaidResult.ComputeCarriedValue(loadout));

            var killed = RaidResult.Create(
                RaidOutcome.Killed, kills: 2, elapsedSeconds: 120f, broughtInValue: 5000, loadout);
            Assert.AreEqual(0, killed.ExtractedValue, "阵亡不应带出任何价值");
            Assert.AreEqual(32000, killed.LostValue);
            Assert.AreEqual(1, killed.LostItems.Count);
            Assert.AreEqual(0, killed.ExtractedItems.Count);

            var extracted = RaidResult.Create(
                RaidOutcome.Extracted, kills: 2, elapsedSeconds: 120f, broughtInValue: 5000, loadout);
            Assert.AreEqual(32000, extracted.ExtractedValue);
            Assert.AreEqual(0, extracted.LostValue);
            Assert.AreEqual(0, extracted.LostItems.Count);
        }
    }
}
