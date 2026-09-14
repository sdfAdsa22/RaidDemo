using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using Unity.Collections;
using UnityEditor;
using Unity.Netcode;

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

        /// <summary>1×1 的弹药：容量不一致那几条用例要靠它把坐标断言写得干净。</summary>
        private const string AmmoId = "ammo.5.45.standard";

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

        /// <summary>
        /// 服务器显式摆放的坐标必须原样复原（P4 验收定位的第二处缺陷）。
        /// </summary>
        /// <remarks>
        /// 早先的消息只带"有什么、几个、是否横放"，客户端一律用 AutoPlace 重新自动摆放；
        /// 只要玩家在任一端显式摆过位置，两端坐标就会分叉，之后的拖拽便"有的能动、有的动不了"。
        /// </remarks>
        [Test]
        public void Apply_PreservesExplicitCellCoordinates()
        {
            var containerId = ContainerIds.SceneContainer(3);
            var grid = new InventoryGrid(5, 5, "战利品");
            m_Registry.Register(grid, ContainerKind.Loot, containerId);

            var batch = new ContainerContentsBatchMessage
            {
                ServerTime = 1d,
                Containers = new[]
                {
                    new ContainerContentsMessage
                    {
                        ContainerId = containerId,
                        Width = 5,
                        Height = 5,
                        Items = new[]
                        {
                            new ContainerItemMessage
                            {
                                ItemId = RifleId,
                                Count = 1,
                                Rotated = false,
                                CellX = 2,
                                CellY = 3,
                            },
                        },
                    },
                },
            };

            LootContainerSync.Apply(m_Registry, m_Catalog, batch, m_Events);

            Assert.AreEqual(1, grid.Items.Count);
            Assert.IsTrue(grid.TryGetOrigin(grid.Items[0], out var origin));
            Assert.AreEqual(2, origin.X, "客户端必须按服务器给的格子坐标复原，而不是自动摆放。");
            Assert.AreEqual(3, origin.Y, "客户端必须按服务器给的格子坐标复原，而不是自动摆放。");
        }

        /// <summary>坐标被占用时不丢物品：退回自动摆放，并留下可查的警告。</summary>
        [Test]
        public void Apply_FallsBackToAutoPlace_WhenCellIsOccupied()
        {
            var containerId = ContainerIds.SceneContainer(4);
            var grid = new InventoryGrid(4, 4, "战利品");
            m_Registry.Register(grid, ContainerKind.Loot, containerId);

            var batch = new ContainerContentsBatchMessage
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
                            new ContainerItemMessage { ItemId = RifleId, Count = 1, CellX = 0, CellY = 0 },
                            new ContainerItemMessage { ItemId = RifleId, Count = 1, CellX = 0, CellY = 0 },
                        },
                    },
                },
            };

            var warnings = new List<string>();
            var rebuilt = LootContainerSync.Apply(
                m_Registry, m_Catalog, batch, m_Events, warnings.Add);

            Assert.AreEqual(1, rebuilt);
            Assert.AreEqual(2, grid.Items.Count, "坐标冲突时应当退回自动摆放，而不是把物品丢掉。");
            Assert.IsNotEmpty(warnings, "退回自动摆放必须留下警告——静默退化正是两端分叉的来源。");
        }

        /// <summary>
        /// 两端容量不一致时，必须**保留本地那个网格对象**，只把内容铺进去。
        /// </summary>
        /// <remarks>
        /// <para><b>它防的是哪一类缺陷：</b>P4.5 实机定位的第三处缺陷——"搜出来的东西进了背包却看不见"
        /// 与"背包里明明有的东西拖不动"同时出现。</para>
        ///
        /// <para>事故链：联机里服务器的随身背包按它自己那份装备建（默认配发 5×5），而客户端带的是
        /// 局外装备（装了背包就是 6×6 / 7×7），两者本来就不一致。若这时按服务器的尺寸**换掉**注册表里的
        /// 网格对象，一个背包就变成了两份数据：服务器内容铺进新网格，而 PlayerLoadout 与已经打开的
        /// 界面视图仍然指向旧网格——玩家眼前和命令里用的都是旧的那份。</para>
        ///
        /// <para>因此断言分两步：对象必须是**同一个**，内容必须真的铺进去。</para>
        /// </remarks>
        [Test]
        public void Apply_CapacityMismatch_KeepsLocalGridObject()
        {
            // 本地 6×6（玩家背了突击包），服务器说这个容器是 5×5（服务器配发的口袋容量）。
            var grid = new InventoryGrid(6, 6, "主背包");
            m_Registry.Register(grid, ContainerKind.PlayerBackpack, ContainerIds.PlayerBackpack);

            var batch = new ContainerContentsBatchMessage
            {
                ServerTime = 1d,
                Containers = new[]
                {
                    new ContainerContentsMessage
                    {
                        ContainerId = ContainerIds.PlayerBackpack,
                        Width = 5,
                        Height = 5,
                        Items = new[]
                        {
                            new ContainerItemMessage
                            {
                                ItemId = AmmoId,
                                Count = 30,
                                Rotated = false,
                                CellX = 3,
                                CellY = 3,
                            },
                        },
                    },
                },
            };

            var rebuilt = LootContainerSync.Apply(m_Registry, m_Catalog, batch, m_Events);

            Assert.AreEqual(1, rebuilt);
            Assert.IsTrue(m_Registry.TryGetGrid(ContainerIds.PlayerBackpack, out var after));
            Assert.AreSame(
                grid,
                after,
                "容量不一致时必须沿用本地那个网格对象——换掉它，装备与界面就会指向旧网格。");
            Assert.AreEqual(1, grid.Items.Count, "容量差异不该让物品丢失，内容仍要铺进本地网格。");
            Assert.IsTrue(grid.TryGetOrigin(grid.Items[0], out var origin));
            Assert.AreEqual(3, origin.X, "内容仍按服务器给的坐标复原。");
            Assert.AreEqual(3, origin.Y, "内容仍按服务器给的坐标复原。");
        }

        /// <summary>
        /// 容器内容消息必须把**格子坐标**原样送达：它是"两端布局一致"的唯一依据。
        /// </summary>
        /// <remarks>
        /// "加了字段却忘了写进 <c>NetworkSerialize</c>"是一类不会报错的缺陷：字段永远是被序列化一方的
        /// 默认值，于是坐标恒为 (0,0)，客户端只能自动摆放，两端布局必然分叉。
        /// 往返一次就能把它钉死——消息里每加一个字段，这条用例都该跟着加一行断言。
        /// </remarks>
        [Test]
        public void ContentsBatch_RoundTripsCellCoordinates()
        {
            var sent = new ContainerContentsBatchMessage
            {
                ServerTime = 12.5d,
                Containers = new[]
                {
                    new ContainerContentsMessage
                    {
                        ContainerId = ContainerIds.SceneContainer(0),
                        Width = 5,
                        Height = 4,
                        Items = new[]
                        {
                            new ContainerItemMessage
                            {
                                ItemId = RifleId,
                                Count = 1,
                                Rotated = true,
                                CellX = 2,
                                CellY = 3,
                            },
                        },
                    },
                },
            };

            var received = default(ContainerContentsBatchMessage);
            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(sent);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadValueSafe(out received);
                }
            }

            Assert.IsNotNull(received.Containers);
            Assert.AreEqual(1, received.Containers.Length);
            Assert.AreEqual(sent.Containers[0].ContainerId, received.Containers[0].ContainerId);
            Assert.AreEqual(sent.Containers[0].Width, received.Containers[0].Width);
            Assert.AreEqual(sent.Containers[0].Height, received.Containers[0].Height);

            var item = received.Containers[0].Items;
            Assert.IsNotNull(item);
            Assert.AreEqual(1, item.Length);
            Assert.AreEqual(RifleId, item[0].ItemId);
            Assert.AreEqual(1, item[0].Count);
            Assert.IsTrue(item[0].Rotated);
            Assert.AreEqual(2, item[0].CellX, "格子坐标必须能送达，否则客户端只能自动摆放，两端布局必然分叉。");
            Assert.AreEqual(3, item[0].CellY, "格子坐标必须能送达，否则客户端只能自动摆放，两端布局必然分叉。");
        }
    }
}
