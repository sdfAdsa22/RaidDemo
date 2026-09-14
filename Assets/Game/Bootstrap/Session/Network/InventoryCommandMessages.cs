using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
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
        /// <summary>0 = 装备，1 = 卸下。</summary>
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
