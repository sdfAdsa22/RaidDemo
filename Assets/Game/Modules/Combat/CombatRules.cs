namespace RaidDemo.Combat
{
    /// <summary>
    /// 战斗层的跨单位规则（与武器参数、命中流程都无关的那一类）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成类：</b>这类规则要在服务器、单机与测试里表现完全一致。
    /// 写成散落在开火流程里的 if，测试只能通过"真的开一枪"去覆盖；
    /// 集中成纯函数之后，规则本身可以逐条钉死，开火流程只负责调用。</para>
    /// </remarks>
    public static class CombatRules
    {
        /// <summary>
        /// 这一击是否应当被"友军免伤"拦下。
        /// </summary>
        /// <param name="shooter">射手单位；未知时为 null。</param>
        /// <param name="target">被打中的单位。</param>
        /// <returns>应当拦下返回 true（不扣血、也不发命中事件）。</returns>
        /// <remarks>
        /// <para><b>规则（2026-09-15 定稿）：</b>PVE 合作里<strong>玩家之间不造成伤害</strong>——
        /// 验收机器人曾在没有敌人的阶段朝最近的队友开火，把房主打至倒地（U-85）。
        /// AI 打玩家、玩家打 AI、AI 之间都不受影响。</para>
        ///
        /// <para><b>为什么连命中事件也不发：</b>发出去的话客户端会画出命中反馈与伤害数字，
        /// 而目标血量没变——那是比"打不中"更难解释的状态。安静地吞掉这一击才是可解释的。</para>
        /// </remarks>
        public static bool BlocksFriendlyDamage(CombatantState shooter, CombatantState target)
        {
            return shooter != null && target != null && shooter.IsPlayer && target.IsPlayer;
        }
    }
}
