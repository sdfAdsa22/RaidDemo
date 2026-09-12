using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 一个 AI 单位的完整运行时：位置、朝向、生命、武器、状态机与感知记忆。
    /// </summary>
    /// <remarks>
    /// <para><b>它是纯逻辑对象，不继承 MonoBehaviour、不持有任何场景引用。</b>
    /// 表现层只订阅它广播的事件并把结果画出来，因此同一个 AI 既可以跑在客户端，
    /// 也可以跑在无头服务端（主文档 5.6 节约束一）。</para>
    ///
    /// <para><b>每帧的顺序是固定的：</b>先感知（写快照）→ 再决策（状态机写意图）→
    /// 后执行（移动与武器）。这个顺序保证状态读到的一定是**本帧**的信息，
    /// 而不是上一帧的残留值——顺序颠倒会让 AI 对玩家的位置永远慢一帧，
    /// 表现为"瞄准总是差一点"。</para>
    ///
    /// <para><b>外部事件（噪音、受击）先入队、下一帧统一消费：</b>事件可能在任何时刻到达，
    /// 而 AI 的决策只在 Tick 里发生。入队保证了「一次 Tick 对应一次完整决策」，
    /// 否则同一帧内连续两声噪音会让状态机在一帧里迁移两次，日志与表现都会跳帧。</para>
    /// </remarks>
    public sealed partial class AiAgent
    {
        private readonly AiDirector m_Director;
        private readonly CombatantState m_Combatant;
        private readonly AiWeaponController m_Weapon;
        private readonly AiMovement m_Movement;
        private readonly StateMachine<AiContext> m_Machine;
        private readonly AiPerceptionSnapshot m_Snapshot = new AiPerceptionSnapshot();
        private readonly AiIntent m_Intent = new AiIntent();
        private readonly AISensesMemory m_Memory = new AISensesMemory();

        private Vector2F m_Position;
        private float m_FacingDegrees;
        private AiStateId m_LastReportedState = AiStateId.Patrol;

        private bool m_PendingNoise;
        private Vector2F m_PendingNoisePosition;
        private NoiseTier m_PendingNoiseTier;

        private bool m_PendingDamage;
        private Vector2F m_PendingDamageSource;
        private int m_PendingDamageSourceId;

        /// <summary>
        /// 创建一个 AI 单位。
        /// </summary>
        /// <param name="director">所属调度器，提供共享服务与参数。</param>
        /// <param name="combatant">战斗层中的单位状态（生命与护甲）。</param>
        /// <param name="weapon">武器参数。</param>
        /// <param name="route">巡逻路线，可为 null。</param>
        /// <param name="spawnPosition">出生位置。</param>
        /// <param name="spawnFacingDegrees">出生朝向（度）。</param>
        /// <param name="randomSeed">散布用的随机种子。固定的种子让 AI 行为可复现。</param>
        /// <param name="reserveAmmo">初始备弹总数。</param>
        public AiAgent(
            AiDirector director,
            CombatantState combatant,
            AiWeaponProfile weapon,
            PatrolRoute route,
            Vector2F spawnPosition,
            float spawnFacingDegrees,
            uint randomSeed,
            int reserveAmmo = 90)
        {
            m_Director = director ?? throw new ArgumentNullException(nameof(director));
            m_Combatant = combatant ?? throw new ArgumentNullException(nameof(combatant));

            var weaponProfile = weapon ?? AiWeaponProfile.CreateGreyboxRifle();
            PatrolRoute = route ?? new PatrolRoute();
            m_Position = spawnPosition;
            m_FacingDegrees = AiAngles.Normalize(spawnFacingDegrees);

            m_Weapon = new AiWeaponController(
                m_Director,
                m_Combatant.Id,
                weaponProfile,
                randomSeed,
                reserveAmmo);

            m_Movement = new AiMovement(m_Director.Pathfinding, m_Director.Profile)
            {
                Bounds = m_Director.Bounds,
            };

            Context = new AiContext(this, m_Memory, m_Director.Profile);
            m_Machine = new StateMachine<AiContext>(
                Context,
                new IState<AiContext>[]
                {
                    new PatrolState(),
                    new AlertState(),
                    new InvestigateState(),
                    new EngageState(),
                    new RetreatState(),
                },
                OnStateChanged);

            m_Machine.Start(AiStateId.Patrol);
        }

        /// <summary>状态共享的上下文。测试可以直接构造它以驱动单个状态。</summary>
        public AiContext Context { get; }

        /// <summary>巡逻路线。</summary>
        public PatrolRoute PatrolRoute { get; }

        /// <summary>本帧的感知输入。</summary>
        public AiPerceptionSnapshot Snapshot
        {
            get { return m_Snapshot; }
        }

        /// <summary>本帧的行为意图。</summary>
        public AiIntent Intent
        {
            get { return m_Intent; }
        }

        /// <summary>战斗层中的单位标识。</summary>
        public int CombatantId
        {
            get { return m_Combatant.Id; }
        }

        /// <summary>当前状态。</summary>
        public AiStateId CurrentState
        {
            get { return m_Machine.CurrentId; }
        }

        /// <summary>当前状态已持续的时长（秒）。</summary>
        public float TimeInState
        {
            get { return m_Machine.TimeInState; }
        }

        /// <summary>是否存活。</summary>
        public bool IsAlive
        {
            get { return m_Combatant.IsAlive; }
        }

        /// <summary>当前生命值。</summary>
        public float Health
        {
            get { return m_Combatant.Health; }
        }

        /// <summary>生命上限。</summary>
        public float MaxHealth
        {
            get { return m_Combatant.MaxHealth; }
        }

        /// <summary>生命比例，取值 0 到 1。</summary>
        public float HealthRatio
        {
            get
            {
                var max = m_Combatant.MaxHealth;
                return max > 0f ? m_Combatant.Health / max : 0f;
            }
        }

        /// <summary>平面位置。</summary>
        public Vector2F Position
        {
            get { return m_Position; }
        }

        /// <summary>朝向（单位向量）。</summary>
        public Vector2F Facing
        {
            get { return Vector2F.FromDegrees(m_FacingDegrees); }
        }

        /// <summary>朝向角度（度）。</summary>
        public float FacingDegrees
        {
            get { return m_FacingDegrees; }
        }

        /// <summary>当前武器射程（米）。</summary>
        public float WeaponRangeMeters
        {
            get { return m_Weapon.RangeMeters; }
        }

        /// <summary>当前弹匣余量。用于调试与界面。</summary>
        public int MagazineAmmo
        {
            get { return m_Weapon.MagazineAmmo; }
        }

        /// <summary>剩余备弹。</summary>
        public int ReserveAmmo
        {
            get { return m_Weapon.ReserveAmmo; }
        }

        /// <summary>
        /// 推进一帧。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="now">战局内的单调时刻（秒）。</param>
        /// <param name="target">当前目标快照。</param>
        public void Tick(float deltaTime, float now, in AiTargetInfo target)
        {
            if (!IsAlive || deltaTime <= 0f)
            {
                return;
            }

            UpdateSnapshot(now, target);

            m_Intent.Clear();
            m_Machine.Tick(deltaTime);

            ApplyIntent(deltaTime);

            // 武器推进放在最后：本帧的移动与转向已经生效，
            // 因此子弹是从"这一帧实际所在的位置与朝向"打出去的。
            m_Weapon.Tick(deltaTime, m_Intent.WantsToFire, m_Position, m_FacingDegrees);
        }

        /// <summary>接收一次噪音刺激，下一帧统一消费。</summary>
        public void EnqueueNoise(Vector2F position, NoiseTier tier)
        {
            m_PendingNoise = true;
            m_PendingNoisePosition = position;
            m_PendingNoiseTier = tier;
        }

        /// <summary>接收一次受击通知，下一帧统一消费。</summary>
        public void NotifyDamaged(Vector2F sourcePosition, int sourceId)
        {
            m_PendingDamage = true;
            m_PendingDamageSource = sourcePosition;
            m_PendingDamageSourceId = sourceId;
        }

        /// <summary>
        /// 恢复生命。
        /// </summary>
        /// <param name="amount">本帧想要恢复的量。</param>
        /// <returns>实际恢复量。</returns>
        public float Heal(float amount)
        {
            return m_Combatant.Heal(amount);
        }

        /// <summary>刷新本帧的感知快照。</summary>
        private void UpdateSnapshot(float now, in AiTargetInfo target)
        {
            m_Snapshot.ClearTransient();
            m_Snapshot.Time = now;
            m_Snapshot.Target = target;

            if (m_PendingNoise)
            {
                m_Snapshot.HeardNoise = true;
                m_Snapshot.NoisePosition = m_PendingNoisePosition;
                m_Snapshot.NoiseTier = m_PendingNoiseTier;
                m_PendingNoise = false;
            }

            if (m_PendingDamage)
            {
                m_Snapshot.WasDamaged = true;
                m_Snapshot.DamageSourcePosition = m_PendingDamageSource;
                m_Snapshot.DamageSourceId = m_PendingDamageSourceId;
                m_PendingDamage = false;
            }

            if (!target.Exists)
            {
                // 目标不存在（阵亡或撤离）时清空记忆：否则 AI 会一直去调查一个空位置。
                // 两个视觉标记由 ClearTransient 统一清掉，这里不必重复写。
                m_Memory.Clear();
                return;
            }

            var profile = m_Director.Profile;
            var tier = profile.ClassifySighting(m_Position, Facing, target.Position);

            // 默认按"非视觉来源"计反应时间（遭到攻击等）。下面的分档会覆盖它。
            m_Snapshot.EngagementReactionSeconds = profile.ReactionSeconds;
            if (tier == SightingTier.None)
            {
                return;
            }

            // 两档都要做遮挡判定：6 米内可以忽略朝向，但隔着集装箱不算发现。
            //
            // 只有射手这一端需要补高度：AI 的 m_Position 是纯平面坐标。
            //
            // 目标中心**不能**再补一次：CenterWorld 来自战斗层的世界坐标（站在 1.25 米平台上
            // 的单位，中心就是 2.10 米）。曾经两端都补过一次，结果是射线瞄到目标头顶上方
            // 1.25 米处、什么也打不到，视线恒为 false，敌人永远停在"警惕"。
            var eyeGround = m_Director.GroundHeight?.SampleHeight(m_Position) ?? 0f;
            var hasLineOfSight = AISensor.HasLineOfSight(
                m_Director.Probe,
                AISensor.ToEyePosition(m_Position, profile.EyeHeightMeters, eyeGround),
                target.CenterWorld,
                target.CombatantId,
                profile.ViewDistanceMeters);

            if (tier == SightingTier.Guaranteed)
            {
                m_Snapshot.SeesTarget = hasLineOfSight;
                m_Snapshot.EngagementReactionSeconds = profile.GuaranteedReactionSeconds;
            }
            else
            {
                m_Snapshot.SuspectedTarget = hasLineOfSight;

                // 警惕确认后进入交战是"观察已经完成"的结果，因此立刻开火，不再叠加反应时间。
                m_Snapshot.EngagementReactionSeconds = 0f;
            }

            if (m_Snapshot.SeesTarget || m_Snapshot.SuspectedTarget)
            {
                // 警惕也要刷新记忆：AI 虽然还不确定，但它知道你在那个位置。
                // 少了这一步，玩家侧身躲进掩体后 AI 会立刻"忘记"，警惕就失去了追查的意义。
                m_Memory.Remember(target.Position, now);
            }
        }

        /// <summary>状态变化回调：广播事件，供表现层与日志使用。</summary>
        private void OnStateChanged(AiStateId state, string reason)
        {
            m_Director.EventBus?.Publish(new AiStateChangedEvent(
                m_Combatant.Id,
                m_LastReportedState,
                state,
                reason,
                m_Position,
                m_Snapshot.Time));

            m_LastReportedState = state;
        }
    }
}
