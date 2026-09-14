using RaidDemo.Data;
using RaidDemo.Inventory;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的容器部分：接收服务器下发的容器内容，把本地网格铺成与服务器一致的那一份。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么客户端不再自己抽掉落：</b>搜刮是"两个人抢同一个箱子"的行为。
    /// 各自在本地抽一份，箱子里就是两份不同的东西——谁先拿到、剩下什么都对不上。
    /// 因此联机模式下客户端**不抽掉落**，只把服务器给的物品铺进本地网格。</para>
    ///
    /// <para><b>界面为什么不用改：</b>搜刮界面读的是本地网格对象。只要把网格换成服务器那份内容，
    /// 界面、重量计算、价值统计全都照旧工作——与战斗里"事件来源换成服务器广播"是同一个套路。</para>
    ///
    /// <para><b>铺放顺序即确定性：</b>服务器只下发"有什么、几个、是否横放"，格子坐标由两端用同一套
    /// 放置规则铺出来。同一批物品、同一个空网格，铺出来的布局必然一致，
    /// 因此两台客户端看到的箱子是同一张。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        private bool m_ContainerChannelRegistered;
        private int m_ContainerSyncCount;

        /// <summary>已按服务器内容重建过的容器数量（调试与验收用）。</summary>
        public int ContainerSyncCount
        {
            get { return m_ContainerSyncCount; }
        }

        /// <summary>订阅容器内容通道；连接成功后调用一次。</summary>
        private void RegisterContainerChannel()
        {
            if (m_ContainerChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.RegisterNamedMessageHandler(
                ContainerNetworkChannel.ContentsMessageName,
                OnContainerContentsReceived);
            m_ContainerChannelRegistered = true;

            RequestContainerContents();
        }

        /// <summary>退订容器内容通道（战局场景销毁时调用）。</summary>
        private void UnregisterContainerChannel()
        {
            if (!m_ContainerChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.UnregisterNamedMessageHandler(
                ContainerNetworkChannel.ContentsMessageName);
            m_ContainerChannelRegistered = false;
        }

        /// <summary>
        /// 向服务器要一次容器内容。
        /// </summary>
        /// <remarks>
        /// 必须由客户端主动要：服务器在"客户端接入"那一刻推的内容，
        /// 很可能早于本客户端的处理器注册完成，会被直接丢掉且不留痕迹。
        /// </remarks>
        private void RequestContainerContents()
        {
            if (m_NetworkClient == null || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            // NGO 的服务器编号是常量 0（静态成员，不能用实例访问）。
            var serverId = NetworkManager.ServerClientId;
            // 缓冲要给够：FastBufferWriter 写不下会直接抛异常，
            // 而这里只要写一个字节——留 16 字节是为了容纳框架自己的长度前缀。
            using (var writer = new FastBufferWriter(16, Allocator.Temp))
            {
                writer.WriteValueSafe((byte)1);
                m_NetworkClient.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.RequestMessageName,
                    serverId,
                    writer);
            }

            Debug.Log("[联机] 已向服务器请求容器内容。");
        }

        /// <summary>收到容器内容：把每个容器的网格换成服务器那份。</summary>
        private void OnContainerContentsReceived(ulong senderId, FastBufferReader reader)
        {
            var batch = default(ContainerContentsBatchMessage);
            reader.ReadValueSafe(out batch);

            if (m_ContainerRegistry == null || m_ItemCatalog == null || batch.Containers == null)
            {
                return;
            }

            var log = m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose)
                // Verbose 带一个可选的 UnityEngine.Object 参数，因此这里用 lambda 定型到 Action<string>。
                ? new System.Action<string>(message => m_Session.Log.Verbose(message))
                : null;

            // 真正的搬运与"补发本地事件"都在 LootContainerSync 里：
            // 那里有测试钉住"改完数据必须发事件"，避免再出现"数据动了、画面没动"（U-75）。
            var rebuilt = LootContainerSync.Apply(
                m_ContainerRegistry, m_ItemCatalog, batch, m_EventBus, log);

            m_ContainerSyncCount += rebuilt;

            // 一条汇总痕迹：它证明"箱子内容来自服务器"这件事真的发生了。
            Debug.Log($"[联机] 容器内容已同步：{rebuilt} 个（服务器权威）。");
        }
    }
}
