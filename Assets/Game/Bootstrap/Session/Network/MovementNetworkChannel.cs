namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 移动同步使用的命名消息通道。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用命名消息而不是 NetworkObject + RPC：</b>玩家对象要成为网络对象，
    /// 预制体就必须登记进 NGO 的预制体列表，而那要求场景里有一个配置好的 NetworkManager
    /// （运行时代码创建的 NetworkManager 拿不到资产引用）。把 NetworkManager 搬进场景、
    /// 连带重建场景与构建列表，属于 P2 的"服务器世界装配"范围。</para>
    ///
    /// <para>P1 只做移动，两端各一个通道就够：上行走输入、下行走快照。
    /// 身份用连接标识（<c>clientId</c>）即可，不需要额外的对象所有权。
    /// P2 引入玩家网络对象后，这两个通道会被 NetworkObject 的 NetworkVariable 与 RPC 取代。</para>
    /// </remarks>
    public static class MovementNetworkChannel
    {
        /// <summary>客户端 → 服务器：一条移动输入。</summary>
        public const string InputMessageName = "RaidDemo.Movement.Input";

        /// <summary>服务器 → 全体客户端：一批玩家快照。</summary>
        public const string SnapshotMessageName = "RaidDemo.Movement.Snapshot";
    }
}
