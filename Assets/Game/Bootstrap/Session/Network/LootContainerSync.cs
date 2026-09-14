using System;
using System.Collections.Generic;
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
    ///
    /// <para><b>为什么同步时要尽量复用物品实例：</b>界面把"物品对象"当成身份在用——
    /// 双击识别比较的是同一个引用，拖拽松手时要按对象在网格里找坐标。
    /// 如果每次同步都把容器内容重建一遍（全部 new），那么只要有一次同步落在
    /// 两次点击之间、或落在一次拖拽的过程里，玩家手上的那件东西就凭空换了对象：
    /// 双击没反应、拖到位松手什么也没发生，而日志里一句错误都没有
    /// （P4.5 实测：同一格、同一件东西，两次同步之间的实例号不同）。
    /// 因此这里按"定义 + 数量 + 摆放方向"复用旧实例，只对真正变化的物品新建。</para>
    /// </remarks>
    public static class LootContainerSync
    {
        /// <summary>
        /// 已经报过"两端容量不一致"的组合（键 = 容器号 + 两端尺寸）。
        /// </summary>
        /// <remarks>
        /// <para>同一种不一致只报一次：容量差异不是错误，而是"装备还没同步"的**持续状态**，
        /// 每次搬东西都刷一条警告会把真正重要的逐件告警淹掉。</para>
        ///
        /// <para>只在主线程访问（同步消息由网络回调驱动），因此不加锁。</para>
        /// </remarks>
        private static readonly HashSet<string> s_ReportedCapacityMismatch = new HashSet<string>();

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

                if (grid.Width != container.Width || grid.Height != container.Height)
                {
                    // 尺寸不一致时**保留本地网格对象**，只把内容铺进去——绝不换对象。
                    //
                    // 为什么不能换：一个背包网格同时被三方持有——ContainerRegistry、
                    // PlayerLoadout（装备 / 重量 / 快捷转移都读它）、以及已经打开的界面视图。
                    // 只换注册表里那一份，就等于从这一刻起有三份"同一个背包"：
                    // 服务器内容铺进了新网格，而玩家眼前和命令里用的还是旧网格。
                    // 表现正是"搜出来的东西进了背包却看不见"与"背包里明明有的东西拖不动"，
                    // 日志里却只留下一句"内容已同步"（P4.5 实机定位的第三处缺陷）。
                    //
                    // 为什么会有差异：联机时服务器用它自己那份装备建随身容器（默认配发 5×5），
                    // 而客户端带的是局外装备（装了背包就是 6×6 / 7×7）。把两者真正对齐属于
                    // P5 的装备同步；在那之前以**本地容量为准**更贴近玩家预期——
                    // 屏幕上画出来的格子，就是他实际能用的格子。
                    ReportCapacityMismatchOnce(container, grid);
                }

                // 复用池必须在清空之前收集：清空之后旧实例就再也找不回来了。
                var reusable = new LootContainerItemReusePool();
                reusable.Collect(grid);
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
                            // 静默跳过物品定义找不到的情况，等于"服务器说有、客户端当没有"，
                            // 排查时完全看不出发生了什么——因此这里必须留痕（P4 验收教训）。
                            UnityEngine.Debug.LogWarning(
                                $"[联机] 容器 {container.ContainerId} 的第 {index} 件物品定义找不到："
                                + $"「{entry.ItemId}」（数量 {entry.Count}）。");
                            continue;
                        }

                        // 优先复用本地已有的同一件东西，复用不到才新建（见类注释）。
                        var item = reusable.Take(entry.ItemId, Mathf.Max(1, entry.Count), entry.Rotated);
                        if (item == null)
                        {
                            item = factory.Create(definition, Mathf.Max(1, entry.Count));
                        }

                        item.Rotated = entry.Rotated;

                        // 先按服务器给的坐标**原位复原**；只有放不下（例如坐标已被别的物品占据、
                        // 或版本差异导致坐标越界）才退回自动摆放，并且必须留一条警告——
                        // 静默退化正是"两端布局悄悄分叉"的来源（P4 验收定位）。
                        var origin = new GridPoint(entry.CellX, entry.CellY);
                        if (grid.Place(item, origin, entry.Rotated).Success)
                        {
                            placed++;
                            continue;
                        }

                        if (grid.AutoPlace(item).Success)
                        {
                            placed++;
                            // 用 Warning 而不是可选的 Verbose：静默退化正是"两端布局悄悄分叉"的来源，
                            // 这一条必须在任何日志级别下都看得见（P4 验收教训）。
                            UnityEngine.Debug.LogWarning(
                                $"[联机] 容器 {container.ContainerId} 的第 {index} 件物品坐标 "
                                + $"({entry.CellX},{entry.CellY}) 放不下，已退回自动摆放。");
                            continue;
                        }

                        UnityEngine.Debug.LogWarning(
                            $"[联机] 容器 {container.ContainerId} 的第 {index} 件物品「{entry.ItemId}」"
                            + $"既放不回原坐标 ({entry.CellX},{entry.CellY})、也找不到空位，已丢弃本次同步。");
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

        /// <summary>
        /// 报告一次"服务器与本地容量不一致"，同一种组合只报一次。
        /// </summary>
        /// <param name="container">服务器下发的容器描述。</param>
        /// <param name="grid">本地保留的网格。</param>
        private static void ReportCapacityMismatchOnce(ContainerContentsMessage container, InventoryGrid grid)
        {
            var key = $"{container.ContainerId}:{container.Width}x{container.Height}:{grid.Width}x{grid.Height}";
            if (!s_ReportedCapacityMismatch.Add(key))
            {
                return;
            }

            UnityEngine.Debug.LogWarning(
                $"[联机] 容器 {container.ContainerId} 的容量两端不一致：服务器 {container.Width}×{container.Height}，"
                + $"本地 {grid.Width}×{grid.Height}。已保留本地网格（换对象会让装备与界面指向旧网格），"
                + "内容按本地容量铺放；放不下的物品会逐件告警。");
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
