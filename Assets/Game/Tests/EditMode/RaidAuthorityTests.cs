using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Raid;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 联机权威的两条规则：客户端不自行结束战局（`RD-AUD-041`）、
    /// 结算数字以服务器为准（`RD-AUD-049`）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这两条要单测：</b>它们都只在联机下才生效，而联机的真机验收成本高；
    /// 规则本身却是纯逻辑——"时钟走到上限时结不结束""金额取谁的"，
    /// 放成 EditMode 用例就能在毫秒级钉住，避免哪次重构把联机又退回"本地自己结算"。</para>
    /// </remarks>
    [TestFixture]
    public sealed class RaidAuthorityTests
    {
        private EventBus m_Bus;
        private RaidSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Bus = new EventBus();
            m_Settings = new RaidSettings { RaidDurationSeconds = 10f, ExtractionDurationSeconds = 2f };
        }

        /// <summary>单机：本地时钟走到上限就收尾（现有行为不变）。</summary>
        [Test]
        public void 单机时本地时钟到点即结束战局()
        {
            var session = new RaidSession(m_Settings, m_Bus, playerCombatantId: 1);
            session.Start(broughtInValue: 0);

            session.Tick(m_Settings.RaidDurationSeconds + 0.1f);

            Assert.AreEqual(RaidOutcome.TimeExpired, session.Outcome, "单机没有服务器裁定，本地必须自己收尾。");
        }

        /// <summary>联机：本地时钟照走（HUD 要有数字），但绝不自行结束。</summary>
        [Test]
        public void 联机时本地时钟到点不结束战局()
        {
            var session = new RaidSession(m_Settings, m_Bus, playerCombatantId: 1)
            {
                IsServerAuthoritative = true,
            };
            session.Start(broughtInValue: 0);

            session.Tick(m_Settings.RaidDurationSeconds + 5f);

            Assert.AreEqual(RaidOutcome.InProgress, session.Outcome, "结束只能由服务器下发的结算消息触发。");
            Assert.AreEqual(
                m_Settings.RaidDurationSeconds,
                session.ElapsedSeconds,
                1e-3f,
                "本地时钟仍要推进并停在上限，HUD 的倒计时才有的显示。");
            Assert.AreEqual(0f, session.RemainingSeconds, 1e-3f, "到点后剩余时间为 0。");

            // 服务器随后下发结果：这时才结束。
            session.NotifyTimeExpired();
            Assert.AreEqual(
                RaidOutcome.TimeExpired,
                session.Outcome,
                "服务器说'时间耗尽'时本地也必须记成超时——以前只有撤离/阵亡两个入口，超时会被显示成阵亡。");
        }

        /// <summary>服务器给的带出价值覆盖本地清单求和（两者不一致时以服务器为准）。</summary>
        [Test]
        public void 结算金额以服务器为准()
        {
            var loadout = new PlayerLoadout(
                new InventoryGrid(5, 5, "主背包"),
                new EquipmentLoadout());
            var factory = new ItemFactory();
            var loot = new TestItemDefinition("loot.bolt", ItemCategory.Loot, baseValue: 600, maxStack: 20);
            loadout.Backpack.AutoPlace(factory.Create(loot, 5));

            var local = RaidResult.Create(RaidOutcome.Extracted, kills: 1, elapsedSeconds: 30f, broughtInValue: 0, loadout);
            Assert.AreEqual(3000, local.ExtractedValue, "不传权威值时按本地清单求和。");

            var authoritative = RaidResult.Create(
                RaidOutcome.Extracted,
                kills: 4,
                elapsedSeconds: 125f,
                broughtInValue: 0,
                loadout,
                authoritativeExtractedValue: 1234);

            Assert.AreEqual(1234, authoritative.ExtractedValue, "服务器给了数字就必须用服务器的。");
            Assert.AreEqual(4, authoritative.Kills, "击杀数同理（本地可能还停在 0）。");
            Assert.AreEqual(125f, authoritative.ElapsedSeconds);
        }
    }
}
