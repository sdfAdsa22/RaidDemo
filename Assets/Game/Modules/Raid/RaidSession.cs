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

        /// <summary>
        /// 本局是否由服务器裁定（联机时置 true）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么需要这个开关（`RD-AUD-041`）：</b>联机时客户端这一份只是**展示用的镜像**：
        /// 它照旧按 <see cref="Tick"/> 推进本地时钟（HUD 的倒计时要有东西可显示），
        /// 但**不能自己把这一局结束掉**——否则会出现"客户端已经弹结算面板、服务器还在跑"的分裂，
        /// 而结算里的价值、击杀、用时又都以服务器为准，两边自然对不上。</para>
        ///
        /// <para>置 true 之后，结束只有两条来路：服务器下发的结算消息
        /// （走 <see cref="NotifyExtracted"/> / <see cref="NotifyPlayerKilled"/>），
        /// 或者离开这一局。倒计时到点时客户端只是把时间停在 0，等服务器说结束。</para>
        /// </remarks>
        public bool IsServerAuthoritative { get; set; }

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

                // 联机时不自行收尾（RD-AUD-041）：本地时钟停在上限，等服务器的裁定。
                if (!IsServerAuthoritative)
                {
                    Finish(RaidOutcome.TimeExpired);
                }
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

        /// <summary>本局因时间耗尽结束（服务器裁定，联机时使用）。</summary>
        /// <remarks>
        /// 三种结局必须分开：以前只有"撤离"与"阵亡"两个入口，
        /// 于是服务器下发的"时间耗尽"被客户端当成阵亡显示——结算面板会写错一句结论
        /// （2026-09-15 真机验收发现）。
        /// </remarks>
        public void NotifyTimeExpired()
        {
            Finish(RaidOutcome.TimeExpired);
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
