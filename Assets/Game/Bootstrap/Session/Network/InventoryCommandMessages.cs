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
}
