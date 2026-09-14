using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 局外进度的下行通道（P5）：目前只下发金币。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一条通道：</b>金币不是容器内容（它没有格子与物品），
    /// 也不属于大厅状态（它在一局结束后还会变）。塞进容器批次会让"容器"这个概念变模糊，
    /// 而塞进房间状态广播会让每个人的钱包被广播给所有人。</para>
    ///
    /// <para>物品那一半不需要新通道：仓库与随身装备本来就是容器，
    /// 走既有的容器内容批次（见 <see cref="ContainerNetworkChannel"/>）。</para>
    /// </remarks>
    public static class ProfileNetworkChannel
    {
        /// <summary>服务器 → 单个客户端：他的局外进度（金币）。</summary>
        public const string StateMessageName = "RaidDemo.Profile.State";
    }

    /// <summary>
    /// 服务器权威的局外进度摘要（P5）。
    /// </summary>
    /// <remarks>只带"客户端画不出来"的那部分：物品由容器批次负责。</remarks>
    public struct ProfileStateMessage : INetworkSerializable
    {
        /// <summary>金币余额。</summary>
        public int Money;

        /// <summary>
        /// 这次为什么要发（<see cref="ProfileStateReasons"/>）。
        /// </summary>
        /// <remarks>只用于日志与排障：客户端不用它做任何判断。</remarks>
        public byte Reason;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Money);
            serializer.SerializeValue(ref Reason);
        }
    }

    /// <summary>下发原因（日志用）。</summary>
    public static class ProfileStateReasons
    {
        /// <summary>登录 / 进入房间。</summary>
        public const byte Join = 0;

        /// <summary>撤离结算。</summary>
        public const byte Extracted = 1;

        /// <summary>阵亡 / 超时结算。</summary>
        public const byte Killed = 2;
    }
}
