using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 背包上行命令的种类。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用"一种消息 + 种类字段"而不是每种命令一条通道：</b>这些命令的载荷形状几乎一样
    /// （源容器 / 目标容器 / 格子 / 数量），合并之后服务器侧只需要一个入口、一个 switch，
    /// 新增命令不再改动网络层——与大厅请求的做法一致。</para>
    ///
    /// <para><b>为什么必须把这几条都接上来：</b>背包的权威在服务器（P3-1 起容器内容由服务器下发），
    /// 客户端本地执行任何一条背包命令都会被随后的权威内容覆盖回去。
    /// 曾经只接管了"拖拽移动"与"装备"，于是**双击快速转移 / 旋转 / 整理 / 拆分**在联机里全都"点了没反应"
    /// （本地改了、服务器又刷回来，见 U-75）。</para>
    /// </remarks>
    public static class InventoryCommandKinds
    {
        /// <summary>拖拽移动：从源格子拿到目标格子。</summary>
        public const byte Move = 0;

        /// <summary>双击快速转移：目标落点由规则层决定（先合并、再找空位）。</summary>
        public const byte QuickTransfer = 1;

        /// <summary>旋转：原地把物品转 90 度。</summary>
        public const byte Rotate = 2;

        /// <summary>整理：把一个容器里的物品重排。</summary>
        public const byte Sort = 3;

        /// <summary>拆分：把一格里的物品分成两份（数量见 <see cref="InventoryMoveCommandMessage.Count"/>）。</summary>
        public const byte Split = 4;
    }

    /// <summary>
    /// 客户端上行的"把某件物品从 A 容器移到 B 容器"命令。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么只发意图不发物品：</b>与单机的命令层完全一致——
    /// 命令里只有"从哪个容器的哪一格，移到哪个容器的哪一格"，
    /// 物品本身由权威侧根据自己那份数据查出来。客户端若能描述物品，就等于能凭空造物。</para>
    ///
    /// <para><b>编号在服务器侧要翻译：</b>客户端说的"1 号容器"永远是它自己的背包，
    /// 而服务器上一个注册表里放着所有玩家的背包，因此命令进来后先按
    /// <see cref="RaidDemo.Inventory.ContainerIds.ServerPlayerContainer"/> 做一次翻译。</para>
    /// </remarks>
    public struct InventoryMoveCommandMessage : INetworkSerializable
    {
        /// <summary>源容器编号。</summary>
        public int SourceContainerId;

        /// <summary>目标容器编号。</summary>
        public int TargetContainerId;

        /// <summary>源格子 X。</summary>
        public int SourceCellX;

        /// <summary>源格子 Y。</summary>
        public int SourceCellY;

        /// <summary>目标格子 X。</summary>
        public int TargetCellX;

        /// <summary>目标格子 Y。</summary>
        public int TargetCellY;

        /// <summary>是否横放。</summary>
        public bool Rotated;

        /// <summary>命令种类（<see cref="InventoryCommandKinds"/> 的取值）。</summary>
        public byte Kind;

        /// <summary>拆分数量（仅"拆分"使用；其余命令为 0）。</summary>
        public int Count;

        /// <summary>命令序号（客户端单调递增，用于日志与去重）。</summary>
        public uint Sequence;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SourceContainerId);
            serializer.SerializeValue(ref TargetContainerId);
            serializer.SerializeValue(ref SourceCellX);
            serializer.SerializeValue(ref SourceCellY);
            serializer.SerializeValue(ref TargetCellX);
            serializer.SerializeValue(ref TargetCellY);
            serializer.SerializeValue(ref Rotated);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Count);
            serializer.SerializeValue(ref Sequence);
        }
    }

    /// <summary>
    /// 客户端上行的装备 / 卸下命令。
    /// </summary>
    /// <remarks>
    /// <para><b>与移动命令的区别：</b>装备只影响自己的东西，但**必须让服务器知道**——
    /// 开火、换弹、弹药口径全在服务器上按"当前手持武器"算。
    /// 客户端本地换了枪而服务器不知道的话，会出现"我拿着手枪、服务器按步枪的射程与伤害结算"。</para>
    ///
    /// <para>装备的界面反馈要即时，因此客户端**本地先执行**再把同一条意图发上来；
    /// 服务器执行失败时以警告记一笔（纠正通路留给 P5 的装备持久化）。</para>
    /// </remarks>
    public struct InventoryEquipCommandMessage : INetworkSerializable
    {
        /// <summary>装备：把来源容器格子里的一件物品装进指定槽位。</summary>
        public const byte KindEquip = 0;

        /// <summary>卸下：把指定槽位里的物品放回来源容器。</summary>
        public const byte KindUnequip = 1;

        /// <summary>
        /// 使用：把来源格子里的一件**消耗品**用掉（当前只有医疗品）。
        /// </summary>
        /// <remarks>
        /// <para>联机下"使用物品"必须上行：回血改的是服务器权威生命值，
        /// 而扣物品改的是服务器权威容器。客户端自己回血等于两边各算一套（`RD-AUD-044`）。</para>
        ///
        /// <para>复用同一条消息：它已经有"来源容器 + 格子 + 序号"这三个字段，
        /// 正是"用哪一格里的哪一件"需要的全部信息；<see cref="Slot"/> 在本种类下不使用。</para>
        /// </remarks>
        public const byte KindUse = 2;

        /// <summary>
        /// 操作种类：<see cref="KindEquip"/> / <see cref="KindUnequip"/> / <see cref="KindUse"/>。
        /// </summary>
        public byte Kind;

        /// <summary>来源容器编号（卸下时忽略）。</summary>
        public int ContainerId;

        /// <summary>来源格子 X（卸下时忽略）。</summary>
        public int CellX;

        /// <summary>来源格子 Y（卸下时忽略）。</summary>
        public int CellY;

        /// <summary>装备槽（<see cref="RaidDemo.Inventory.EquipmentSlot"/> 的字节值）。</summary>
        public byte Slot;

        /// <summary>命令序号。</summary>
        public uint Sequence;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref ContainerId);
            serializer.SerializeValue(ref CellX);
            serializer.SerializeValue(ref CellY);
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref Sequence);
        }
    }
}
