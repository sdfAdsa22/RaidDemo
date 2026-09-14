namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 容器内容同步使用的命名消息通道。
    /// </summary>
    /// <remarks>
    /// <para>只有下行：容器的内容是服务器抽的，客户端没有"我要你把箱子改成这样"这种请求
    /// （客户端能表达的是"我要拿走这件"，那是 P3-2 的上行命令，走另一条通道）。</para>
    /// </remarks>
    public static class ContainerNetworkChannel
    {
        /// <summary>服务器 → 客户端：一批容器的完整内容。</summary>
        public const string ContentsMessageName = "RaidDemo.Container.Contents";

        /// <summary>
        /// 客户端 → 服务器：请把容器内容发给我。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么必须由客户端主动请求：</b>服务器在"客户端接入"事件里就发内容，
        /// 但那一刻客户端的命名消息处理器**还没注册完**（注册发生在它自己的接入回调里），
        /// 于是这条一次性消息会被直接丢掉——现象是"服务器说有容器、客户端一个也没有"，
        /// 而且没有任何报错。改成客户端就绪后主动要一次，竞态就不存在了。</para>
        /// </remarks>
        public const string RequestMessageName = "RaidDemo.Container.Request";

        /// <summary>
        /// 客户端 → 服务器：一条背包操作命令（把物品从 A 容器移到 B 容器）。
        /// </summary>
        /// <remarks>
        /// 走可靠有序投递：背包命令是有先后语义的（先拿走的那个才算拿到），
        /// 丢一条或乱序都会让"谁拿到了"变得不可解释。
        /// </remarks>
        public const string CommandMessageName = "RaidDemo.Container.Command";

        /// <summary>服务器 → 全体客户端：某人被结算了（撤离 / 阵亡）。</summary>
        public const string OutcomeMessageName = "RaidDemo.Raid.Outcome";

        /// <summary>客户端 → 服务器：请求重开这一局（P4 会把它收进房主权限）。</summary>
        public const string RestartMessageName = "RaidDemo.Raid.Restart";

        /// <summary>客户端 → 服务器：装备 / 卸下（P3 的装备权威化）。</summary>
        public const string EquipCommandMessageName = "RaidDemo.Container.Equip";

        /// <summary>服务器 → 全体客户端：倒地 / 被救起 / 流血死亡。</summary>
        public const string LifeMessageName = "RaidDemo.Raid.Life";
    }
}
