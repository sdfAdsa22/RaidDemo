using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 警惕状态：发现可疑动静（6~9 米内的目标），停下当前动作、转向并缓慢逼近，观察一段时间。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要这个状态，而不是"看见就进入交战"：</b>如果 9 米内一律直接开火，
    /// 玩家在远处刚露头就会被射击，没有任何反应余地；而如果 9 米外一律看不见，
    /// AI 又变成了只能被动挨打的靶子。警惕状态填的正是中间那段：
    /// **AI 会表现出"我好像看到什么了"，但还没动手。**</para>
    ///
    /// <para><b>它对玩家的价值是"可读"：</b>玩家能看到 AI 停下来转向自己，
    /// 于是有 5 秒时间决定是躲进掩体、后退，还是抢先开枪。这比一个隐藏的数值好得多。</para>
    ///
    /// <para><b>这段时间 AI 不开火。</b>开火只发生在交战状态，而交战需要：
    /// 目标进入必定发现距离，或者警惕观察满 <see cref="AIPerceptionProfile.AlertConfirmSeconds"/> 秒。</para>
    /// </remarks>
    public sealed class AlertState : IState<AiContext>
    {
        /// <summary>占位容差（米）：站位死区，避免 AI 在保持距离附近反复微调。</summary>
        private const float HoldTolerance = 0.5f;

        private float m_ObservedSeconds;

        /// <inheritdoc />
        public AiStateId Id
        {
            get { return AiStateId.Alert; }
        }

        /// <inheritdoc />
        public void Enter(AiContext context)
        {
            m_ObservedSeconds = 0f;
        }

        /// <inheritdoc />
        public void Exit(AiContext context)
        {
            // 离开警惕时清掉开火意图：这个状态本身从不开火，
            // 但清一次可以防止后续新增逻辑时把意图泄漏到别的状态。
            context.Intent.WantsToFire = false;
        }

        /// <inheritdoc />
        public AiTransition Tick(AiContext context, float deltaTime)
        {
            var snapshot = context.Snapshot;
            var profile = context.Profile;

            if (snapshot.SeesTarget)
            {
                context.RememberThreat(snapshot.Target.Position);
                return new AiTransition(AiStateId.Engage, "目标进入必定发现距离");
            }

            if (snapshot.WasDamaged)
            {
                context.RememberThreat(snapshot.DamageSourcePosition);
                return new AiTransition(AiStateId.Engage, "警惕中遭到攻击");
            }

            if (!snapshot.SuspectedTarget)
            {
                // 目标脱离视线。如果还记得它在哪，就走过去查看——直接回巡逻的话，
                // 玩家只要侧身躲进掩体就能让 AI 当无事发生，警惕会变得毫无意义。
                return context.HasFreshMemory
                    ? new AiTransition(AiStateId.Investigate, "可疑目标脱离视线，前去查看")
                    : new AiTransition(AiStateId.Patrol, "可疑目标已脱离");
            }

            context.RememberThreat(snapshot.Target.Position);

            m_ObservedSeconds += deltaTime;
            if (m_ObservedSeconds >= profile.AlertConfirmSeconds)
            {
                return new AiTransition(AiStateId.Engage, "警惕确认：目标持续停留");
            }

            UpdateStance(context);
            return AiTransition.None;
        }

        /// <summary>面向目标，并缓慢逼近到"警惕保持距离"。</summary>
        private void UpdateStance(AiContext context)
        {
            var self = context.Self;
            var profile = context.Profile;

            if (!context.TryResolveThreatPosition(out var threatPosition))
            {
                return;
            }

            var toThreat = threatPosition - self.Position;
            var distance = toThreat.Magnitude;
            if (distance <= 1e-3f)
            {
                return;
            }

            var direction = toThreat / distance;
            context.Intent.Facing = direction;

            // 只在比保持距离更远时才靠近：站在 7 米左右盯着玩家，落在 6~9 米的警惕区间内。
            // 逼到 6 米以内会自动升级为"必定发现"，这个状态就结束了。
            if (distance <= profile.AlertHoldDistance + HoldTolerance)
            {
                return;
            }

            context.Intent.HasMoveGoal = true;
            context.Intent.MoveGoal = threatPosition - (direction * profile.AlertHoldDistance);
        }
    }
}
