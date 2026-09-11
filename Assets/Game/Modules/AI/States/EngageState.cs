using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 交战状态：把站位调整到期望距离，对准目标，按点射节奏开火。
    /// </summary>
    /// <remarks>
    /// <para><b>三条让战斗"可玩"的约束：</b></para>
    /// <list type="number">
    /// <item><description><b>有反应时间。</b>看见目标后要等一小段才开火。
    /// 没有它，AI 会在玩家转过拐角的同一帧命中，体感等同于被狙击手埋伏。</description></item>
    /// <item><description><b>点射而不是持续扫射。</b>打一阵停一阵，玩家才有换位与还击的窗口。
    /// 全自动不停火的 AI 会让掩体失去意义。</description></item>
    /// <item><description><b>保持距离而不是贴脸。</b>AI 会走到"期望距离"上停下来。
    /// 贴脸的敌人会让玩家的准星失去意义（俯视角下太近的目标反而难瞄）。</description></item>
    /// </list>
    ///
    /// <para><b>看不见目标不等于立刻放弃：</b>只要记忆还新鲜，AI 会朝着最后已知位置继续逼近，
    /// 这段时间正是玩家"绕到侧面试探"的紧张窗口。</para>
    /// </remarks>
    public sealed class EngageState : IState<AiContext>
    {
        private float m_ReactionRemaining;
        private float m_BurstRemaining;
        private float m_PauseRemaining;

        /// <inheritdoc />
        public AiStateId Id
        {
            get { return AiStateId.Engage; }
        }

        /// <inheritdoc />
        public void Enter(AiContext context)
        {
            var profile = context.Profile;
            m_ReactionRemaining = profile.ReactionSeconds;
            m_BurstRemaining = profile.FireBurstSeconds;
            m_PauseRemaining = 0f;
        }

        /// <inheritdoc />
        public void Exit(AiContext context)
        {
            // 离开交战时必须清掉开火意图：否则迁移到撤退的那一帧还会打出一发，
            // 看起来像 AI 一边逃跑一边回头放冷枪。
            context.Intent.WantsToFire = false;
        }

        /// <inheritdoc />
        public AiTransition Tick(AiContext context, float deltaTime)
        {
            var snapshot = context.Snapshot;
            var profile = context.Profile;
            var self = context.Self;

            if (self.HealthRatio <= profile.RetreatHealthRatio)
            {
                return new AiTransition(AiStateId.Retreat, "生命过低");
            }

            if (snapshot.SeesTarget)
            {
                context.RememberThreat(snapshot.Target.Position);
            }
            else if (!context.HasFreshMemory)
            {
                // 既看不见、记忆也过期了，说明彻底跟丢。此时回到巡逻，
                // 而不是进入"无线索的调查"——那只会让日志多出一条没有信息量的迁移。
                return new AiTransition(AiStateId.Patrol, "目标丢失且线索中断");
            }

            if (!context.TryResolveThreatPosition(out var threatPosition))
            {
                return new AiTransition(AiStateId.Patrol, "没有可交战的目标");
            }

            var toThreat = threatPosition - self.Position;
            var distance = toThreat.Magnitude;
            if (distance > 1e-3f)
            {
                context.Intent.Facing = toThreat / distance;
            }

            UpdatePositioning(context, threatPosition, distance);
            UpdateFireControl(context, deltaTime, distance);
            return AiTransition.None;
        }

        /// <summary>调整站位：走到"距目标期望距离"的位置上。</summary>
        private void UpdatePositioning(AiContext context, Vector2F threatPosition, float distance)
        {
            var profile = context.Profile;
            if (distance <= 1e-3f)
            {
                return;
            }

            var directionToThreat = (threatPosition - context.Self.Position) / distance;
            var desired = threatPosition - (directionToThreat * profile.PreferredEngageDistance);

            // 容差的一半作为移动死区：太敏感会让 AI 在期望距离附近反复微调，
            // 在俯视角下看起来像在原地抽搐。
            if (Vector2F.Distance(context.Self.Position, desired) <= profile.EngageDistanceTolerance * 0.5f)
            {
                return;
            }

            context.Intent.HasMoveGoal = true;
            context.Intent.MoveGoal = desired;
        }

        /// <summary>推进反应时间与点射节奏，决定本帧是否开火。</summary>
        private void UpdateFireControl(AiContext context, float deltaTime, float distance)
        {
            var profile = context.Profile;

            if (m_ReactionRemaining > 0f)
            {
                m_ReactionRemaining -= deltaTime;
            }

            if (m_PauseRemaining > 0f)
            {
                m_PauseRemaining -= deltaTime;
                if (m_PauseRemaining <= 0f)
                {
                    m_BurstRemaining = profile.FireBurstSeconds;
                }
            }
            else
            {
                m_BurstRemaining -= deltaTime;
                if (m_BurstRemaining <= 0f)
                {
                    m_PauseRemaining = profile.FirePauseSeconds;
                }
            }

            if (!context.Snapshot.SeesTarget || m_ReactionRemaining > 0f || m_PauseRemaining > 0f)
            {
                return;
            }

            if (distance > context.Self.WeaponRangeMeters)
            {
                // 超出射程不打：既打不中，又把"AI 在浪费子弹"这种糊涂账留给调试。
                return;
            }

            // 用**实际朝向**而不是意图朝向做判定：意图只是"想瞄哪"，
            // 用意图判定等于允许 AI 在枪还没转过来时就开火，瞄准容差会形同虚设。
            var aimDegrees = context.Self.FacingDegrees;
            var targetDegrees = AiAngles.ToDegrees(context.Snapshot.Target.Position - context.Self.Position);
            if (!AiAngles.IsWithinTolerance(aimDegrees, targetDegrees, profile.AimToleranceDegrees))
            {
                return;
            }

            context.Intent.WantsToFire = true;
        }
    }
}
