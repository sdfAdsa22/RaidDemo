using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局生命事件：某人倒地 / 被救起 / 流血死亡（P3.5）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要单独一类消息：</b>结算（<see cref="RaidOutcomeMessage"/>）是"这一局对他结束了"，
    /// 而倒地是"他还有救"。两者混在一起会让客户端无法区分"该等他一会儿"与"该收尾了"。</para>
    /// </remarks>
    public struct RaidLifeEventMessage : INetworkSerializable
    {
        /// <summary>事件种类。</summary>
        public const byte KindDowned = 1;

        /// <summary>被救起。</summary>
        public const byte KindRevived = 2;

        /// <summary>流血死亡（倒计时耗尽，随后会收到结算消息）。</summary>
        public const byte KindBledOut = 3;

        /// <summary>事件种类（见上面的常量）。</summary>
        public byte Kind;

        /// <summary>涉及的玩家。</summary>
        public int PlayerId;

        /// <summary>剩余时间（倒地时为流血倒计时秒数）。</summary>
        public float SecondsRemaining;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref SecondsRemaining);
        }
    }
}
