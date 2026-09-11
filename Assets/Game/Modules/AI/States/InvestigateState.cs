using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 调查状态：前往声源或最后目击点，四处搜索，超时后回到巡逻。
    /// </summary>
    /// <remarks>
    /// <para><b>这个状态存在的意义是"给玩家制造压力但不给 AI 作弊"：</b>
    /// AI 并不知道玩家现在在哪，它只知道"刚才那儿有动静"。
    /// 玩家因此可以靠转移位置来摆脱追踪——这正是搜打撤里"躲一躲再走"的乐趣所在。</para>
    ///
    /// <para><b>新的噪音会重置调查进度</b>：否则 AI 走到一半时听到新动静，
    /// 会因为"总时长超时"而放弃，表现为玩家在附近跑来跑去 AI 却视而不见。</para>
    /// </remarks>
    public sealed class InvestigateState : IState<AiContext>
    {
        private float m_Elapsed;
        private bool m_Arrived;
        private float m_ScanDegrees;

        /// <inheritdoc />
        public AiStateId Id
        {
            get { return AiStateId.Investigate; }
        }

        /// <inheritdoc />
        public void Enter(AiContext context)
        {
            m_Elapsed = 0f;
            m_Arrived = false;
            m_ScanDegrees = context.Self.FacingDegrees;
        }

        /// <inheritdoc />
        public void Exit(AiContext context)
        {
            m_Arrived = false;
        }

        /// <inheritdoc />
        public AiTransition Tick(AiContext context, float deltaTime)
        {
            var snapshot = context.Snapshot;

            if (snapshot.SeesTarget)
            {
                context.RememberThreat(snapshot.Target.Position);
                return new AiTransition(AiStateId.Engage, "调查中确认目标");
            }

            if (snapshot.WasDamaged)
            {
                context.RememberThreat(snapshot.DamageSourcePosition);
                return new AiTransition(AiStateId.Engage, "调查中遭到攻击");
            }

            if (snapshot.HeardNoise)
            {
                // 新动静覆盖旧线索，并把调查计时与到达标记一并重置。
                context.RememberThreat(snapshot.NoisePosition);
                m_Elapsed = 0f;
                m_Arrived = false;
            }

            m_Elapsed += deltaTime;
            if (m_Elapsed >= context.Profile.InvestigateSeconds)
            {
                return new AiTransition(AiStateId.Patrol, "调查超时");
            }

            if (context.Memory == null || !context.Memory.HasMemory)
            {
                // 没有任何线索可查。理论上进入调查时一定有线索，
                // 但记忆可能因为过期被其它状态清空，这里兜底回巡逻而不是原地发呆。
                return new AiTransition(AiStateId.Patrol, "线索已失效");
            }

            var target = context.Memory.LastKnownPosition;
            var distance = Vector2F.Distance(context.Self.Position, target);

            if (!m_Arrived && distance <= context.Profile.WaypointReachDistance)
            {
                m_Arrived = true;
                m_ScanDegrees = context.Self.FacingDegrees;
            }

            if (m_Arrived)
            {
                // 到了线索点就原地转圈搜索：这里才是"调查"与"巡逻"的区别所在。
                m_ScanDegrees += context.Profile.ScanTurnSpeedDegreesPerSecond * deltaTime;
                context.Intent.Facing = Vector2F.FromDegrees(m_ScanDegrees);
                return AiTransition.None;
            }

            context.Intent.HasMoveGoal = true;
            context.Intent.MoveGoal = target;

            // 移动过程中面向移动方向即可，朝向由 Agent 按移动方向推导。
            return AiTransition.None;
        }
    }
}
