using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的背包装配部分。
    /// </summary>
    /// <remarks>
    /// <para>拆成 partial 文件的原因有两个：主文件需要保持在项目规定的 400 行以内；
    /// 装配背包与装配移动本来就是两件独立的事，分开之后各自都能独立阅读。</para>
    /// <para>战斗、AI 等模块的装配在后续里程碑会继续沿用这个做法，各自新增一个 partial 文件。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        private void InitializeInventory()
        {
            m_ContainerRegistry = new ContainerRegistry();
            m_EncumbranceProfile = new EncumbranceProfile { CapacityKg = m_CarryCapacityKg };
            var profileProblem = m_EncumbranceProfile.Validate();
            if (profileProblem != null)
            {
                Debug.LogError($"[RaidDemo] 负重配置不合法：{profileProblem}", this);
                m_EncumbranceProfile = new EncumbranceProfile();
            }

            var backpack = CreateGrid(m_BackpackSize, "主背包");
            m_Loadout = new PlayerLoadout(backpack, new EquipmentLoadout());
            m_BackpackContainerId = m_ContainerRegistry.Register(backpack, ContainerKind.PlayerBackpack);

            var loot = CreateGrid(m_LootContainerSize, "战利品箱");
            m_LootContainerId = m_ContainerRegistry.Register(loot, ContainerKind.Loot);
            PopulateLootContainer(loot);

            var context = new InventoryContext(m_ContainerRegistry, m_Loadout, m_EventBus);
            m_CommandRouter.Register<InventoryMoveIntent>(new InventoryMoveCommandHandler(context));
            m_CommandRouter.Register<InventoryQuickTransferIntent>(new InventoryQuickTransferCommandHandler(context));
            m_CommandRouter.Register<InventoryRotateIntent>(new InventoryRotateCommandHandler(context));
            m_CommandRouter.Register<InventorySplitIntent>(new InventorySplitCommandHandler(context));
            m_CommandRouter.Register<InventorySortIntent>(new InventorySortCommandHandler(context));
            m_CommandRouter.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(context));
            m_CommandRouter.Register<InventoryUnequipIntent>(new InventoryUnequipCommandHandler(context));

            var uiHost = new GameObject("InventoryScreen");
            uiHost.transform.SetParent(transform, worldPositionStays: false);
            m_InventoryScreen = uiHost.AddComponent<InventoryScreenController>();
            m_InventoryScreen.Initialize(
                m_CommandRouter,
                m_ContainerRegistry,
                m_Loadout,
                m_EventBus,
                m_BackpackContainerId,
                m_LootContainerId,
                m_EncumbranceProfile,
                ApplyCursorLock);

            UpdateEncumbrance(true);
        }

        /// <summary>按给定尺寸创建一个网格容器。</summary>
        private static InventoryGrid CreateGrid(Vector2Int size, string label)
        {
            return new InventoryGrid(
                Mathf.Max(1, size.x),
                Mathf.Max(1, size.y),
                label);
        }

        /// <summary>
        /// 用物品目录填满灰盒战利品箱。
        /// </summary>
        /// <remarks>
        /// <para>M2 阶段还没有掉落表（属于 M5 的战局内容），这里用"把目录里的物品各放一份"的方式
        /// 制造出一箱可搜刮的东西，让背包与负重链路能被真正走通。</para>
        /// <para>数量取堆叠上限的三分之一，是为了让玩家一眼看出"这一堆没装满"，
        /// 从而能观察到堆叠合并与拆分的效果。</para>
        /// <para>放置顺序按占地从大到小，否则小件会先把空间切碎，大件（护甲、步枪）
        /// 反而一件都放不进去，灰盒演示时就看不到负重系统真正起作用。</para>
        /// <para>**背包类物品不进灰盒战利品箱**：它们占地 9 到 16 格，两件就能吃掉整箱空间，
        /// 把弹药与武器挤出去，而后者才是验证射击链路必需的东西。
        /// 背包本身属于 M6 局外系统的内容，到那时会有专门的获取途径。</para>
        /// </remarks>
        private void PopulateLootContainer(InventoryGrid loot)
        {
            if (m_ItemCatalog == null)
            {
                Debug.LogWarning(
                    "[RaidDemo] 未指定物品目录，战利品箱将是空的。请先执行菜单「RaidDemo → 生成初始物品资产」。",
                    this);
                return;
            }

            var factory = new ItemFactory();
            var candidates = new List<ItemDefinition>();
            var halfOfContainer = loot.CellCount / 2;
            var items = m_ItemCatalog.All;
            for (var i = 0; i < items.Count; i++)
            {
                var definition = items[i];
                if (definition == null)
                {
                    continue;
                }

                // 占地超过容器一半的物品不收：例如 4x4 的突击背包塞进 5x4 的箱子，
                // 只会把整箱挤满一件，既不合理也看不出背包系统的效果。
                if (definition.GridSize.CellCount > halfOfContainer)
                {
                    continue;
                }

                if (definition.Category == ItemCategory.Backpack)
                {
                    continue;
                }

                candidates.Add(definition);
            }

            candidates.Sort((left, right) =>
                right.GridSize.CellCount.CompareTo(left.GridSize.CellCount));

            for (var i = 0; i < candidates.Count; i++)
            {
                var definition = candidates[i];
                var count = definition.MaxStack > 1 ? Mathf.Max(1, definition.MaxStack / 3) : 1;
                loot.AutoPlace(factory.Create(definition, count));
            }
        }

        /// <summary>
        /// 重新计算负重并把结果应用到移动配置。
        /// </summary>
        /// <param name="force">为 true 时无论状态是否变化都重新计算，用于初始化。</param>
        /// <remarks>
        /// <para>只有负重状态**发生变化**时才真正改写配置并广播事件：负重是个慢变量，
        /// 每帧重算并广播会让订阅方做大量无意义的重复工作。</para>
        /// <para>倍率基于基准值相乘，因此这里反复调用不会让速度越乘越小。</para>
        /// </remarks>
        private void UpdateEncumbrance(bool force = false)
        {
            if (m_Loadout == null || m_EncumbranceProfile == null)
            {
                return;
            }

            var weight = m_Loadout.TotalWeightKg;
            var state = EncumbranceRules.Evaluate(weight, m_EncumbranceProfile);
            if (!force && m_HasEncumbranceState && state == m_LastEncumbranceState)
            {
                return;
            }

            m_HasEncumbranceState = true;
            m_LastEncumbranceState = state;

            var ratio = m_EncumbranceProfile.RatioFor(weight);
            var modifiers = EncumbranceRules.ResolveModifiers(state, ratio);
            m_MovementProfile.ApplyModifiers(modifiers);

            m_EventBus.Publish(new EncumbranceChangedEvent(
                state,
                weight,
                m_EncumbranceProfile.CapacityKg,
                modifiers.SpeedMultiplier,
                modifiers.CanSprint));
        }

        /// <summary>把光标锁定开关交给背包界面调用。</summary>
        private void ApplyCursorLock(bool locked)
        {
            if (m_InputCollector != null)
            {
                m_InputCollector.SetCursorLock(locked);
            }
        }
    }
}
