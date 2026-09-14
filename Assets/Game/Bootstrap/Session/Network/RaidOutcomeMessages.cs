using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器裁定的战局结果（一人一条）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么一人一条而不是一条全局结果：</b>联机里各人有各人的结局——
    /// 一个人撤离成功时，队友可能还在图里搜刮，甚至已经阵亡。
    /// 一条全局消息没法表达这件事。</para>
    ///
    /// <para><b>客户端拿到它之后做什么：</b>如果 <see cref="PlayerId"/> 是自己，
    /// 就把本局按这个结果收尾（结算界面）。价值与击杀数也由服务器给，
    /// 客户端不再自己算——结算数据是"跨局资产"，不能由被结算的人自己决定。</para>
    /// </remarks>
    public struct RaidOutcomeMessage : INetworkSerializable
    {
        /// <summary>玩家编号。</summary>
        public int PlayerId;

        /// <summary>结果（<see cref="RaidOutcome"/> 的字节值）。</summary>
        public byte Outcome;

        /// <summary>带出（撤离）或损失（阵亡）的价值。</summary>
        public int CarriedValue;

        /// <summary>本局击杀数。</summary>
        public int Kills;

        /// <summary>本局用时（秒）。</summary>
        public float ElapsedSeconds;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Outcome);
            serializer.SerializeValue(ref CarriedValue);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref ElapsedSeconds);
        }
    }
}
