using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Raid;
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
            if (m_ContainersReady || string.IsNullOrEmpty(m_Options.MapSceneName))
            {
                return;
            }

            if (SceneManager.GetActiveScene().name != m_Options.MapSceneName)
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
        }
    }
}
