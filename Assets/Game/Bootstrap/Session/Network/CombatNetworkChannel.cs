namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战斗同步使用的命名消息通道。
    /// </summary>
    /// <remarks>
    /// 上行方向复用移动输入消息（扣扳机与换弹请求搭在它上面，见
    /// <see cref="PlayerInputMessage"/> 的说明），因此这里只有下行一条：
    /// 服务器把结算结果广播给所有客户端。
    /// </remarks>
    public static class CombatNetworkChannel
    {
        /// <summary>服务器 → 全体客户端：一条战斗事件（开火 / 伤害 / 摧毁 / 换弹）。</summary>
        public const string EventMessageName = "RaidDemo.Combat.Event";
    }
}
