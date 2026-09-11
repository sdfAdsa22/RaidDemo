using RaidDemo.Kernel;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 一局战局的会话：计时、击杀统计与结局判定。
    /// </summary>
    /// <remarks>
    /// <para><b>它是战局状态的唯一权威。</b>界面不自己数倒计时，也不自己判断输赢，
    /// 全部以这里的结果为准。这条规则与项目其它模块一致：
    /// 表现层只呈现权威状态，不参与计算。</para>
    ///
    /// <para>纯 C# 实现，不依赖 UnityEngine：它要能在 EditMode 测试里直接构造。
    /// 时间由外部按 deltaTime 推进，而不是读引擎的 Time.deltaTime。</para>
    /// </remarks>
    public sealed class RaidSession
    {
        private readonly RaidSettings m_Settings;
        private readonly EventBus m_EventBus;
        private readonly int m_PlayerCombatantId;

        /// <summary>创建战局会话。</summary>
        /// <param name="settings">节奏参数，不允许为 null。</param>
        /// <param name="eventBus">事件总线，不允许为 null。</param>
        /// <param name="playerCombatantId">玩家的战斗单位标识；击杀统计只认这个攻击方。</param>
        public RaidSession(RaidSettings settings, EventBus eventBus, int playerCombatantId)
        {
            m_Settings = settings ?? new RaidSettings();
            m_EventBus = eventBus;
            m_PlayerCombatantId = playerCombatantId;
            Outcome = RaidOutcome.InProgress;
        }

        /// <summary>当前结局。</summary>
        public RaidOutcome Outcome { get; private set; }

        /// <summary>战局是否仍在进行。</summary>
        public bool IsActive
        {
            get { return Outcome == RaidOutcome.InProgress; }
        }

        /// <summary>已经过去的时长（秒）。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>剩余时长（秒），不会小于 0。</summary>
        public float RemainingSeconds
        {
            get
            {
                var remaining = m_Settings.RaidDurationSeconds - ElapsedSeconds;
                return remaining > 0f ? remaining : 0f;
            }
        }

        /// <summary>玩家击杀数。</summary>
        public int Kills { get; private set; }

        /// <summary>本局带入的装备总价值。撤离与损失的对比要靠它。</summary>
        public int BroughtInValue { get; private set; }

        /// <summary>
        /// 开始这一局。
        /// </summary>
        /// <param name="broughtInValue">带入装备的总价值。</param>
        /// <remarks>
        /// 带入价值必须在这里一次性记录：开打之后背包里会混入搜刮到的东西，
        /// 那时再回头统计就分不清「本来就有」与「刚捡到」了。
        /// </remarks>
        public void Start(int broughtInValue)
        {
            BroughtInValue = broughtInValue;
            ElapsedSeconds = 0f;
            Kills = 0;
            Outcome = RaidOutcome.InProgress;
            m_EventBus?.Publish(new RaidStartedEvent(m_PlayerCombatantId, m_Settings.RaidDurationSeconds));
        }

        /// <summary>推进计时。</summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <remarks>
        /// 已经结束的会话再调用本方法是安全的空操作：
        /// 结算界面冻结世界之后主循环可能还会多跑几帧，那时不应触发第二次结算。
        /// </remarks>
        public void Tick(float deltaTime)
        {
            if (!IsActive || deltaTime <= 0f)
            {
                return;
            }

            ElapsedSeconds += deltaTime;
            if (ElapsedSeconds >= m_Settings.RaidDurationSeconds)
            {
                ElapsedSeconds = m_Settings.RaidDurationSeconds;
                Finish(RaidOutcome.TimeExpired);
            }
        }

        /// <summary>登记一次击杀。</summary>
        /// <param name="attackerCombatantId">攻击方标识。</param>
        /// <remarks>
        /// 只统计玩家造成的击杀。让 AI 之间的误伤也计入，结算上的击杀数就不再可信。
        /// </remarks>
        public void NotifyKill(int attackerCombatantId)
        {
            if (!IsActive || attackerCombatantId != m_PlayerCombatantId)
            {
                return;
            }

            Kills++;
        }

        /// <summary>玩家阵亡，本局以失败结束。</summary>
        public void NotifyPlayerKilled()
        {
            Finish(RaidOutcome.Killed);
        }

        /// <summary>撤离完成，本局以成功结束。</summary>
        public void NotifyExtracted()
        {
            Finish(RaidOutcome.Extracted);
        }

        /// <summary>结束战局并广播结果。重复调用不会重复广播。</summary>
        private void Finish(RaidOutcome outcome)
        {
            if (!IsActive)
            {
                return;
            }

            Outcome = outcome;
            m_EventBus?.Publish(new RaidEndedEvent(outcome, ElapsedSeconds, Kills));
        }
    }
}
