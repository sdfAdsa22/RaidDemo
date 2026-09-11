namespace RaidDemo.AI
{
    /// <summary>
    /// AI 的状态标识。
    /// </summary>
    /// <remarks>
    /// <para>用枚举而不是"状态对象互相持有引用"来表达迁移目标，
    /// 好处是状态机可以在迁移发生时记录一条可读的日志（谁变成了谁、为什么），
    /// 而对象图里的迁移是无声的：出了问题只能靠断点猜。</para>
    ///
    /// <para>四个状态与主文档 7.4 节的规划一致：
    /// 巡逻 → 调查 → 交战 → 撤退。</para>
    /// </remarks>
    public enum AiStateId
    {
        /// <summary>巡逻：沿路径点移动，低频扫描。</summary>
        Patrol = 0,

        /// <summary>调查：前往声源或最后目击点搜索。</summary>
        Investigate,

        /// <summary>交战：保持距离、瞄准、射击。</summary>
        Engage,

        /// <summary>撤退：远离威胁并恢复生命。</summary>
        Retreat,
    }

    /// <summary>
    /// 一次状态迁移请求：去哪，以及为什么。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么迁移要带理由：</b>AI 出问题时的第一句话永远是"它为什么不打人"。
    /// 只有目的状态的话，只能看到"它现在在巡逻"；带上理由之后，
    /// 日志会直接说明它是在"调查超时"之后回到巡逻的，还是压根没看见过目标。</para>
    ///
    /// <para>默认值（<see cref="None"/>）表示"本帧不迁移"，这是状态的常规返回值。</para>
    /// </remarks>
    public readonly struct AiTransition
    {
        /// <summary>不迁移。</summary>
        public static readonly AiTransition None = default;

        /// <summary>请求迁移到指定状态。</summary>
        /// <param name="target">目标状态。</param>
        /// <param name="reason">迁移理由，用于日志与调试。</param>
        public AiTransition(AiStateId target, string reason)
        {
            Target = target;
            Reason = reason;
        }

        /// <summary>目标状态；为 null 表示不迁移。</summary>
        public AiStateId? Target { get; }

        /// <summary>迁移理由。</summary>
        public string Reason { get; }

        /// <summary>是否请求了迁移。</summary>
        public bool HasTarget
        {
            get { return Target.HasValue; }
        }

        public override string ToString()
        {
            return HasTarget ? $"→{Target.Value}（{Reason}）" : "保持当前状态";
        }
    }
}
