using NUnit.Framework;
using RaidDemo.Combat;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// PVE 合作战斗规则测试：玩家之间不造成伤害（2026-09-15 定稿，U-85 的修复）。
    /// </summary>
    /// <remarks>
    /// <para>这条规则曾经不存在：验收机器人在没有敌人的阶段把朝向对准最近的队友，
    /// 而"验收模式一律扣着扳机"的旧规则让它一路朝队友扫射，把房主打至倒地（U-85）。
    /// 规则本身是纯函数，因此在这里把四种组合逐条钉死；开火流程只负责调用它。</para>
    /// </remarks>
    [TestFixture]
    public sealed class CombatRulesTests
    {
        /// <summary>玩家打玩家：必须被拦下。</summary>
        [Test]
        public void 玩家打玩家_被友军免伤拦下()
        {
            var shooter = new CombatantState(1, 100f, armor: null, isPlayer: true);
            var target = new CombatantState(2, 100f, armor: null, isPlayer: true);

            Assert.IsTrue(CombatRules.BlocksFriendlyDamage(shooter, target));
        }

        /// <summary>玩家打 AI：正常结算。</summary>
        [Test]
        public void 玩家打AI_不被拦下()
        {
            var shooter = new CombatantState(1, 100f, armor: null, isPlayer: true);
            var target = new CombatantState(2, 100f);

            Assert.IsFalse(CombatRules.BlocksFriendlyDamage(shooter, target));
        }

        /// <summary>AI 打玩家：正常结算（敌人依然能打伤玩家）。</summary>
        [Test]
        public void AI打玩家_不被拦下()
        {
            var shooter = new CombatantState(1, 100f);
            var target = new CombatantState(2, 100f, armor: null, isPlayer: true);

            Assert.IsFalse(CombatRules.BlocksFriendlyDamage(shooter, target));
        }

        /// <summary>AI 打 AI：正常结算。</summary>
        [Test]
        public void AI打AI_不被拦下()
        {
            var shooter = new CombatantState(1, 100f);
            var target = new CombatantState(2, 100f);

            Assert.IsFalse(CombatRules.BlocksFriendlyDamage(shooter, target));
        }

        /// <summary>任一参数为空：不拦截（让原有流程自己处理缺失单位）。</summary>
        [Test]
        public void 参数为空时不拦截()
        {
            var player = new CombatantState(1, 100f, armor: null, isPlayer: true);

            Assert.IsFalse(CombatRules.BlocksFriendlyDamage(null, player));
            Assert.IsFalse(CombatRules.BlocksFriendlyDamage(player, null));
            Assert.IsFalse(CombatRules.BlocksFriendlyDamage(null, null));
        }

        /// <summary>战斗世界的 Create 要把"是不是玩家"透传进单位状态。</summary>
        [Test]
        public void 战斗世界创建单位时透传玩家标记()
        {
            var world = new CombatWorld();

            var player = world.Create(100f, armor: null, isPlayer: true);
            var enemy = world.Create(100f);

            Assert.IsTrue(world.TryGet(player, out var playerState));
            Assert.IsTrue(playerState.IsPlayer, "玩家单位必须带玩家标记。");

            Assert.IsTrue(world.TryGet(enemy, out var enemyState));
            Assert.IsFalse(enemyState.IsPlayer, "默认创建的（AI）单位不应带玩家标记。");
        }
    }
}
