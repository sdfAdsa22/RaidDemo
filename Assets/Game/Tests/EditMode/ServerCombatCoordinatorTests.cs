using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器战斗权威的测试（M9 · P2）。
    /// </summary>
    /// <remarks>
    /// <para>这些用例锁住的是"服务器说了算"这件事：弹药由服务器扣、命中由服务器判、
    /// 伤害由服务器算、死亡之后不能继续开枪。客户端只上报意图。</para>
    ///
    /// <para>命中用**编排好的探针**而不是真实物理：真实射线在测试里几乎无法稳定构造，
    /// 而这里要验证的是"拿到命中结果之后服务器怎么结算"，不是 PhysX 本身。</para>
    /// </remarks>
    [TestFixture]
    public sealed class ServerCombatCoordinatorTests
    {
        private const string CatalogPath = "Assets/Game/Content/Items/ItemCatalog.asset";
        private const int ShooterId = 1;
        private const int TargetPlayerId = 2;

        private ItemCatalog m_Catalog;
        private EventBus m_Events;
        private ScriptedProbe m_Probe;
        private ServerCombatCoordinator m_Coordinator;
        private readonly List<DamageAppliedEvent> m_DamageEvents = new List<DamageAppliedEvent>();

        [SetUp]
        public void SetUp()
        {
            m_Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.IsNotNull(m_Catalog, $"找不到物品目录：{CatalogPath}");

            m_Events = new EventBus();
            m_Probe = new ScriptedProbe();
            m_Coordinator = new ServerCombatCoordinator(
                m_Catalog,
                m_Events,
                originProvider: OriginAtZero,
                probe: m_Probe);

            m_DamageEvents.Clear();
            m_Events.Subscribe<DamageAppliedEvent>(evt => m_DamageEvents.Add(evt));
        }

        /// <summary>参战后应当配发武器与备弹。</summary>
        [Test]
        public void 参战后配发武器与备弹()
        {
            Assert.IsTrue(m_Coordinator.TryAddPlayer(ShooterId, out var error), error);
            Assert.AreEqual(1, m_Coordinator.PlayerCount);

            Assert.IsTrue(m_Coordinator.TryGetAmmo(ShooterId, out var magazine, out var reserve));
            Assert.Greater(magazine, 0, "出厂弹匣应当是满的。");
            Assert.Greater(reserve, 0, "应当配发备弹。");
        }

        /// <summary>重复参战与未知玩家的输入都要被拒绝。</summary>
        [Test]
        public void 重复参战与未知玩家被拒绝()
        {
            Assert.IsTrue(m_Coordinator.TryAddPlayer(ShooterId, out _));
            Assert.IsFalse(m_Coordinator.TryAddPlayer(ShooterId, out var duplicate));
            StringAssert.Contains("已参战", duplicate);

            Assert.IsFalse(m_Coordinator.SubmitInput(99, triggerHeld: true, aimDirection: Vector2F.Right));
            Assert.IsFalse(m_Coordinator.RequestReload(99, 1u, out var failure));
            Assert.AreEqual("not-in-combat", failure);
        }

        /// <summary>
        /// 服务器侧射击自己不该产生任何伤害（联机安全屋"一开火就倒地"的回归用例）。
        /// </summary>
        /// <remarks>
        /// <para>关键在于**让两个编号空间错开**：先造一个别的战斗单位占掉 1 号，
        /// 玩家的战斗单位编号就不再等于玩家编号。</para>
        ///
        /// <para>只绑战斗单位、不绑"规则用的射手编号"时，规则会退回用玩家编号当射手，
        /// 于是"射手就是自己"这条判定永远不成立——自伤直接穿过去。
        /// 旧用例之所以没抓到，是因为测试里玩家编号恰好等于战斗单位编号，两个空间重合了。</para>
        /// </remarks>
        [Test]
        public void 服务器侧射击自己不造成伤害()
        {
            // 先占掉 1 号战斗单位：这样玩家编号（1）与玩家的战斗单位编号（2）必然不同。
            m_Coordinator.World.Create(ServerCombatCoordinator.DefaultMaxHealth);

            Assert.IsTrue(m_Coordinator.TryAddPlayer(ShooterId, out var error), error);
            var combatantId = m_Coordinator.GetCombatantId(ShooterId);
            Assert.AreNotEqual(ShooterId, combatantId, "本用例需要两个编号空间错开才有意义。");

            m_DamageEvents.Clear();
            m_Probe.TargetId = combatantId;   // 安排成"打中的是自己"

            m_Coordinator.SubmitInput(ShooterId, triggerHeld: true, aimDirection: Vector2F.Right);
            for (var i = 0; i < 30; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            Assert.IsEmpty(m_DamageEvents, "打中自己不该产生伤害事件：自伤必须被规则拦住。");
        }

        /// <summary>扣动扳机之后弹药减少——扣弹药的权力在服务器。</summary>
        [Test]
        public void 开火消耗弹匣弹药()
        {
            m_Coordinator.TryAddPlayer(ShooterId, out _);
            m_Coordinator.TryGetAmmo(ShooterId, out var before, out _);

            var fired = new List<WeaponFiredEvent>();
            m_Events.Subscribe<WeaponFiredEvent>(evt => fired.Add(evt));

            m_Coordinator.SubmitInput(ShooterId, triggerHeld: true, aimDirection: Vector2F.Right);
            for (var i = 0; i < 20; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            m_Coordinator.TryGetAmmo(ShooterId, out var after, out _);
            Assert.Less(after, before, "持续扣扳机应当消耗弹药。");

            // 开火事件必须带上真实的射击者：客户端靠它区分"谁在开枪"（音效、动画、击杀归属）。
            // 编号空间是**战斗单位编号**——AI 的事件用的也是它，两者统一之后
            // 服务器才能把"射手"一致地翻译成玩家编号或敌人编号。
            Assert.Greater(fired.Count, 0, "应当产生开火事件。");
            var expectedCombatantId = m_Coordinator.GetCombatantId(ShooterId);
            Assert.AreNotEqual(0, expectedCombatantId, "参战之后应当拿到战斗单位编号。");
            Assert.AreEqual(
                expectedCombatantId,
                fired[0].ShooterId,
                "开火事件的射击者应当是本人的战斗单位编号（没有绑定时会退化成玩家编号或 0）。");
        }

        /// <summary>命中敌人时，服务器结算伤害并广播事件。</summary>
        [Test]
        public void 命中敌人造成伤害并广播()
        {
            m_Coordinator.TryAddPlayer(ShooterId, out _);

            // 用 AI 靶子验证伤害链路：PVE 合作里玩家之间免伤（CombatRules），
            // 拿第二名玩家当目标在这条规则生效后不会掉血（那条路径另有测试覆盖）。
            var targetCombatant = m_Coordinator.World.Create(ServerCombatCoordinator.DefaultMaxHealth);
            m_Probe.TargetId = targetCombatant;

            m_Coordinator.SubmitInput(ShooterId, triggerHeld: true, aimDirection: Vector2F.Right);
            for (var i = 0; i < 30; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            Assert.Greater(m_DamageEvents.Count, 0, "应当产生伤害事件。");
            Assert.AreEqual(targetCombatant, m_DamageEvents[0].TargetId);
            Assert.Greater(m_DamageEvents[0].Damage, 0f);
            Assert.Less(m_DamageEvents[0].RemainingHealth, ServerCombatCoordinator.DefaultMaxHealth);
        }

        /// <summary>玩家之间免伤：命中队友不产生任何伤害事件，队友血量不变（U-85 的回归）。</summary>
        [Test]
        public void 命中队友不产生伤害事件()
        {
            m_Coordinator.TryAddPlayer(ShooterId, out _);
            m_Coordinator.TryAddPlayer(TargetPlayerId, out _);

            var targetCombatant = m_Coordinator.GetCombatantId(TargetPlayerId);
            Assert.AreNotEqual(0, targetCombatant, "目标应当已参战。");
            m_Probe.TargetId = targetCombatant;

            m_Coordinator.SubmitInput(ShooterId, triggerHeld: true, aimDirection: Vector2F.Right);
            for (var i = 0; i < 30; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            Assert.AreEqual(0, m_DamageEvents.Count, "玩家之间不应产生任何伤害事件。");
            Assert.IsTrue(m_Coordinator.World.TryGet(targetCombatant, out var targetState));
            Assert.AreEqual(
                ServerCombatCoordinator.DefaultMaxHealth,
                targetState.Health,
                "队友的血量必须保持满值（2026-09-15 定稿：PVE 合作没有友伤）。");
        }

        /// <summary>打到不该打的单位（没登记的目标）不会崩，也不会凭空造伤害。</summary>
        [Test]
        public void 未登记目标的命中被忽略()
        {
            m_Coordinator.TryAddPlayer(ShooterId, out _);
            m_Probe.TargetId = 9999;

            m_Coordinator.SubmitInput(ShooterId, triggerHeld: true, aimDirection: Vector2F.Right);
            for (var i = 0; i < 30; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            Assert.AreEqual(0, m_DamageEvents.Count);
        }

        /// <summary>被击杀的玩家必须停止开火，否则尸体会一直射击。</summary>
        [Test]
        public void 阵亡玩家停止开火()
        {
            m_Coordinator.TryAddPlayer(TargetPlayerId, out _);
            var combatant = m_Coordinator.GetCombatantId(TargetPlayerId);
            Assert.AreNotEqual(0, combatant);

            m_Coordinator.OnDamageApplied(new DamageAppliedEvent(
                attackerId: ShooterId,
                targetId: combatant,
                damage: 999f,
                armorDamage: 0f,
                isCritical: false,
                penetrationFactor: 1f,
                remainingHealth: 0f,
                wasKilled: true,
                timestamp: 0d,
                sequence: 1u));

            m_Coordinator.SubmitInput(TargetPlayerId, triggerHeld: true, aimDirection: Vector2F.Right);
            m_Coordinator.TryGetAmmo(TargetPlayerId, out var before, out _);

            for (var i = 0; i < 30; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            m_Coordinator.TryGetAmmo(TargetPlayerId, out var after, out _);
            Assert.AreEqual(before, after, "阵亡后不应再消耗弹药。");
        }

        /// <summary>换弹从弹药挂里扣备弹，而不是凭空补满。</summary>
        [Test]
        public void 换弹消耗备弹()
        {
            m_Coordinator.TryAddPlayer(ShooterId, out _);

            // 先打掉几发，否则满弹匣无法开始换弹。
            m_Coordinator.SubmitInput(ShooterId, triggerHeld: true, aimDirection: Vector2F.Right);
            for (var i = 0; i < 10; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            m_Coordinator.SubmitInput(ShooterId, triggerHeld: false, aimDirection: Vector2F.Right);
            m_Coordinator.TryGetAmmo(ShooterId, out var magazineBefore, out var reserveBefore);
            Assert.Less(magazineBefore, 30, "应当已经打掉一些子弹。");

            Assert.IsTrue(m_Coordinator.RequestReload(ShooterId, 1u, out var failure), failure);

            // 换弹需要时间：推进足够长的步数让它完成。
            for (var i = 0; i < 240; i++)
            {
                m_Coordinator.Tick(1f / 60f);
            }

            m_Coordinator.TryGetAmmo(ShooterId, out var magazineAfter, out var reserveAfter);
            Assert.Greater(magazineAfter, magazineBefore, "换弹后弹匣应当更满。");
            Assert.Less(reserveAfter, reserveBefore, "换弹应当扣掉备弹。");
        }

        /// <summary>退出战斗后不再参战，战斗单位也被移除。</summary>
        [Test]
        public void 退出战斗后移除单位()
        {
            m_Coordinator.TryAddPlayer(ShooterId, out _);
            Assert.IsTrue(m_Coordinator.RemovePlayer(ShooterId));
            Assert.AreEqual(0, m_Coordinator.PlayerCount);
            Assert.IsFalse(m_Coordinator.TryGetAmmo(ShooterId, out _, out _));
        }

        /// <summary>
        /// 固定返回原点的坐标提供者（这些用例不关心子弹从哪发出）。
        /// </summary>
        /// <remarks>
        /// 真实实现允许返回 false（身体还没登记，此时这一帧不开火）；
        /// 这里始终给得出坐标，因此不影响"输入 → 开火 → 结算"这些用例。
        /// </remarks>
        private static bool OriginAtZero(int playerId, out Vector3 origin)
        {
            origin = Vector3.zero;
            return true;
        }

        /// <summary>永远拿不到坐标的提供者（模拟"刚进图、身体还没登记"）。</summary>
        private static bool OriginUnavailable(int playerId, out Vector3 origin)
        {
            origin = default;
            return false;
        }

        /// <summary>
        /// 拿不到玩家坐标时**一枪都不该打出去**（刚进图、身体还没登记的那一帧）。
        /// </summary>
        /// <remarks>
        /// 从前这里会退回世界原点：子弹从地图另一头飞出去，
        /// 表现就是负责人反馈的"刚进图第一枪弹道和准星不在一条线上"（问题 7）。
        /// 正确行为是这一帧不开火——既不扣弹药，也不发开火事件。
        /// </remarks>
        [Test]
        public void 拿不到玩家坐标时不开火()
        {
            var coordinator = new ServerCombatCoordinator(
                m_Catalog,
                m_Events,
                originProvider: OriginUnavailable,
                probe: m_Probe);

            Assert.IsTrue(coordinator.TryAddPlayer(ShooterId, out var error), error);
            coordinator.TryGetAmmo(ShooterId, out var before, out _);

            var fired = new List<WeaponFiredEvent>();
            m_Events.Subscribe<WeaponFiredEvent>(evt => fired.Add(evt));

            coordinator.SubmitInput(ShooterId, triggerHeld: true, aimDirection: Vector2F.Right);
            for (var i = 0; i < 30; i++)
            {
                coordinator.Tick(1f / 60f);
            }

            coordinator.TryGetAmmo(ShooterId, out var after, out _);
            Assert.AreEqual(before, after, "拿不到坐标时不该消耗弹药。");
            Assert.IsEmpty(fired, "拿不到坐标时不该产生开火事件。");
        }

        /// <summary>按脚本返回命中结果的探针。</summary>
        private sealed class ScriptedProbe : IHitProbe
        {
            /// <summary>要命中的目标标识；0 表示打空。</summary>
            public int TargetId;

            /// <summary>命中点。</summary>
            public Vector3 Point = new Vector3(0f, 0f, 5f);

            public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
            {
                if (TargetId == 0)
                {
                    hit = default;
                    return false;
                }

                hit = new HitInfo(TargetId, Point, Point + new Vector3(0f, -1f, 0f), Point.magnitude);
                return true;
            }

            /// <inheritdoc />
            public bool TryRaycastIgnoringTarget(
                Vector3 origin,
                Vector3 direction,
                float maxDistance,
                int ignoredTargetId,
                out HitInfo hit)
            {
                return TryRaycast(origin, direction, maxDistance, out hit);
            }
        }
    }
}
