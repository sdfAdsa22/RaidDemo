using NUnit.Framework;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 射手的"规则编号"与自伤拦截（2026-09-16 真机缺陷的回归用例）。
    /// </summary>
    /// <remarks>
    /// <para><b>缺陷原貌：</b>单机里武器控制器的事件编号是玩家编号 0，而战斗单位编号从 1 开始；
    /// 伤害规则拿 0 去查单位表永远查不到，于是"玩家之间免伤"与"不能打自己"两条一起失效。
    /// 恰好枪口在"武器视图尚未装备"的极早期会回退到角色体内，那一帧开枪的射线第一个命中的就是射手本人——
    /// 玩家点完菜单进安全屋，鼠标键还按着，AK 两发（77×2）直接把自己打成"阵亡"：
    /// 角色躺在 `die` 片段里，但因为移动是模拟层驱动的，人还能走。</para>
    ///
    /// <para>这一组把三件事钉死：绑定后自伤被丢、玩家之间仍然免伤、伤害事件带的是战斗单位编号。</para>
    /// </remarks>
    [TestFixture]
    public sealed class WeaponSelfDamageTests
    {
        /// <summary>可控的射线检测替身：精确指定"这一枪打中了谁"。</summary>
        private sealed class FakeHitProbe : IHitProbe
        {
            /// <summary>命中结果。</summary>
            public HitInfo Result { get; set; }

            /// <inheritdoc />
            public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
            {
                hit = Result;
                return true;
            }
        }

        private ItemFactory m_Factory;
        private EventBus m_Bus;
        private CombatWorld m_World;
        private FakeHitProbe m_Probe;
        private PlayerWeaponController m_Controller;
        private TestWeaponStats m_WeaponStats;
        private TestItemDefinition m_AmmoDefinition;
        private int m_ShooterCombatantId;

        [SetUp]
        public void SetUp()
        {
            m_Factory = new ItemFactory();
            m_Bus = new EventBus();
            m_World = new CombatWorld();
            m_Probe = new FakeHitProbe();

            var backpack = new InventoryGrid(6, 6, "主背包");
            var ammoPouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);
            var loadout = new PlayerLoadout(backpack, new EquipmentLoadout(), ammoPouch);

            m_Controller = new PlayerWeaponController(
                new PlayerWeapon(new DeterministicRandom(2026u)),
                loadout,
                m_Probe,
                m_World,
                new CombatTuning(),
                m_Bus);

            m_WeaponStats = new TestWeaponStats(
                baseDamage: 77f,
                roundsPerMinute: 600f,
                magazineCapacity: 30,
                caliberId: "5.45",
                reloadSeconds: 1f,
                rangeMeters: 12f);

            m_AmmoDefinition = new TestItemDefinition(
                "ammo.5.45",
                ItemCategory.Ammo,
                maxStack: 120,
                ammoStats: new TestAmmoStats("5.45", 15f));

            ammoPouch.AutoPlace(m_Factory.Create(m_AmmoDefinition, 60));
            m_Controller.SyncEquippedWeapon(m_WeaponStats);
            m_Controller.SetMuzzlePosition(Vector3.zero);

            // 玩家本人：单机里事件编号是 0，战斗单位编号是 1 —— 两者不同正是缺陷的源头。
            m_ShooterCombatantId = m_World.Create(100f, isPlayer: true);
            m_Controller.BindCombatantForRules(m_ShooterCombatantId);
        }

        /// <summary>把假射线安排成"打在指定单位身上"。</summary>
        private void ArrangeHitOn(int targetId)
        {
            var center = new Vector3(10f, 0.9f, 0f);
            m_Probe.Result = new HitInfo(targetId, center, center, 10f);
        }

        /// <summary>打中自己：既不该扣血，也不该发命中事件。</summary>
        [Test]
        public void 打中自己时不产生任何伤害()
        {
            ArrangeHitOn(m_ShooterCombatantId);

            var damageEvents = 0;
            using (m_Bus.Subscribe<DamageAppliedEvent>(_ => damageEvents++))
            {
                m_Controller.SetTriggerHeld(true);
                m_Controller.Tick(1f);
            }

            Assert.AreEqual(0, damageEvents, "自伤在物理上就不该成立：不扣血、也不给命中反馈。");
            Assert.IsTrue(m_World.TryGet(m_ShooterCombatantId, out var state));
            Assert.AreEqual(100f, state.Health, 1e-3f, "血量必须原封不动。");
        }

        /// <summary>绑定之后，玩家之间仍然免伤（U-85 的规则不能被这次修复带坏）。</summary>
        [Test]
        public void 绑定规则编号后玩家之间仍然免伤()
        {
            var teammateId = m_World.Create(100f, isPlayer: true);
            ArrangeHitOn(teammateId);

            var damageEvents = 0;
            using (m_Bus.Subscribe<DamageAppliedEvent>(_ => damageEvents++))
            {
                m_Controller.SetTriggerHeld(true);
                m_Controller.Tick(1f);
            }

            Assert.AreEqual(0, damageEvents, "PVE 合作里玩家之间不造成伤害。");
            Assert.IsTrue(m_World.TryGet(teammateId, out var teammate));
            Assert.AreEqual(100f, teammate.Health, 1e-3f);
        }

        /// <summary>伤害事件带的是射手的战斗单位编号，击杀归属才查得到人。</summary>
        [Test]
        public void 伤害事件带射手的战斗单位编号()
        {
            var enemyId = m_World.Create(100f, isPlayer: false);
            ArrangeHitOn(enemyId);

            DamageAppliedEvent captured = default;
            var seen = false;
            using (m_Bus.Subscribe<DamageAppliedEvent>(evt =>
            {
                captured = evt;
                seen = true;
            }))
            {
                m_Controller.SetTriggerHeld(true);
                m_Controller.Tick(1f);
            }

            Assert.IsTrue(seen, "打中敌人应当产生伤害事件。");
            Assert.AreEqual(
                m_ShooterCombatantId,
                captured.AttackerId,
                "击杀归属按战斗单位编号查表；用玩家编号 0 会永远对不上（单机击杀数一直不涨就是这个原因）。");
        }
    }
}
