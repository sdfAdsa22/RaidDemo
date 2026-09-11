namespace RaidDemo.Raid
{
    /// <summary>
    /// 一局战局的结局。
    /// </summary>
    /// <remarks>
    /// <para>取值刻意只有三种终态：撤离成功、阵亡、时间耗尽。
    /// 每一种都对应结算界面上的一句明确结论，玩家不需要猜自己是怎么结束的。</para>
    ///
    /// <para><see cref="InProgress"/> 放在 0 号位：默认构造的枚举值就是「还没结束」，
    /// 这样任何忘记初始化的字段都不会意外表现为「已经撤离成功」。
    /// 新增结局时**必须追加到末尾**，否则已经序列化的数据会整体错位。</para>
    /// </remarks>
    public enum RaidOutcome
    {
        /// <summary>战局进行中。</summary>
        InProgress = 0,

        /// <summary>成功撤离：带出的东西归玩家所有（M6 起接入仓库）。</summary>
        Extracted = 1,

        /// <summary>阵亡：本次携带的物品全部损失。</summary>
        Killed = 2,

        /// <summary>时间耗尽：没来得及撤离，等同于损失。</summary>
        TimeExpired = 3,
    }
}
