using System;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 把服务器下发的容器内容铺进本地网格（联机客户端的唯一入口）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么抽成独立静态部件：</b>它是"数据搬运 + 通知界面"这两件事的接缝，
    /// 而接缝正是最容易漏东西的地方——U-75 就是漏了通知那一步（只改了数据、没发事件，
    /// 界面永远不重画）。抽出来之后可以**用测试钉住"搬完必须发事件"**，
    /// 而不是靠人工看画面。</para>
    ///
    /// <para><b>铺放顺序即确定性：</b>服务器只下发"有什么、几个、是否横放"，
    /// 格子坐标由两端用同一套放置规则铺出来；同一批物品 + 同一个空网格 → 布局必然一致。</para>
    /// </remarks>
    public static class LootContainerSync
    {
        /// <summary>
        /// 应用一批容器内容。
        /// </summary>
        /// <param name="registry">本地容器注册表。</param>
        /// <param name="catalog">物品目录。</param>
        /// <param name="batch">服务器下发的批次。</param>
        /// <param name="events">本地事件总线：每个被重建的容器都会补发一条
        /// <see cref="InventoryChangeTypes.Sync"/> 变更事件，界面据此重画。</param>
        /// <param name="log">可选的详细日志出口（每个容器一行）。</param>
        /// <returns>实际重建的容器数量。</returns>
        public static int Apply(
            ContainerRegistry registry,
            ItemCatalog catalog,
            in ContainerContentsBatchMessage batch,
            EventBus events,
            Action<string> log = null)
        {
            if (registry == null || catalog == null || batch.Containers == null)
            {
                return 0;
            }

            var factory = new ItemFactory();
            var rebuilt = 0;

            for (var i = 0; i < batch.Containers.Length; i++)
            {
                var container = batch.Containers[i];
                if (container.ContainerId <= 0)
                {
                    continue;
                }

                // 宽度为 0 说明服务器那边也没建起来（例如容器定义缺失）：保持本地现状，
                // 而不是把界面指向一个 0×0 的网格。
                if (container.Width <= 0 || container.Height <= 0)
                {
                    continue;
                }

                // 就地重建而不是替换：背包网格被 PlayerLoadout 直接持有，
                // 换掉对象会让装备、重量、快捷转移全都指向旧网格。
                if (!registry.TryGetGrid(container.ContainerId, out var grid))
                {
                    continue;
                }

                // 尺寸不一致（换了背包）时无法就地改，只能换对象——
                // 这种情况在联机里由服务器的装备同步负责，这里先只处理尺寸一致的情形。
                if (grid.Width != container.Width || grid.Height != container.Height)
                {
                    registry.Replace(container.ContainerId, new InventoryGrid(
                        container.Width,
                        container.Height,
                        grid.Label));
                    registry.TryGetGrid(container.ContainerId, out grid);
                }

                if (grid == null)
                {
                    continue;
                }

                ClearGrid(grid);

                var placed = 0;
                var items = container.Items;
                if (items != null)
                {
                    for (var index = 0; index < items.Length; index++)
                    {
                        var entry = items[index];
                        var definition = entry.ItemId != null ? catalog.Get(entry.ItemId) : null;
                        if (definition == null)
                        {
                            continue;
                        }

                        var item = factory.Create(definition, Mathf.Max(1, entry.Count));
                        item.Rotated = entry.Rotated;

                        if (grid.AutoPlace(item).Success)
                        {
                            placed++;
                        }
                    }
                }

                rebuilt++;
                log?.Invoke($"[联机] 容器 {container.ContainerId} 内容已同步：{placed} 件（服务器权威）。");
            }

            if (rebuilt == 0)
            {
                return 0;
            }

            // 关键的一步：**补发本地事件**。
            // 单机时这条事件由 handler 在本地发布；联机时命令在服务器执行，本地收不到，
            // 必须在这里补上——否则网格数据变了、界面还在画旧内容（U-75 的根因）。
            // 一次全量同步只发一条：界面刷新是整体重画，逐容器发只会重复劳动；
            // 而音效、负重、护甲这些订阅者也都只关心"有变化"。
            events?.Publish(new InventoryChangedEvent(
                containerId: 0,
                changeType: InventoryChangeTypes.Sync,
                playerId: 0,
                timestamp: 0d,
                sequence: 0u));

            return rebuilt;
        }

        /// <summary>清空一个网格（逐个移除，保持网格对象本身不变）。</summary>
        /// <param name="grid">目标网格。</param>
        private static void ClearGrid(InventoryGrid grid)
        {
            // Items 是只读视图，直接遍历时 Remove 会改集合，因此先拷一份出来。
            var items = grid.Items;
            var snapshot = new ItemInstance[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                snapshot[i] = items[i];
            }

            for (var i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != null)
                {
                    grid.Remove(snapshot[i]);
                }
            }
        }
    }
}
