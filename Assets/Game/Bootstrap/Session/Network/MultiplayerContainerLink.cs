using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的容器链路：内容下行（服务器权威）+ 命令上行（背包 / 装备）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么抽成独立类（P5）：</b>P4.5-b 之后，安全屋也需要"服务器权威的容器"——
    /// 仓库变成房间共享的、随身装备跟着账号走、金币由服务器算。这套东西原本长在
    /// <c>SceneBootstrap</c> 的战局分部里（内容下行 + 六条命令上行），现在两个场景完全一样，
    /// 复制一份意味着"放置规则、上行字段、装备镜像"各有一处会漂移的实现。</para>
    ///
    /// <para><b>通道归属是静态的：</b>与移动链路同一条规矩——网络连接由大厅会话跨场景持有，
    /// 而"容器内容"这个处理器名只有一个；场景切换的顺序是「新场景注册 → 旧场景退订」，
    /// 旧场景若无条件退订就会把新场景刚注册的处理器删掉（症状：进图后箱子全空、搬不动东西）。</para>
    ///
    /// <para><b>本地不执行、只上行：</b>容器的权威在服务器，本地改了也会被下发的权威内容覆盖
    /// （U-75 的教训）。唯一例外是装备 / 卸下：本地先执行让界面即时反馈，同时把同一条意图上行，
    /// 由服务器决定真正的装备状态（服务器还会把结果通过装备槽镜像发回来）。</para>
    /// </remarks>
    internal sealed partial class MultiplayerContainerLink
    {
        /// <summary>当前真正持有容器通道的链路（跨场景注册的归属标记）。</summary>
        private static MultiplayerContainerLink s_ChannelOwner;

        private readonly SessionScope m_Session;
        private readonly ContainerRegistry m_Registry;
        private readonly ItemCatalog m_Catalog;
        private readonly EventBus m_Events;
        private readonly PlayerLoadout m_Loadout;

        private NetworkManager m_Network;
        private bool m_HandlerRegistered;
        private uint m_Sequence;

        /// <summary>
        /// 创建容器链路。
        /// </summary>
        /// <param name="session">会话（日志）。</param>
        /// <param name="registry">本地容器注册表（界面读的就是它）。</param>
        /// <param name="catalog">物品目录（把服务器的物品 ID 还原成定义）。</param>
        /// <param name="events">本地事件总线（铺完内容要补发事件，界面才会重画）。</param>
        /// <param name="loadout">本地随身装备（装备槽镜像的应用目标）。</param>
        public MultiplayerContainerLink(
            SessionScope session,
            ContainerRegistry registry,
            ItemCatalog catalog,
            EventBus events,
            PlayerLoadout loadout)
        {
            m_Session = session;
            m_Registry = registry;
            m_Catalog = catalog;
            m_Events = events;
            m_Loadout = loadout;
        }

        /// <summary>已按服务器内容重建过的容器数量（调试与验收用）。</summary>
        public int SyncCount { get; private set; }

        /// <summary>通道是否已经挂上。</summary>
        public bool IsAttached
        {
            get { return m_Network != null; }
        }

        /// <summary>本实例是不是容器通道的当前持有者（跨场景退订保护）。</summary>
        public bool IsChannelOwner
        {
            get { return ReferenceEquals(s_ChannelOwner, this); }
        }

        /// <summary>
        /// 接管连接：订阅容器内容通道，并主动向服务器要一次全量。
        /// </summary>
        /// <param name="network">大厅会话持有的网络管理器。</param>
        public void Attach(NetworkManager network)
        {
            if (network == null)
            {
                return;
            }

            m_Network = network;
            s_ChannelOwner = this;

            if (!m_HandlerRegistered && m_Network.CustomMessagingManager != null)
            {
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                    ContainerNetworkChannel.ContentsMessageName,
                    OnContainerContentsReceived);
                m_HandlerRegistered = true;
            }

            // 必须由客户端主动要：服务器在"客户端接入"那一刻推的内容，
            // 很可能早于本客户端的处理器注册完成，会被直接丢掉且不留痕迹（P-20）。
            RequestContents();
        }

        /// <summary>
        /// 退订通道。
        /// </summary>
        /// <remarks>不是当前持有者时只清自己的状态：那个处理器可能是新场景刚注册的那一个。</remarks>
        public void Detach()
        {
            if (m_Network != null && m_HandlerRegistered && IsChannelOwner)
            {
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    ContainerNetworkChannel.ContentsMessageName);
                s_ChannelOwner = null;
            }

            m_HandlerRegistered = false;
            m_Network = null;
        }

        /// <summary>
        /// 把六条会改动容器内容的命令换成"只上行"。
        /// </summary>
        /// <param name="router">本场景的命令路由。</param>
        /// <remarks>
        /// 在场景注册完本地处理器之后调用；命令路由支持覆盖注册，
        /// 因此这里只是"换掉处理器"，不动命令层本身。
        /// </remarks>
        public void InstallCommandHandlers(CommandRouter router)
        {
            if (router == null)
            {
                return;
            }

            router.Register<InventoryMoveIntent>(new MultiplayerMoveCommandHandler(this), overwrite: true);
            router.Register<InventoryQuickTransferIntent>(
                new MultiplayerQuickTransferCommandHandler(this), overwrite: true);
            router.Register<InventoryRotateIntent>(new MultiplayerRotateCommandHandler(this), overwrite: true);
            router.Register<InventorySortIntent>(new MultiplayerSortCommandHandler(this), overwrite: true);
            router.Register<InventorySplitIntent>(new MultiplayerSplitCommandHandler(this), overwrite: true);

            // 装备：本地先执行（界面即时反馈），同时把同一条意图上行——
            // 否则会出现"我拿着手枪、服务器按步枪结算"。
            router.Register<InventoryEquipIntent>(new MultiplayerEquipCommandHandler(this), overwrite: true);
            router.Register<InventoryUnequipIntent>(new MultiplayerUnequipCommandHandler(this), overwrite: true);

            Debug.Log("[联机] 背包移动 / 快速转移 / 旋转 / 整理 / 拆分改为上行；装备本地执行并同步给服务器。");
        }

        /// <summary>向服务器要一次容器全量内容。</summary>
        public void RequestContents()
        {
            if (m_Network == null || m_Network.CustomMessagingManager == null)
            {
                return;
            }

            using (var writer = new FastBufferWriter(16, Allocator.Temp))
            {
                writer.WriteValueSafe((byte)1);
                m_Network.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.RequestMessageName,
                    NetworkManager.ServerClientId,
                    writer);
            }

            Debug.Log("[联机] 已向服务器请求容器内容。");
        }

        /// <summary>
        /// 收到容器内容：把每个容器的网格换成服务器那份。
        /// </summary>
        /// <remarks>
        /// 装备槽镜像是"每槽一格"的伪容器，不在容器注册表里，因此在走通用铺放之前单独应用。
        /// 真正的搬运与"补发本地事件"都在 <see cref="LootContainerSync"/> 里——
        /// 那里有测试钉住"改完数据必须发事件"，避免再出现"数据动了、画面没动"（U-75）。
        /// </remarks>
        private void OnContainerContentsReceived(ulong senderId, FastBufferReader reader)
        {
            var batch = default(ContainerContentsBatchMessage);
            reader.ReadValueSafe(out batch);

            if (m_Registry == null || m_Catalog == null || batch.Containers == null)
            {
                return;
            }

            ApplyEquipmentMirror(batch);

            // 显式限定命名空间：本文件同时引用了 Unity.Netcode，它也有一个 LogLevel。
            var log = m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose)
                // Verbose 带一个可选的 UnityEngine.Object 参数，因此这里用 lambda 定型到 Action<string>。
                ? new System.Action<string>(message => m_Session.Log.Verbose(message))
                : null;

            var rebuilt = LootContainerSync.Apply(m_Registry, m_Catalog, batch, m_Events, log);
            SyncCount += rebuilt;

            // 逐容器清单与汇总：出问题时"服务器没发这些容器"与"发了但客户端没应用"
            // 是两种完全不同的诊断，而它们看起来都像"箱子是空的"（P4 验收实机排查）。
            var listing = new System.Text.StringBuilder("[联机] 收到容器清单：");
            var itemCount = 0;
            for (var i = 0; i < batch.Containers.Length; i++)
            {
                var entry = batch.Containers[i];
                var items = entry.Items == null ? 0 : entry.Items.Length;
                itemCount += items;
                listing.Append('#').Append(entry.ContainerId).Append('(').Append(entry.Width).Append('x')
                    .Append(entry.Height).Append('/').Append(items).Append(") ");
            }

            Debug.Log(listing.ToString());
            Debug.Log($"[联机] 容器内容已同步：{rebuilt} 个容器 / {itemCount} 件物品（服务器权威）。");
        }

        /// <summary>
        /// 把服务器下发的装备槽镜像应用到本地装备槽。
        /// </summary>
        /// <remarks>
        /// 整槽覆盖而不是增量：镜像是权威状态，服务器取下的装备（换枪、阵亡清空）
        /// 必须真的从客户端消失。目录里查不到的定义直接跳过——一个坏定义不该让整批同步停下。
        /// </remarks>
        private void ApplyEquipmentMirror(in ContainerContentsBatchMessage batch)
        {
            if (m_Loadout == null || m_Loadout.Equipment == null || m_Catalog == null)
            {
                return;
            }

            var mirrorIndex = -1;
            for (var i = 0; i < batch.Containers.Length; i++)
            {
                if (batch.Containers[i].ContainerId == ContainerIds.EquipmentMirror)
                {
                    mirrorIndex = i;
                    break;
                }
            }

            if (mirrorIndex < 0)
            {
                // 批次里没有镜像（例如广播批只含场景容器）：保持本地装备现状。
                return;
            }

            var applied = EquipmentMirrorCodec.Apply(m_Loadout.Equipment, batch.Containers[mirrorIndex], m_Catalog);
            Debug.Log($"[联机] 装备槽已按服务器同步：{applied} 件。");
        }
    }
}
