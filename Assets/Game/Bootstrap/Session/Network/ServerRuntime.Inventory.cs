using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Raid;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的容器部分：建权威容器注册表并抽出容器内容（P3-1 起）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么容器必须在服务器上：</b>搜刮是"两个人抢同一个箱子"的行为。
    /// 若各自在本地抽一份掉落，两人看到的箱子里就是两份不同的东西——
    /// 谁先拿到、剩下什么，两边永远对不上，而且这是无法靠事后对账弥补的
    /// （与 AI 必须搬到服务器是同一个理由）。</para>
    ///
    /// <para><b>容器内容从哪来：</b>与客户端同一张地图、同一份生成点数组
    /// （场景里的 <see cref="LootSpawnPoint"/>）与同一份容器定义表。
    /// 服务器不重新设计掉落，只是"由它来抽"这一件事换了个执行者。</para>
    ///
    /// <para><b>随机数必须用服务器自己的：</b>两个进程的 <c>UnityEngine.Random</c> 序列不同，
    /// 客户端本地抽出来的内容与服务器永远对不上。这里用 <see cref="DeterministicRandom"/>，
    /// 与武器散布同源——同样的种子必然得到同样的箱子内容。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>默认掉落种子。固定值让同一份种子下的战局内容可复现（便于排查与验收）。</summary>
        private const int DefaultLootSeed = 20260914;

        private readonly ContainerRegistry m_Containers = new ContainerRegistry();
        private readonly Dictionary<int, string> m_ContainerDefinitions = new Dictionary<int, string>();
        private bool m_ContainersReady;

        /// <summary>服务器侧的容器注册表。未就绪时是空的（不是 null）。</summary>
        public ContainerRegistry Containers
        {
            get { return m_Containers; }
        }

        /// <summary>容器是否已经建好（验收与调试用）。</summary>
        public bool ContainersReady
        {
            get { return m_ContainersReady; }
        }

        /// <summary>
        /// 每帧检查：地图生效且物品目录到手之后，建一次容器。
        /// </summary>
        /// <remarks>
        /// 依赖两件启动期才具备的东西（地图场景 + 物品目录），因此与 AI 一样用惰性初始化：
        /// 第一帧拿不到就下一帧再试，不阻塞服务器起来。
        /// </remarks>
        private void TickContainers()
        {
            // 只有战局世界才有战利品容器：安全屋里没有搜刮点，也不该有任何"这局的箱子"。
            if (m_ContainersReady || m_WorldKind != ServerWorldKind.Raid)
            {
                return;
            }

            if (SceneManager.GetActiveScene().name != m_WorldSceneName)
            {
                return;
            }

            var catalog = ServerMode.SceneItemCatalog;
            if (catalog == null)
            {
                return;
            }

            BuildContainers(catalog);
            m_ContainersReady = true;
        }

        /// <summary>按场景里的生成点建立全部战利品容器并抽好内容。</summary>
        /// <param name="catalog">物品目录。</param>
        private void BuildContainers(ItemCatalog catalog)
        {
            var spawnPoints = Object.FindObjectsByType<LootSpawnPoint>(FindObjectsSortMode.None);
            var roller = new LootRoller(
                catalog,
                new DeterministicRandom(DefaultLootSeed),
                new ItemFactory());

            var placedTotal = 0;
            var built = 0;

            for (var i = 0; i < spawnPoints.Length; i++)
            {
                var point = spawnPoints[i];
                var definition = LootContainerCatalog.Get(point.ContainerDefinitionId);
                if (definition == null)
                {
                    // 未知定义与客户端同处理：跳过这一箱，而不是让整张地图的容器都建不起来。
                    m_Session?.Log.Warning(
                        $"[服务器] 未知的容器定义「{point.ContainerDefinitionId}」，已跳过。");
                    continue;
                }

                var grid = new InventoryGrid(
                    definition.GridSize.Width,
                    definition.GridSize.Height,
                    definition.DisplayName);

                // 编号用与客户端同一套约定（起始值 + 生成顺序）：
                // 两端读的是同一张地图，因此第 i 个箱子在两端是同一个编号。
                var containerId = m_Containers.Register(
                    grid, ContainerKind.Loot, ContainerIds.SceneContainer(i));
                if (containerId == 0)
                {
                    m_Session?.Log.Warning($"[服务器] 容器编号 {ContainerIds.SceneContainer(i)} 已被占用，已跳过。");
                    continue;
                }

                var placed = roller.Roll(definition.Table, grid, definition.FixedContents);
                placedTotal += placed;
                built++;

                m_ContainerDefinitions[containerId] = point.ContainerDefinitionId;

                if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
                {
                    m_Session.Log.Verbose(
                        $"[服务器] 容器 {containerId}（{definition.DisplayName}）已就绪：{placed} 件。");
                }
            }

            // 汇总一条 Info 痕迹：这是"容器权威在服务器上成立"最直接的证据，
            // 而它是否成立决定了联机搜刮是不是两个人各看各的箱子。
            m_Session?.Log.Info(
                $"[服务器] 容器已就绪：{built}/{spawnPoints.Length} 个，共 {placedTotal} 件物品"
                + $"（掉落种子 {DefaultLootSeed}）。");

            // 容器可能在客户端接入之后才建好（要等地图与物品目录），因此建完就补发一次全量。
            BroadcastAllContainerContents();
        }

        /// <summary>
        /// 把全部容器的内容发给所有客户端。
        /// </summary>
        /// <remarks>
        /// 全量重发：箱子内容必须绝对一致，而全量的字节数很小（每箱最多几十件）。
        /// 增量同步要处理丢包与乱序，收益远小于风险。
        /// </remarks>
        private void BroadcastAllContainerContents()
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null
                || manager.ConnectedClientsIds.Count == 0)
            {
                return;
            }

            var batch = BuildContainerContentsBatch();
            using (var writer = new FastBufferWriter(ContainerContentsCapacity(batch), Allocator.Temp))
            {
                writer.WriteValueSafe(batch);
                manager.CustomMessagingManager.SendNamedMessageToAll(
                    ContainerNetworkChannel.ContentsMessageName,
                    writer,
                    // 容器全量一次可能上千字节，超过默认投递方式的单包上限（1264 字节）会抛异常。
                    // 用分片可靠投递：内容必须完整到达，而且它本来就是"一次全量"。
                    NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        /// <summary>
        /// 把全部容器内容 + 这名玩家自己的随身容器与装备槽镜像发给他本人。
        /// </summary>
        /// <param name="clientId">目标客户端（数值上等于该玩家的编号）。</param>
        /// <remarks>
        /// <para>两个调用点都是"发给某一名玩家本人"：玩家刚接入时（<c>ServerRuntime.Players</c>）
        /// 与客户端主动请求时（<c>OnContainerContentsRequested</c>）。后者才是真正可靠的路径——
        /// 服务器推的那一次可能早于客户端处理器注册完成。</para>
        ///
        /// <para><b>踩过的坑：</b>本方法原先调用不带玩家编号的
        /// <c>BuildContainerContentsBatch()</c>，于是这条"可靠路径"只给了场景容器：
        /// 背包内容要等第一次背包命令的重发才补上，而装备槽镜像（<see cref="ContainerIds.EquipmentMirror"/>）
        /// 完全不在批次里。客户端表现是"进图后 HUD 无武器、按开火没有反应"，
        /// 而服务器日志一切正常（2026-09-14 联机基础问题修复的定位结论）。</para>
        /// </remarks>
        private void SendAllContainerContentsTo(ulong clientId)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null || !m_ContainersReady)
            {
                return;
            }

            var batch = BuildContainerContentsBatch(playerId: (int)clientId);
            using (var writer = new FastBufferWriter(ContainerContentsCapacity(batch), Allocator.Temp))
            {
                writer.WriteValueSafe(batch);
                manager.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.ContentsMessageName,
                    clientId,
                    writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        /// <summary>客户端要求容器内容：回一份全量。</summary>
        /// <param name="senderId">发起请求的客户端。</param>
        /// <param name="reader">消息体（当前为空）。</param>
        /// <remarks>
        /// 这是容器内容真正可靠的下发路径：服务器推的那一次可能早于客户端就绪，
        /// 而"客户端注册完处理器之后自己来要"不存在时序问题。
        /// </remarks>
        private void OnContainerContentsRequested(ulong senderId, FastBufferReader reader)
        {
            SendAllContainerContentsTo(senderId);

            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose($"[服务器] 客户端 {senderId} 请求容器内容，已回发全量。");
            }
        }

        /// <summary>
        /// 把"场景容器 + 这名玩家自己的容器"发给他本人。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <remarks>
        /// 背包命令执行完之后走这条路：场景容器可能少了一件（所有人都要知道），
        /// 而他的背包里多了一件（只有他自己需要看到）。
        /// </remarks>
        private void SendContainerContentsTo(int playerId)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null)
            {
                return;
            }

            var batch = BuildContainerContentsBatch(playerId);
            using (var writer = new FastBufferWriter(ContainerContentsCapacity(batch), Allocator.Temp))
            {
                writer.WriteValueSafe(batch);
                manager.CustomMessagingManager.SendNamedMessage(
                    ContainerNetworkChannel.ContentsMessageName,
                    (ulong)playerId,
                    writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        /// <summary>把当前容器注册表里所有场景容器的内容打包成一条消息。</summary>
        private ContainerContentsBatchMessage BuildContainerContentsBatch()
        {
            return BuildContainerContentsBatch(playerId: -1);
        }

        /// <summary>
        /// 打包容器内容；<paramref name="playerId"/> 为负数时只含场景容器。
        /// </summary>
        /// <param name="playerId">要一并打包其随身容器的玩家；-1 表示不带玩家容器。</param>
        private ContainerContentsBatchMessage BuildContainerContentsBatch(int playerId)
        {
            var ids = m_Containers.ContainerIds;
            var entries = new List<ContainerContentsMessage>(ids.Count);
            var backpackId = playerId >= 0
                ? ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.Backpack)
                : 0;
            var pouchId = playerId >= 0
                ? ContainerIds.ServerPlayerContainer(playerId, ContainerIds.PlayerSlot.AmmoPouch)
                : 0;

            for (var i = 0; i < ids.Count; i++)
            {
                var containerId = ids[i];
                var isSceneContainer = containerId >= ContainerIds.SceneBase
                                       && containerId < ContainerIds.ServerPlayerBase;
                var isOwnContainer = containerId == backpackId || containerId == pouchId;

                if (!isSceneContainer && !isOwnContainer)
                {
                    continue;
                }

                if (!m_Containers.TryGetGrid(containerId, out var grid))
                {
                    continue;
                }

                // 客户端只认自己那套编号（背包 1 / 弹药挂 2），因此玩家容器要翻译回去。
                var wireId = containerId == backpackId
                    ? ContainerIds.PlayerBackpack
                    : containerId == pouchId
                        ? ContainerIds.AmmoPouch
                        : containerId;

                entries.Add(BuildContainerContents(wireId, grid));
            }

            // 装备槽没有自己的下行通道，借容器批次一起发（见 ContainerIds.EquipmentMirror 的说明）。
            // 只在发给"玩家本人"的批次里带：装备状态是私有的，广播给所有人没有意义。
            if (playerId >= 0 && TryBuildEquipmentMirror(playerId, out var equipmentMirror))
            {
                entries.Add(equipmentMirror);
            }

            return new ContainerContentsBatchMessage
            {
                ServerTime = m_World != null ? m_World.SimulationTime : 0d,
                Containers = entries.ToArray(),
            };
        }

        /// <summary>把一个容器网格转成可下行的内容描述。</summary>
        private static ContainerContentsMessage BuildContainerContents(int containerId, InventoryGrid grid)
        {
            var items = grid.Items;
            var entries = new ContainerItemMessage[items.Count];

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                // 带上真实坐标：客户端据此"原位复原"，而不是重新自动摆放——
                // 否则玩家显式摆过的位置会在下一次同步被抹掉，两端坐标从那一刻开始分叉。
                var origin = default(GridPoint);
                grid.TryGetOrigin(item, out origin);

                entries[i] = new ContainerItemMessage
                {
                    ItemId = item.Definition != null ? item.Definition.Id : null,
                    Count = item.StackCount,
                    Rotated = item.Rotated,
                    CellX = origin.X,
                    CellY = origin.Y,
                };
            }

            return new ContainerContentsMessage
            {
                ContainerId = containerId,
                Width = grid.Width,
                Height = grid.Height,
                Items = entries,
            };
        }

        /// <summary>
        /// 估算一条容器全量消息需要的缓冲区大小。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不能"估个差不多"：</b><c>FastBufferWriter</c> 写不下时会直接抛
        /// <c>OverflowException</c>——消息发不出去，而且异常发生在发送这一侧，
        /// 客户端只会表现为"什么都没收到"。（这条是实测踩出来的：按 128 字节/容器估算，
        /// 十几个容器就溢出了。）</para>
        ///
        /// <para>因此按**上界**给：每个容器最多按它自己的网格容量算物品数，
        /// 每件物品按 64 字节（物品 ID 字符串 + 数量 + 旋转 + 长度前缀）估算，
        /// 再留 1 KB 的固定余量。</para>
        /// </remarks>
        private static int ContainerContentsCapacity(in ContainerContentsBatchMessage batch)
        {
            const int fixedOverheadBytes = 1024;
            const int perItemBytes = 64;

            var containers = batch.Containers;
            if (containers == null)
            {
                return fixedOverheadBytes;
            }

            var total = fixedOverheadBytes;
            for (var i = 0; i < containers.Length; i++)
            {
                var items = containers[i].Items;
                var itemCount = items == null ? 0 : items.Length;
                total += 64 + (itemCount * perItemBytes);
            }

            return total;
        }
    }
}
