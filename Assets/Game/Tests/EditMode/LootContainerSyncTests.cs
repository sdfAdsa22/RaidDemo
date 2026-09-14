using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using Unity.Collections;
using UnityEditor;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器容器内容下行后的"本地应用"测试。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>U-75 —— 服务器明明把物品搬走了、客户端日志也显示"内容已同步"，
    /// 但**界面还在画旧内容**：因为应用数据的那段代码只改了网格、没有在本地补发变更事件，
    /// 而界面只在收到事件时才重画。玩家看到的就是"物品搬不动"。</para>
    ///
    /// <para>因此这里的核心断言不是"物品进没进网格"，而是**"应用完必须发出变更事件"**。</para>
    /// </remarks>
    [TestFixture]
    public sealed class LootContainerSyncTests
    {
        private const string CatalogPath = "Assets/Game/Content/Items/ItemCatalog.asset";
        private const string RifleId = "weapon.rifle.ak74";

        private ItemCatalog m_Catalog;
        private ContainerRegistry m_Registry;
        private EventBus m_Events;

        [SetUp]
        public void SetUp()
        {
            m_Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.IsNotNull(m_Catalog, $"找不到物品目录：{CatalogPath}");

            m_Registry = new ContainerRegistry();
            m_Events = new EventBus();
        }

        /// <summary>造一批"服务器说这个箱子里有一把步枪"的内容。</summary>
        private static ContainerContentsBatchMessage BatchWithRifle(int containerId, string itemId = RifleId)
        {
            return new ContainerContentsBatchMessage
            {
                ServerTime = 1d,
                Containers = new[]
                {
                    new ContainerContentsMessage
                    {
                        ContainerId = containerId,
                        Width = 4,
                        Height = 4,
                        Items = new[]
                        {
                            new ContainerItemMessage
                            {
                                ItemId = itemId,
                                Count = 1,
                                Rotated = false,
                            },
                        },
                    },
                },
            };
        }

        [Test]
        public void Apply_PutsServerItemsIntoLocalGrid()
        {
            var containerId = ContainerIds.SceneContainer(0);
            var grid = new InventoryGrid(4, 4, "战利品");
            m_Registry.Register(grid, ContainerKind.Loot, containerId);

            var rebuilt = LootContainerSync.Apply(m_Registry, m_Catalog, BatchWithRifle(containerId), m_Events);

            Assert.AreEqual(1, rebuilt);
            Assert.AreEqual(1, grid.Items.Count, "服务器下发的物品应当被铺进本地网格。");
        }

        /// <summary>U-75 的回归断言：应用完必须发出变更事件，否则界面不会重画。</summary>
        [Test]
        public void Apply_PublishesInventoryChangedEvent()
        {
            var containerId = ContainerIds.SceneContainer(1);
            var grid = new InventoryGrid(4, 4, "战利品");
            m_Registry.Register(grid, ContainerKind.Loot, containerId);

            var received = 0;
            var lastChangeType = string.Empty;
            using (m_Events.Subscribe<InventoryChangedEvent>(evt =>
                   {
                       received++;
                       lastChangeType = evt.ChangeType;
                   }))
            {
                LootContainerSync.Apply(m_Registry, m_Catalog, BatchWithRifle(containerId), m_Events);
            }

            Assert.AreEqual(1, received, "应用服务器内容后必须补发一条本地变更事件，否则界面不会刷新。");
            Assert.AreEqual(InventoryChangeTypes.Sync, lastChangeType);
        }

        /// <summary>空批次（服务器说这个箱子空了）同样要发事件：界面要把里面的东西擦掉。</summary>
        [Test]
        public void Apply_EmptyBatch_ClearsGridAndStillPublishes()
        {
            var containerId = ContainerIds.SceneContainer(2);
            var grid = new InventoryGrid(4, 4, "战利品");
            m_Registry.Register(grid, ContainerKind.Loot, containerId);

            var received = 0;
            using (m_Events.Subscribe<InventoryChangedEvent>(_ => received++))
            {
                LootContainerSync.Apply(m_Registry, m_Catalog, BatchWithRifle(containerId), m_Events);
                LootContainerSync.Apply(
                    m_Registry,
                    m_Catalog,
                    new ContainerContentsBatchMessage
                    {
                        ServerTime = 2d,
                        Containers = new[]
                        {
                            new ContainerContentsMessage
                            {
                                ContainerId = containerId,
                                Width = 4,
                                Height = 4,
                                Items = new List<ContainerItemMessage>().ToArray(),
                            },
                        },
                    },
                    m_Events);
            }

            Assert.AreEqual(0, grid.Items.Count, "服务器说空了，本地网格也应当空。");
            Assert.AreEqual(2, received, "两次同步应当各发一条事件（清空也要发）。");
        }

        [Test]
        public void Apply_UnknownContainer_IsIgnoredWithoutEvent()
        {
            var received = 0;
            using (m_Events.Subscribe<InventoryChangedEvent>(_ => received++))
            {
                var rebuilt = LootContainerSync.Apply(
                    m_Registry, m_Catalog, BatchWithRifle(ContainerIds.SceneContainer(9)), m_Events);

                Assert.AreEqual(0, rebuilt, "本地没有这个容器时应当安静跳过。");
            }

            Assert.AreEqual(0, received, "什么都没改时不该惊动界面。");
        }
    }
}
