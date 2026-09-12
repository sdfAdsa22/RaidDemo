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

            // 弹药挂：一行五格，只收弹药。规则写在网格自身的分类过滤上，
            // 因此拖拽、堆叠、拆分、整理全部自动可用。
            var ammoPouch = new InventoryGrid(
                m_AmmoPouchCells,
                1,
                "弹药挂",
                acceptedCategory: ItemCategory.Ammo);

            m_Loadout = new PlayerLoadout(backpack, new EquipmentLoadout(), ammoPouch);
            m_BackpackContainerId = m_ContainerRegistry.Register(backpack, ContainerKind.PlayerBackpack);
            m_AmmoPouchContainerId = m_ContainerRegistry.Register(ammoPouch, ContainerKind.AmmoPouch);

            // 战利品容器不再在这里创建：M5 的容器散布在地图上，
            // 由 InitializeRaid 按场景标记逐个生成（见 SceneBootstrap.Raid.cs）。
            // 这一段只负责「随身携带的那几件东西」。
            var context = new InventoryContext(m_ContainerRegistry, m_Loadout, m_EventBus);
            m_CommandRouter.Register<InventoryMoveIntent>(new InventoryMoveCommandHandler(context));
            m_CommandRouter.Register<InventoryQuickTransferIntent>(new InventoryQuickTransferCommandHandler(context));
            m_CommandRouter.Register<InventoryRotateIntent>(new InventoryRotateCommandHandler(context));
            m_CommandRouter.Register<InventorySplitIntent>(new InventorySplitCommandHandler(context));
            m_CommandRouter.Register<InventorySortIntent>(new InventorySortCommandHandler(context));
            m_CommandRouter.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(context));
            m_CommandRouter.Register<InventoryUnequipIntent>(new InventoryUnequipCommandHandler(context));
            m_CommandRouter.Register<PlayerSwitchWeaponIntent>(
                new WeaponSwitchCommandHandler(m_Loadout.Equipment, m_EventBus));

            var uiHost = new GameObject("InventoryScreen");
            uiHost.transform.SetParent(transform, worldPositionStays: false);
            m_InventoryScreen = uiHost.AddComponent<InventoryScreenController>();
            m_InventoryScreen.Initialize(
                m_CommandRouter,
                m_ContainerRegistry,
                m_Loadout,
                m_EventBus,
                m_BackpackContainerId,
                m_AmmoPouchContainerId,
                m_EncumbranceProfile,
                ApplyCursorLock,
                m_ItemCatalog,
                RequestUseItemAt);

            UpdateEncumbrance(true);
        }

        /// <summary>没有装备背包时的口袋容量（列 x 行）。</summary>
        private static readonly Vector2Int PocketSize = new Vector2Int(5, 5);

        /// <summary>
        /// 按当前装备的背包重算随身容量。
        /// </summary>
        /// <remarks>
        /// <para>规则：装备了背包就用它的内部格数，没装备就用 5x5 口袋。
        /// 换包时先把现有物品搬进新网格，**任何一件放不下就放弃这次调整**并保留原网格——
        /// 项目里没有地面掉落系统，让物品凭空消失是不可接受的失败方式。</para>
        ///
        /// <para>因此缩容只会在「东西刚好放得下」时发生：背包塞满时把它卸下来，
        /// 容量会暂时保持不变（自愈：等你腾出空间后，下一次变化会重新尝试缩容）。
        /// 这是灰盒阶段的已知放宽，正式版应当改为「拒绝卸下」并给出提示。</para>
        /// </remarks>
        private void RefreshBackpackCapacity()
        {
            if (m_Loadout == null || m_ContainerRegistry == null || m_InventoryScreen == null)
            {
                return;
            }

            var equipped = m_Loadout.Equipment?.Get(EquipmentSlot.Backpack)?.Definition;
            var size = equipped != null && equipped.IsContainer
                ? equipped.ContainerGridSize
                : new GridSize(PocketSize.x, PocketSize.y);

            var current = m_Loadout.Backpack;
            if (current != null && current.Width == size.Width && current.Height == size.Height)
            {
                return;
            }

            var resized = new InventoryGrid(size.Width, size.Height, "主背包");
            var items = current != null ? new List<ItemInstance>(current.Items) : new List<ItemInstance>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var rotatedBefore = item.Rotated;
                if (!resized.AutoPlace(item).Success)
                {
                    // 放弃这次调整：把旋转状态还原，否则旧网格的占位图会与物品对不上。
                    item.Rotated = rotatedBefore;
                    return;
                }
            }

            m_Loadout.ReplaceBackpack(resized);
            m_ContainerRegistry.Replace(m_BackpackContainerId, resized);
            m_InventoryScreen.RebuildLayout(resized, m_BackpackContainerId);
        }

        /// <summary>
        /// 右键菜单的「使用」入口：从指定格子取物品并开始使用。
        /// </summary>
        /// <remarks>
        /// 菜单只负责派发意图，能不能用由这里判断（是否医疗品、是否已在用、是否满血）。
        /// 它与 H 键走同一条路径——否则迟早出现「快捷键能用、菜单点了没反应」这种分叉。
        /// </remarks>
        private void RequestUseItemAt(int containerId, int cellX, int cellY)
        {
            if (m_ItemUse == null || m_ItemUse.IsUsing || m_ContainerRegistry == null)
            {
                return;
            }

            if (!m_ContainerRegistry.TryGetGrid(containerId, out var grid))
            {
                return;
            }

            var item = grid.GetAt(cellX, cellY);
            var medical = ResolveMedical(item);
            if (medical == null || ResolveMissingHealth() <= 0f)
            {
                return;
            }

            m_ItemUse.TryBegin(item, item.Definition.DisplayName, medical.UseDurationSeconds);
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
