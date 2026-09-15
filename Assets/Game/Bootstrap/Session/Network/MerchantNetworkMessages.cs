using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 商人交互（购买 / 出售 / 任务）使用的命名消息通道（P5.5）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要服务端权威：</b>一次交易要同时改"房间共享仓库"和"我的金币"，
    /// 而这两者在 P5 之后都在服务器上（客户端那份只是镜像）。在客户端本地扣钱、加物品，
    /// 下一帧就会被权威内容覆盖回去——显示的是一份不存在的账。因此 P5.5 把整条交易链路
    /// 换成"客户端只表达意图、服务器执行并回发结果"，与背包命令（P3-2）同一条路线。</para>
    ///
    /// <para><b>为什么单独一条通道而不是塞进容器命令：</b>容器命令的消息体是
    /// "从 A 格搬到 B 格"（三组坐标），而交易带的是"买什么、多少个"或"一排待售格子"，
    /// 字段形状完全不同。硬塞进一条消息会让两种语义共用一批用不到的字段，
    /// 将来任何一边加字段都要重新解释另一边的空位。</para>
    /// </remarks>
    public static class MerchantNetworkChannel
    {
        /// <summary>客户端 → 服务器：一条交易 / 任务意图。</summary>
        public const string TradeMessageName = "RaidDemo.Merchant.Trade";

        /// <summary>服务器 → 单个客户端：交易结果（成功或失败原因）。</summary>
        public const string ResultMessageName = "RaidDemo.Merchant.Result";

        /// <summary>服务器 → 单个客户端：任务状态快照。</summary>
        public const string QuestStateMessageName = "RaidDemo.Merchant.QuestState";
    }

    /// <summary>
    /// 交易消息的种类（<see cref="MerchantTradeMessage.Kind"/> 的取值）。
    /// </summary>
    /// <remarks>
    /// 所有种类共用一条上行通道与同一套字段：购买只用 ItemId + Count，
    /// 出售只用格子列表，任务只用 ItemId——空着的字段由发送方留默认值。
    /// 这与容器命令"一条通道 + 按种类分派"是同一种做法。
    /// </remarks>
    public static class MerchantTradeKinds
    {
        /// <summary>购买（ItemId + Count）。</summary>
        public const byte Buy = 0;

        /// <summary>出售单件（一个格子）。</summary>
        public const byte Sell = 1;

        /// <summary>批量出售（多个格子，原子结算）。</summary>
        public const byte SellBatch = 2;

        /// <summary>接取任务。</summary>
        public const byte QuestAccept = 3;

        /// <summary>追踪任务。</summary>
        public const byte QuestTrack = 4;

        /// <summary>上交任务物品。</summary>
        public const byte QuestTurnIn = 5;

        /// <summary>领取任务奖励。</summary>
        public const byte QuestClaim = 6;

        /// <summary>是不是任务类操作。</summary>
        /// <param name="kind">消息种类。</param>
        /// <returns>任务类返回 true。</returns>
        public static bool IsQuest(byte kind)
        {
            return kind >= QuestAccept && kind <= QuestClaim;
        }

        /// <summary>种类的中文说明（日志用）。</summary>
        /// <param name="kind">消息种类。</param>
        /// <returns>可读文本。</returns>
        public static string Describe(byte kind)
        {
            switch (kind)
            {
                case Buy: return "购买";
                case Sell: return "出售";
                case SellBatch: return "批量出售";
                case QuestAccept: return "接取任务";
                case QuestTrack: return "追踪任务";
                case QuestTurnIn: return "上交任务物品";
                case QuestClaim: return "领取任务奖励";
                default: return $"未知种类 {kind}";
            }
        }
    }

    /// <summary>
    /// 一个待售格子的坐标。
    /// </summary>
    /// <remarks>
    /// 与背包命令同一条原则：只传坐标，不传物品对象——服务器拿坐标去权威仓库里找物品，
    /// 客户端无法凭空构造一件物品或指定价格。
    /// </remarks>
    public struct MerchantSellCell : INetworkSerializable
    {
        /// <summary>格子横坐标。</summary>
        public int X;

        /// <summary>格子纵坐标。</summary>
        public int Y;

        /// <summary>创建一个坐标。</summary>
        /// <param name="x">横坐标。</param>
        /// <param name="y">纵坐标。</param>
        public MerchantSellCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
        }
    }

    /// <summary>
    /// 客户端 → 服务器的一条交易 / 任务意图。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么格子用固定容量列表（<see cref="FixedList512Bytes{T}"/>）：</b>
    /// 与房间状态消息"固定槽位"同一条思路——上限固定、消息保持为值类型，
    /// 不可能出现"消息说有 80 个格子、实际只带得动 63 个"的不一致；
    /// 而列表比手写几十个槽位字段短得多，读写都只走一个循环。
    /// 容量 63（512 字节减去 FixedList 自身 8 字节头部，每个坐标 8 字节）：
    /// 共享仓库 7×7=49 格、最多 49 件物品，足够覆盖"整仓全选"。</para>
    ///
    /// <para><b>为什么批量出售要一次发完而不是逐个发单件：</b>M6 定稿的批量出售是**原子结算**
    /// （要么一起卖、要么一件都不卖）。逐件上行时中途一件失败就会出现
    /// "卖了一半、钱也只加了一半"的中间状态，而且这个状态会被节流落盘。</para>
    /// </remarks>
    public struct MerchantTradeMessage : INetworkSerializable
    {
        /// <summary>
        /// 批量出售一次能带的最大格子数。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="FixedList512Bytes{T}"/> 的实际容量保持一致
        /// （512 字节 − 8 字节头部 = 504，除以每个坐标 8 字节 = 63）。
        /// 改列表类型时这个常量要一起改，测试会立刻发现对不上。
        /// </remarks>
        public const int MaxSellCells = 63;

        /// <summary>消息种类（<see cref="MerchantTradeKinds"/>）。</summary>
        public byte Kind;

        /// <summary>物品 ID（购买）或任务 ID（任务操作）。</summary>
        public FixedString64Bytes ItemId;

        /// <summary>购买数量。</summary>
        public int Count;

        /// <summary>实际有效的格子数量（0~<see cref="MaxSellCells"/>）。</summary>
        public int CellCount;

        /// <summary>待售格子（前 <see cref="CellCount"/> 个有效）。</summary>
        public FixedList512Bytes<MerchantSellCell> Cells;

        /// <summary>请求序号（日志与去重参考）。</summary>
        public uint Sequence;

        /// <summary>写入一个待售格子（<see cref="CellCount"/> 满时忽略）。</summary>
        /// <param name="x">横坐标。</param>
        /// <param name="y">纵坐标。</param>
        /// <returns>写入成功返回 true。</returns>
        public bool AddCell(int x, int y)
        {
            if (CellCount < 0 || CellCount >= MaxSellCells)
            {
                return false;
            }

            EnsureCellCapacity();
            Cells[CellCount] = new MerchantSellCell(x, y);
            CellCount++;
            return true;
        }

        /// <summary>
        /// 把格子列表的长度撑到容量上限。
        /// </summary>
        /// <remarks>
        /// <c>FixedList</c> 的默认实例长度是 0——直接按下标写入会抛
        /// <c>IndexOutOfRangeException</c>（本类的消息总是"默认构造 + 填字段"，
        /// 因此写入端与读取端都要先经过这里）。扩容只改变本地可用槽位，
        /// 不改变实际数量：真正有多少个格子由 <see cref="CellCount"/> 说了算。
        /// </remarks>
        private void EnsureCellCapacity()
        {
            if (Cells.Length != MaxSellCells)
            {
                Cells.Length = MaxSellCells;
            }
        }

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Count);
            serializer.SerializeValue(ref CellCount);

            // 只序列化"实际带了的"格子：空槽位不占带宽。
            // 循环上界读端也取同一个值（CellCount 已经先行序列化），两端天然对称。
            var count = CellCount < 0 ? 0 : (CellCount > MaxSellCells ? MaxSellCells : CellCount);
            EnsureCellCapacity();
            for (var i = 0; i < count; i++)
            {
                var cell = Cells[i];
                serializer.SerializeValue(ref cell);
                Cells[i] = cell;
            }

            serializer.SerializeValue(ref Sequence);
        }
    }

    /// <summary>
    /// 服务器 → 客户端：一次交易 / 任务操作的结果。
    /// </summary>
    /// <remarks>
    /// <para><b>成败都要回：</b>成功时客户端要把界面上的乐观文案换成权威文案；
    /// 失败时要把乐观结果改回来并显示原因（与容器命令"不论成败都回发一次"同一条规则）。</para>
    ///
    /// <para>结果消息不携带金币与容器内容：那两样各有权威通道（<c>ProfileStateMessage</c>
    /// 与容器批次）。把同一份数据放进两条消息里，迟早会出现"哪个才是最新的"问题。</para>
    /// </remarks>
    public struct MerchantResultMessage : INetworkSerializable
    {
        /// <summary>对应的请求种类（<see cref="MerchantTradeKinds"/>，日志用）。</summary>
        public byte Kind;

        /// <summary>是否成功。</summary>
        public bool Success;

        /// <summary>展示给玩家的文案（成功摘要或失败原因）。</summary>
        public FixedString128Bytes Detail;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Success);
            serializer.SerializeValue(ref Detail);
        }
    }

    /// <summary>
    /// 任务快照里的一个任务。
    /// </summary>
    /// <remarks>
    /// 与 <c>QuestSaveRecord</c> 字段一一对应：任务的**定义**（标题、目标、奖励）
    /// 两端各自持有同一份静态目录，下行的只有"状态与进度"这些会变的东西。
    /// </remarks>
    public struct MerchantQuestEntry : INetworkSerializable
    {
        /// <summary>任务 ID。</summary>
        public FixedString64Bytes QuestId;

        /// <summary>状态（<c>QuestState</c> 的字节值）。</summary>
        public byte State;

        /// <summary>当前进度。</summary>
        public int Progress;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref QuestId);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref Progress);
        }
    }

    /// <summary>
    /// 服务器 → 客户端：任务状态快照（P5.5）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么整批发而不是逐条更新：</b>任务数量固定为 5（<c>QuestCatalog</c>），
    /// 一次交易后整批发出的代价极小，而"客户端按增量更新"一旦漏掉一条，
    /// 界面会长期停在一个不存在的状态上。整批覆盖让客户端永远与服务器一致。</para>
    ///
    /// <para>槽位数为 <see cref="SlotCount"/>：新增任务时同步改这里与目录——
    /// 数量不符时多余的记录会被忽略，而不是让消息序列化失败。</para>
    /// </remarks>
    public struct MerchantQuestStateMessage : INetworkSerializable
    {
        /// <summary>固定槽位数量（= 当前任务目录的条目数）。</summary>
        public const int SlotCount = 5;

        /// <summary>实际有效的任务数量（0~<see cref="SlotCount"/>）。</summary>
        public int QuestCount;

        /// <summary>当前追踪的任务 ID；没有追踪时为空。</summary>
        public FixedString64Bytes TrackedQuestId;

        /// <summary>任务槽位 0。</summary>
        public MerchantQuestEntry Quest0;

        /// <summary>任务槽位 1。</summary>
        public MerchantQuestEntry Quest1;

        /// <summary>任务槽位 2。</summary>
        public MerchantQuestEntry Quest2;

        /// <summary>任务槽位 3。</summary>
        public MerchantQuestEntry Quest3;

        /// <summary>任务槽位 4。</summary>
        public MerchantQuestEntry Quest4;

        /// <summary>按索引读取任务；越界返回默认值。</summary>
        /// <param name="index">0 起算的槽位索引。</param>
        /// <returns>任务条目；越界时为默认值。</returns>
        public MerchantQuestEntry GetQuest(int index)
        {
            switch (index)
            {
                case 0: return Quest0;
                case 1: return Quest1;
                case 2: return Quest2;
                case 3: return Quest3;
                case 4: return Quest4;
                default: return default;
            }
        }

        /// <summary>按索引写入任务；越界时忽略。</summary>
        /// <param name="index">0 起算的槽位索引。</param>
        /// <param name="entry">任务条目。</param>
        public void SetQuest(int index, in MerchantQuestEntry entry)
        {
            switch (index)
            {
                case 0: Quest0 = entry; break;
                case 1: Quest1 = entry; break;
                case 2: Quest2 = entry; break;
                case 3: Quest3 = entry; break;
                case 4: Quest4 = entry; break;
            }
        }

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref QuestCount);
            serializer.SerializeValue(ref TrackedQuestId);
            serializer.SerializeValue(ref Quest0);
            serializer.SerializeValue(ref Quest1);
            serializer.SerializeValue(ref Quest2);
            serializer.SerializeValue(ref Quest3);
            serializer.SerializeValue(ref Quest4);
        }
    }
}
