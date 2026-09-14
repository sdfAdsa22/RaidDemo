using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>容器里的一件物品（下行用）。</summary>
    /// <remarks>
    /// 只带"是什么、几个、是否横放"三件事：格子坐标不发——客户端拿到同一批物品后
    /// 按同一套放置规则铺一遍，两台客户端得到的就是同一张箱子布局。
    /// </remarks>
    public struct ContainerItemMessage : INetworkSerializable
    {
        /// <summary>物品定义 ID。</summary>
        public string ItemId;

        /// <summary>这一堆的数量。</summary>
        public int Count;

        /// <summary>是否横放（占用尺寸宽高互换）。</summary>
        public bool Rotated;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Count);
            serializer.SerializeValue(ref Rotated);
        }
    }

    /// <summary>一个容器的完整内容（下行用）。</summary>
    public struct ContainerContentsMessage : INetworkSerializable
    {
        /// <summary>容器编号（两端约定一致，见 <see cref="RaidDemo.Inventory.ContainerIds"/>）。</summary>
        public int ContainerId;

        /// <summary>容器网格宽（格）。</summary>
        public int Width;

        /// <summary>容器网格高（格）。</summary>
        public int Height;

        /// <summary>容器里的物品。</summary>
        public ContainerItemMessage[] Items;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ContainerId);
            serializer.SerializeValue(ref Width);
            serializer.SerializeValue(ref Height);

            var count = Items == null ? 0 : Items.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                Items = new ContainerItemMessage[count];
            }

            for (var i = 0; i < count; i++)
            {
                Items[i].NetworkSerialize(serializer);
            }
        }
    }

    /// <summary>
    /// 一批容器内容（服务器 → 客户端）。
    /// </summary>
    /// <remarks>
    /// <para><b>全量而不是增量：</b>一个箱子最多几十件物品，全量的字节数很小，
    /// 而增量同步要处理丢包、乱序与"客户端漏了一条怎么办"——
    /// 对"两个人抢同一个箱子"这种必须绝对一致的东西，全量重发的确定性更值钱。</para>
    /// </remarks>
    public struct ContainerContentsBatchMessage : INetworkSerializable
    {
        /// <summary>这批内容对应的服务器时刻（秒）。</summary>
        public double ServerTime;

        /// <summary>各容器的内容。</summary>
        public ContainerContentsMessage[] Containers;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTime);

            var count = Containers == null ? 0 : Containers.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                Containers = new ContainerContentsMessage[count];
            }

            for (var i = 0; i < count; i++)
            {
                Containers[i].NetworkSerialize(serializer);
            }
        }
    }
}
