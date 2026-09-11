using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 撤退状态：远离威胁并恢复生命，恢复到阈值后重新投入战斗。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要一个会逃跑的 AI：</b>不会撤退的敌人只是会还击的靶子，
    /// 玩家很快就能总结出"站着对枪"这一种解法。会撤退的敌人迫使玩家做决策：
    /// 追上去补枪（消耗时间与位置），还是先搜刮（放过它，但它可能再回来）。</para>
    ///
    /// <para><b>灰盒阶段的治疗是"停下来恢复"，不是使用医疗物品。</b>
    /// 医疗物品属于 M6 的局外经济循环，这里先用匀速恢复把状态链路跑通，
    /// 参数与行为都集中在 <see cref="AIPerceptionProfile"/>，届时替换为物品调用即可。</para>
    ///
    /// <para><b>撤退仍然正面朝向威胁：</b>俯视角下"背对"看起来像失控逃跑，
    /// 而正面后撤既让玩家看得懂 AI 在做什么，也保留了它随时还击的可能性。</para>
    /// </remarks>
    public sealed class RetreatState : IState<AiContext>
    {
        /// <inheritdoc />
        public AiStateId Id
        {
            get { return AiStateId.Retreat; }
        }

        /// <inheritdoc />
        public void Enter(AiContext context)
        {
            // 丢掉来时的路径：撤退方向通常与来路相反，沿用旧路径会让 AI 先冲向玩家再掉头。
            context.Self.ResetPath();
        }

        /// <inheritdoc />
        public void Exit(AiContext context)
        {
            context.Intent.WantsToFire = false;
        }

        /// <inheritdoc />
        public AiTransition Tick(AiContext context, float deltaTime)
        {
            var profile = context.Profile;
            var self = context.Self;

            self.Heal(profile.HealPerSecondDuringRetreat * deltaTime);

            if (!context.Snapshot.Target.Exists)
            {
                return new AiTransition(AiStateId.Patrol, "目标已不存在");
            }

            if (!context.TryResolveThreatPosition(out var threatPosition))
            {
                // 威胁位置彻底丢失（看不见且记忆过期），此时没有"背离谁"可言。
                return new AiTransition(AiStateId.Patrol, "威胁位置丢失");
            }

            var away = self.Position - threatPosition;
            if (away.IsNearlyZero)
            {
                // 与威胁完全重合时没有远离方向，用当前朝向的反方向兜底。
                away = Vector2F.FromDegrees(self.FacingDegrees + 180f);
            }

            var awayDirection = away.Normalized;

            // 面向威胁（即背离方向的相反方向）。Shared 层的 Vector2F 没有一元取反运算符，
            // 这里显式取负分量，避免为了一个符号而去改动被客户端与服务端共用的数据结构。
            context.Intent.Facing = new Vector2F(-awayDirection.X, -awayDirection.Y);
            context.Intent.HasMoveGoal = true;
            context.Intent.MoveGoal = threatPosition + (awayDirection * profile.RetreatDistanceMeters);

            if (self.HealthRatio >= profile.RetreatHealCeilingRatio)
            {
                var canFight = context.Snapshot.SeesTarget || context.HasFreshMemory;
                return canFight
                    ? new AiTransition(AiStateId.Engage, "恢复后重新交战")
                    : new AiTransition(AiStateId.Patrol, "恢复后失去目标");
            }

            return AiTransition.None;
        }
    }
}
