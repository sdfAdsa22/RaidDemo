using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Shared;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的背包装配：与战局用的是同一套容器与同一条命令链路。
    /// </summary>
    /// <remarks>
    /// <para>仓库与随身携带物都来自跨场景存活的局外进度，因此安全屋与战局看到的是**同一批物品**：
    /// 在安全屋把枪拖到主武器槽，出击后手上就是那把枪。</para>
    ///
    /// <para>这就是为什么不需要为「局外」单独做一套物品系统——容器抽象在 M2 就打好了地基。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        private EncumbranceProfile m_EncumbranceProfile;
        private System.IDisposable m_ChangedSubscription;
        private MetaProgress m_Progress;
        private TraderCatalog m_TraderCatalog;
        private MerchantScreenController m_MerchantScreen;

        /// <summary>开发期测试箱的容器 ID；0 表示没有（定义被删掉之后就是这样）。</summary>
        private int m_DebugCrateContainerId;

        private void InitializeInventory()
        {
            m_Registry = new ContainerRegistry();
            m_EncumbranceProfile = new EncumbranceProfile();

            var flow = RaidFlowController.Ensure();
            flow.ApplyPendingSave(m_ItemCatalog);
            m_Progress = flow.Progress;
            m_Loadout = m_Progress.Loadout;
            m_BackpackContainerId = m_Registry.Register(m_Loadout.Backpack, ContainerKind.PlayerBackpack);
            m_AmmoPouchContainerId = m_Registry.Register(m_Loadout.AmmoPouch, ContainerKind.AmmoPouch);
            m_StashContainerId = m_Registry.Register(m_Progress.Stash, ContainerKind.Stash);
            BuildDebugCrate();

            var context = new InventoryContext(m_Registry, m_Loadout, m_EventBus);
            m_CommandRouter.Register<InventoryMoveIntent>(new InventoryMoveCommandHandler(context));
            m_CommandRouter.Register<InventoryQuickTransferIntent>(new InventoryQuickTransferCommandHandler(context));
            m_CommandRouter.Register<InventoryRotateIntent>(new InventoryRotateCommandHandler(context));
            m_CommandRouter.Register<InventorySplitIntent>(new InventorySplitCommandHandler(context));
            m_CommandRouter.Register<InventorySortIntent>(new InventorySortCommandHandler(context));
            m_CommandRouter.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(context));
            m_CommandRouter.Register<InventoryUnequipIntent>(new InventoryUnequipCommandHandler(context));
            m_CommandRouter.Register<PlayerSwitchWeaponIntent>(
                new WeaponSwitchCommandHandler(m_Loadout.Equipment, m_EventBus));

            var host = new GameObject("InventoryScreen");
            host.transform.SetParent(transform, worldPositionStays: false);
            m_InventoryScreen = host.AddComponent<InventoryScreenController>();
            m_InventoryScreen.Initialize(
                m_CommandRouter,
                m_Registry,
                m_Loadout,
                m_EventBus,
                m_BackpackContainerId,
                m_AmmoPouchContainerId,
                m_EncumbranceProfile,
                ApplyCursorLock,
                m_ItemCatalog,
                requestUseItem: null);
            m_InventoryScreen.SetStashContainer(m_StashContainerId);
            m_InventoryScreen.InputEnabled = true;

            // 商人经济：货架固定、价格由 TraderPricing 计算，买卖与任务都走命令链路。
            m_TraderCatalog = TraderCatalog.CreateDefault();
            m_CommandRouter.Register<BuyItemIntent>(
                new BuyItemCommandHandler(m_Progress, m_ItemCatalog, m_TraderCatalog, m_EventBus));
            m_CommandRouter.Register<SellItemIntent>(
                new SellItemCommandHandler(m_Progress, m_Registry, m_EventBus));
            m_CommandRouter.Register<QuestAcceptIntent>(
                new QuestAcceptCommandHandler(m_Progress.Quests));
            m_CommandRouter.Register<QuestTrackIntent>(
                new QuestTrackCommandHandler(m_Progress.Quests));
            m_CommandRouter.Register<QuestTurnInIntent>(
                new QuestTurnInCommandHandler(m_Progress.Quests));
            m_CommandRouter.Register<QuestClaimIntent>(
                new QuestClaimCommandHandler(m_Progress.Quests));

            var merchantHost = new GameObject("MerchantScreen");
            merchantHost.transform.SetParent(transform, worldPositionStays: false);
            m_MerchantScreen = merchantHost.AddComponent<MerchantScreenController>();
            m_MerchantScreen.Initialize(
                m_CommandRouter,
                m_Registry,
                m_Progress,
                m_ItemCatalog,
                m_TraderCatalog,
                m_EventBus,
                m_StashContainerId,
                ApplyCursorLock);

            // 在安全屋里换背包也要立刻改格子数——否则玩家会以为背包没用。
            m_ChangedSubscription = m_EventBus.Subscribe<InventoryChangedEvent>(_ => RefreshBackpackCapacity());
            RefreshBackpackCapacity();
        }

        /// <summary>按当前装备的背包重算随身容量（与战局里同一条规则）。</summary>
        private void RefreshBackpackCapacity()
        {
            var equipped = m_Loadout?.Equipment?.Get(EquipmentSlot.Backpack)?.Definition;
            var size = equipped != null && equipped.IsContainer
                ? equipped.ContainerGridSize
                : new GridSize(Meta.MetaProgress.PocketWidth, Meta.MetaProgress.PocketHeight);

            var current = m_Loadout.Backpack;
            if (current == null || (current.Width == size.Width && current.Height == size.Height))
            {
                return;
            }

            var resized = new InventoryGrid(size.Width, size.Height, "主背包");
            var items = new System.Collections.Generic.List<ItemInstance>(current.Items);
            for (var i = 0; i < items.Count; i++)
            {
                var rotated = items[i].Rotated;
                if (!resized.AutoPlace(items[i]).Success)
                {
                    items[i].Rotated = rotated;
                    return;
                }
            }

            m_Loadout.ReplaceBackpack(resized);
            m_Registry.Replace(m_BackpackContainerId, resized);
            m_InventoryScreen.RebuildLayout(resized, m_BackpackContainerId);
            m_InventoryScreen.SetStashContainer(m_StashContainerId);
        }

        /// <summary>把光标锁定开关交给背包界面调用。</summary>
        private void ApplyCursorLock(bool locked)
        {
            m_InputCollector?.SetCursorLock(locked);
        }

        /// <summary>
        /// 生成开发期测试箱：固定产出武器、护甲、头盔、背包。
        /// </summary>
        /// <remarks>
        /// <para>放在安全屋而不是战局地图里：它是**准备装备**用的，
        /// 而准备动作本来就发生在安全屋。放进战局还会污染那一局的掉落与结算数据。</para>
        ///
        /// <para>目录里找不到 <c>crate.debug</c> 时静默跳过——删除测试箱时
        /// 只需要删定义与场景标记，这段代码不需要动。</para>
        /// </remarks>
        private void BuildDebugCrate()
        {
            var definition = RaidDemo.Raid.LootContainerCatalog.Get("crate.debug");
            if (definition == null)
            {
                return;
            }

            var grid = new InventoryGrid(
                definition.GridSize.Width,
                definition.GridSize.Height,
                definition.DisplayName);
            m_DebugCrateContainerId = m_Registry.Register(grid, ContainerKind.Loot);

            var roller = new RaidDemo.Raid.LootRoller(
                m_ItemCatalog,
                new DeterministicRandom(20260912u),
                new ItemFactory());
            roller.Roll(definition.Table, grid, definition.FixedContents);
        }
    }
}
