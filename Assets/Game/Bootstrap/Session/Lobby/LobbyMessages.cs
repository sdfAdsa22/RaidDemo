using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 客户端上行的一条大厅请求。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么只有一个结构体而不是每种请求一个：</b>大厅的请求都很短（几十字节），
    /// 且数量固定。合并成"种类 + 两个字段"之后，服务器侧只需要一条通道、一个处理器、
    /// 一个 switch；新增请求种类不动通道，网络层代码不会随功能线性膨胀。</para>
    ///
    /// <para><b>字段复用约定：</b>字段名刻意叫 A/B 而不是具体业务名，
    /// 因为同一字段在不同请求里含义不同（见 <see cref="LobbyRequestKind"/> 的注释）。
    /// 若将来某个请求需要第三个字段，宁可拆出专用通道，也不要让这里长成"万能消息"。</para>
    ///
    /// <para><b>长度上限：</b>64 字节的固定字符串能装下 16 个汉字或 32 个 token 字符，
    /// 与 <see cref="LobbyLimits"/> 的约束一致。固定长度让消息可以栈上分配，收发不产生垃圾。</para>
    /// </remarks>
    public struct LobbyRequestMessage : INetworkSerializable
    {
        /// <summary>请求种类（<see cref="LobbyRequestKind"/> 的字节值）。</summary>
        public byte Kind;

        /// <summary>字段 A：登录时为昵称，创建房间时为房间名，其余请求忽略。</summary>
        public FixedString64Bytes FieldA;

        /// <summary>字段 B：登录时为口令或 token，创建 / 加入房间时为房间密码。</summary>
        public FixedString64Bytes FieldB;

        /// <summary>请求序号（客户端单调递增，仅用于日志对齐与去重排查）。</summary>
        public uint Sequence;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref FieldA);
            serializer.SerializeValue(ref FieldB);
            serializer.SerializeValue(ref Sequence);
        }
    }

    /// <summary>
    /// 服务器对一条大厅请求的回复（只发给发起者）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要单独回复：</b>房间状态广播说明的是"房间里有什么"，
    /// 而失败原因（密码错、昵称被占用）只与发起者有关，广播出去既泄密又刷屏。</para>
    ///
    /// <para><see cref="Detail"/> 是给界面直接显示的中文说明：错误码决定逻辑分支，
    /// 文本决定玩家看到什么。这样界面不需要维护一份错误码到文案的映射表。</para>
    /// </remarks>
    public struct LobbyResultMessage : INetworkSerializable
    {
        /// <summary>对应的请求种类（<see cref="LobbyRequestKind"/>）。</summary>
        public byte Kind;

        /// <summary>是否成功。</summary>
        public bool Success;

        /// <summary>失败原因（<see cref="LobbyError"/> 的字节值）；成功时为 <see cref="LobbyError.None"/>。</summary>
        public byte Error;

        /// <summary>可直接显示给玩家的中文说明。</summary>
        public FixedString128Bytes Detail;

        /// <summary>
        /// 登录成功时返回的自动登录 token（其余请求为空）。
        /// </summary>
        /// <remarks>
        /// <para>客户端把它存在本地，下次进入联机时直接拿它登录，不必重输口令。
        /// 它只在"这个人已经证明过自己知道口令"之后才会下发，因此拿到它等于拿到一把临时钥匙。</para>
        ///
        /// <para>token 只走这条点对点回复，不进房间状态广播——广播是所有人可见的。</para>
        /// </remarks>
        public FixedString64Bytes Token;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Success);
            serializer.SerializeValue(ref Error);
            serializer.SerializeValue(ref Detail);
            serializer.SerializeValue(ref Token);
        }
    }

    /// <summary>房间成员的一条展示数据。</summary>
    /// <remarks>
    /// <para>它是"展示快照"而不是权威数据：服务器用它广播房间名册，
    /// 客户端只读它来画界面，任何判断（谁是房主、能不能开局）都以服务器为准。</para>
    /// </remarks>
    public struct LobbyMemberEntry : INetworkSerializable
    {
        /// <summary>成员的网络标识（等于其连接编号）。</summary>
        public int ClientId;

        /// <summary>昵称。</summary>
        public FixedString64Bytes Nickname;

        /// <summary>是否是房主。</summary>
        public bool IsHost;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Nickname);
            serializer.SerializeValue(ref IsHost);
        }
    }

    /// <summary>
    /// 房间状态广播：阶段、房间名、房主与成员列表。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用四个固定槽位而不是数组：</b>房间上限就是 4 人（<see cref="LobbyLimits.MaxPlayers"/>），
    /// 固定槽位让消息保持为可栈上分配的值类型，并且天然不可能出现"消息说 7 个人、界面画 4 个"的不一致。
    /// 人数超过上限时由服务器拒绝加入，不会走到这里。</para>
    ///
    /// <para><b>广播给谁：</b>包括尚未加入房间的已连接客户端——他们需要看到"这台服务器上有没有房间"，
    /// 才能决定是创建还是加入。房间密码不会出现在广播里，只有"有没有密码"这一个布尔值。</para>
    /// </remarks>
    public struct RoomStateMessage : INetworkSerializable
    {
        /// <summary>阶段（<see cref="LobbyPhase"/> 的字节值）。</summary>
        public byte Phase;

        /// <summary>房间名；空闲阶段为空。</summary>
        public FixedString64Bytes RoomName;

        /// <summary>是否设置了房间密码（不广播密码本身）。</summary>
        public bool HasPassword;

        /// <summary>房主的网络标识；空闲阶段为 -1。</summary>
        public int HostClientId;

        /// <summary>当前成员数（0~4）。</summary>
        public int MemberCount;

        /// <summary>成员槽位 0。</summary>
        public LobbyMemberEntry Member0;

        /// <summary>成员槽位 1。</summary>
        public LobbyMemberEntry Member1;

        /// <summary>成员槽位 2。</summary>
        public LobbyMemberEntry Member2;

        /// <summary>成员槽位 3。</summary>
        public LobbyMemberEntry Member3;

        /// <summary>按索引读取成员；越界返回默认值。</summary>
        /// <param name="index">0 起算的槽位索引。</param>
        public LobbyMemberEntry GetMember(int index)
        {
            switch (index)
            {
                case 0: return Member0;
                case 1: return Member1;
                case 2: return Member2;
                case 3: return Member3;
                default: return default;
            }
        }

        /// <summary>按索引写入成员；越界时忽略。</summary>
        /// <param name="index">0 起算的槽位索引。</param>
        /// <param name="entry">成员数据。</param>
        public void SetMember(int index, in LobbyMemberEntry entry)
        {
            switch (index)
            {
                case 0: Member0 = entry; break;
                case 1: Member1 = entry; break;
                case 2: Member2 = entry; break;
                case 3: Member3 = entry; break;
            }
        }

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref RoomName);
            serializer.SerializeValue(ref HasPassword);
            serializer.SerializeValue(ref HostClientId);
            serializer.SerializeValue(ref MemberCount);
            serializer.SerializeValue(ref Member0);
            serializer.SerializeValue(ref Member1);
            serializer.SerializeValue(ref Member2);
            serializer.SerializeValue(ref Member3);
        }
    }

    /// <summary>
    /// 服务器 → 房间成员：战局开始。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么客户端不能自己决定进战局：</b>开局是服务器状态机的一次转移
    /// （等待中 → 战局中），客户端只是"被通知去加载地图"。
    /// 若客户端各自决定，就会出现"有人已经出生在地图上、房间却还在等待"的分裂状态。</para>
    ///
    /// <para>地图名由服务器给出而不是客户端自己写死：将来一局一张图时，
    /// 换图只是服务器发一个不同的字符串。</para>
    /// </remarks>
    public struct RaidStartMessage : INetworkSerializable
    {
        /// <summary>要加载的战局场景名。</summary>
        public FixedString64Bytes MapSceneName;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref MapSceneName);
        }
    }
}
