namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 敌人同步使用的命名消息通道。
    /// </summary>
    /// <remarks>
    /// <para>只有下行一条：敌人完全由服务器驱动，客户端没有需要上行表达的东西——
    /// 玩家对敌人做的一切（开枪、被看见）都已经走在移动输入与战斗请求上了。</para>
    ///
    /// <para>与 <see cref="MovementNetworkChannel"/> 分开成一个通道，是因为两者的"数据主人"不同：
    /// 玩家快照的真身在客户端的预测与和解里（那里要用它回滚重放），
    /// 而敌人快照只进插值缓冲。混在一个通道里会让"订阅者要不要处理这条消息"变成运行时判断。</para>
    /// </remarks>
    public static class EnemyNetworkChannel
    {
        /// <summary>服务器 → 全体客户端：一批敌人快照。</summary>
        public const string SnapshotMessageName = "RaidDemo.Enemy.Snapshot";
    }
}
