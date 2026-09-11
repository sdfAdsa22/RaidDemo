using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 巡逻状态：沿路径点移动，并在每个路径点停下来环视。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么巡逻要"走一段、停一下环视"：</b>一直朝前走的 AI 背后是永久盲区，
    /// 玩家只要从后方跟随就能安全通过，敌人等于不存在。停下环视让视野真的扫过四周，
    /// 也让玩家有"等它转头再行动"的可读节奏。</para>
    ///
    /// <para><b>优先级顺序是刻意的：</b>看到目标 &gt; 挨打 &gt; 听到动静。
    /// 直接证据永远优先于间接证据，否则会出现"AI 明明看见人，却被墙后的噪音引走"。</para>
    /// </remarks>
    public sealed class PatrolState : IState<AiContext>
    {
        private bool m_Scanning;
        private float m_ScanRemaining;

        /// <summary>环视时的当前朝向角度（度）。连续累加，因此转向是平滑的。</summary>
        private float m_ScanDegrees;

        /// <inheritdoc />
        public AiStateId Id
        {
            get { return AiStateId.Patrol; }
        }

        /// <inheritdoc />
        public void Enter(AiContext context)
        {
            m_Scanning = false;
            m_ScanRemaining = 0f;
        }

        /// <inheritdoc />
        public void Exit(AiContext context)
        {
            // 离开巡逻时清掉环视进度：否则下次回到巡逻会接着上次的角度继续转，
            // 表现为 AI 刚回来就歪着头看向某个固定方向。
            m_Scanning = false;
            m_ScanRemaining = 0f;
        }

        /// <inheritdoc />
        public AiTransition Tick(AiContext context, float deltaTime)
        {
            var snapshot = context.Snapshot;

            if (snapshot.SeesTarget)
            {
                context.RememberThreat(snapshot.Target.Position);
                return new AiTransition(AiStateId.Engage, "巡逻中发现目标");
            }

            if (snapshot.WasDamaged)
            {
                context.RememberThreat(snapshot.DamageSourcePosition);
                return new AiTransition(AiStateId.Engage, "巡逻中遭到攻击");
            }

            if (snapshot.SuspectedTarget)
            {
                return new AiTransition(AiStateId.Alert, "巡逻中发现可疑目标");
            }

            if (snapshot.HeardNoise)
            {
                context.RememberThreat(snapshot.NoisePosition);
                return new AiTransition(AiStateId.Investigate, "巡逻中听到动静");
            }

            AdvancePatrol(context, deltaTime);
            return AiTransition.None;
        }

        /// <summary>推进"前往路径点"与"到达后环视"两段行为。</summary>
        private void AdvancePatrol(AiContext context, float deltaTime)
        {
            var self = context.Self;

            if (self.PatrolRoute == null || self.PatrolRoute.IsEmpty)
            {
                // 没有路线的 AI 原地环视。这比报错更有用：装配漏了一条路线时，
                // 场景里仍然能看到一个在观察四周的敌人，问题一眼可见。
                ContinueScanning(context, deltaTime, restartWhenDone: true);
                return;
            }

            if (m_Scanning)
            {
                m_ScanRemaining -= deltaTime;
                if (m_ScanRemaining > 0f)
                {
                    RotateInPlace(context, deltaTime);
                    return;
                }

                m_Scanning = false;
                self.PatrolRoute.Advance();
            }

            var waypoint = self.PatrolRoute.GetCurrent(self.Position);
            if (Vector2F.Distance(self.Position, waypoint) <= context.Profile.WaypointReachDistance)
            {
                BeginScan(context);
                RotateInPlace(context, deltaTime);
                return;
            }

            context.Intent.HasMoveGoal = true;
            context.Intent.MoveGoal = waypoint;
        }

        /// <summary>开始一次环视，以当前朝向为起点。</summary>
        private void BeginScan(AiContext context)
        {
            m_Scanning = true;
            m_ScanRemaining = context.Profile.WaypointScanSeconds;

            // 以当前朝向作为扫描起点：从"它正在看的方向"开始转，
            // 视觉上比突然跳到某个固定角度自然得多。
            m_ScanDegrees = context.Self.FacingDegrees;
        }

        /// <summary>无路线时的持续环视：扫完一轮立刻开始下一轮。</summary>
        private void ContinueScanning(AiContext context, float deltaTime, bool restartWhenDone)
        {
            if (!m_Scanning)
            {
                BeginScan(context);
            }
            else
            {
                m_ScanRemaining -= deltaTime;
                if (m_ScanRemaining <= 0f && restartWhenDone)
                {
                    BeginScan(context);
                }
            }

            RotateInPlace(context, deltaTime);
        }

        /// <summary>按扫描速度连续转动朝向。</summary>
        private void RotateInPlace(AiContext context, float deltaTime)
        {
            m_ScanDegrees += context.Profile.ScanTurnSpeedDegreesPerSecond * deltaTime;
            context.Intent.Facing = Vector2F.FromDegrees(m_ScanDegrees);
        }
    }
}
