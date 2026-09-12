using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Raid;
using RaidDemo.Shared;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的战局装配部分（M5 战局闭环）。
    /// </summary>
    /// <remarks>
    /// <para>这一部分把四件事接起来：地图上的容器、搜刮读条、撤离读秒、战局结算。
    /// 逻辑全部在 <c>RaidDemo.Raid</c> 里，本文件只负责「谁在什么时候被调用」。</para>
    ///
    /// <para>与背包、战斗、AI 的装配拆成不同文件，是因为它们的关注点不同：
    /// 这里关心的是「一局从开始到结束要经过哪些步骤」。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>一局战局时长（秒）。</summary>
        [SerializeField] private float m_RaidDurationSeconds = 480f;

        /// <summary>撤离读秒时长（秒）。</summary>
        [SerializeField] private float m_ExtractionDurationSeconds = 10f;

        /// <summary>搜刮读条时长（秒）。</summary>
        [SerializeField] private float m_LootSearchDurationSeconds = 2f;

        /// <summary>搜刮交互的最大距离（米）。</summary>
        [SerializeField] private float m_LootSearchRangeMeters = 2.2f;

        /// <summary>
        /// 战利品随机种子。
        /// </summary>
        /// <remarks>
        /// 0 表示每局随机；填一个非零值就会固定掉落，便于复现「某一局为什么开出这些东西」。
        /// 随机源统一走 <c>IRandomProvider</c>，联机时可以换成服务端下发的种子。
        /// </remarks>
        [SerializeField] private int m_LootSeed;

        /// <summary>地图上的一个战利品容器在运行时的形态。</summary>
        private sealed class LootContainerRuntime
        {
            /// <summary>创建运行时容器。</summary>
            public LootContainerRuntime(
                int containerId,
                LootContainerDefinition definition,
                Vector3 worldPosition,
                float rangeMeters)
            {
                ContainerId = containerId;
                Definition = definition;
                WorldPosition = worldPosition;
                RangeMeters = rangeMeters;
            }

            /// <summary>容器注册表里的 ID。</summary>
            public int ContainerId { get; }

            /// <summary>容器定义。</summary>
            public LootContainerDefinition Definition { get; }

            /// <summary>容器的世界位置（取箱体中心）。</summary>
            public Vector3 WorldPosition { get; }

            /// <summary>可以触发搜刮的距离（米）。</summary>
            public float RangeMeters { get; }
        }

        private RaidSettings m_RaidSettings;
        private RaidSession m_RaidSession;
        private ExtractionTracker m_ExtractionTracker;
        private LootSearchInteraction m_LootSearch;

        /// <summary>使用物品的读条器（M5.5：医疗品）。</summary>
        private ItemUseInteraction m_ItemUse;

        /// <summary>本帧玩家是否受了伤。受伤会打断正在进行的物品使用。</summary>
        private bool m_PlayerDamagedThisFrame;
        private RaidHudView m_RaidHud;
        private List<LootContainerRuntime> m_LootContainers;
        private LootContainerRuntime m_NearbyLoot;
        private int m_BroughtInValue;

        /// <summary>
        /// 战局是否已经开始。
        /// </summary>
        /// <remarks>
        /// 主菜单状态下本值为 false，主循环与光标逻辑据此跳过战局部分。
        /// 用一个显式标志而不是散落各处的 null 检查：每新增一个可选系统，
        /// 就不必再补一处判断。
        /// </remarks>
        private bool m_RaidActive;

        private bool m_RaidResultShown;
        private IDisposable m_KillSubscription;
        private IDisposable m_RaidEndedSubscription;
        private IDisposable m_LootSearchSubscription;
        private IDisposable m_ExtractionSubscription;
        private IDisposable m_ItemUseSubscription;
        private IDisposable m_ArmorSubscription;
        private IDisposable m_BackpackSubscription;

        /// <summary>战局会话，供测试与调试读取。</summary>
        public RaidSession Raid
        {
            get { return m_RaidSession; }
        }

        /// <summary>地图上的战利品容器数量，供调试读取。</summary>
        public int LootContainerCount
        {
            get { return m_LootContainers != null ? m_LootContainers.Count : 0; }
        }

        /// <summary>
        /// 装配战局闭环。
        /// </summary>
        /// <remarks>
        /// 必须在玩家与 AI 装配之后调用：结算需要玩家的战斗单位标识来统计击杀，
        /// 而那个标识是在 AI 装配阶段登记的。
        /// </remarks>
        private void InitializeRaid()
        {
            m_RaidActive = true;
            m_RaidSettings = new RaidSettings
            {
                RaidDurationSeconds = m_RaidDurationSeconds,
                ExtractionDurationSeconds = m_ExtractionDurationSeconds,
            };

            var settingsProblem = m_RaidSettings.Validate();
            if (settingsProblem != null)
            {
                Debug.LogError($"[RaidDemo] 战局参数不合法：{settingsProblem}。将使用默认值继续运行。", this);
                m_RaidSettings = new RaidSettings();
            }

            m_LootSearch = new LootSearchInteraction(
                m_LootSearchDurationSeconds,
                movementToleranceMeters: 0.35f,
                m_EventBus);

            m_ItemUse = new ItemUseInteraction(m_EventBus);

            BuildExtractionTracker();
            BuildLootContainers();
            BuildRaidHud();

            m_BroughtInValue = RaidResult.ComputeCarriedValue(m_Loadout);
            m_RaidSession = new RaidSession(m_RaidSettings, m_EventBus, m_PlayerCombatantId);
            m_RaidResultShown = false;

            m_KillSubscription = m_EventBus.Subscribe<DamageAppliedEvent>(OnKillCounted);
            m_RaidEndedSubscription = m_EventBus.Subscribe<RaidEndedEvent>(OnRaidEnded);
            m_LootSearchSubscription = m_EventBus.Subscribe<LootSearchCompletedEvent>(OnLootSearchCompleted);
            m_ExtractionSubscription = m_EventBus.Subscribe<ExtractionCompletedEvent>(OnExtractionCompleted);
            m_ItemUseSubscription = m_EventBus.Subscribe<ItemUseCompletedEvent>(OnItemUseCompleted);
            m_ArmorSubscription = m_EventBus.Subscribe<InventoryChangedEvent>(_ => RefreshPlayerArmor());
            m_BackpackSubscription = m_EventBus.Subscribe<InventoryChangedEvent>(_ => RefreshBackpackCapacity());

            // 带入价值必须在开战前统计，之后背包里的东西就分不清「本来就有的」与「刚搜到的」了。
            m_RaidSession.Start(m_BroughtInValue);

            // 开局先按当前装备算一次容量：默认没有背包时应当是口袋大小。
            RefreshBackpackCapacity();
        }

        /// <summary>从场景标记构建撤离点列表。</summary>
        /// <remarks>
        /// 找不到任何标记时补一个位于出生点正北的撤离点并报错：宁可让这一局还能打完，
        /// 也不要把玩家困在一个无法结束的战局里——那会让人以为游戏卡死了。
        /// </remarks>
        private void BuildExtractionTracker()
        {
            var markers = UnityEngine.Object.FindObjectsByType<ExtractionZoneMarker>(FindObjectsSortMode.None);
            var zones = new List<ExtractionZone>(markers.Length);
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                var position = marker.transform.position;
                zones.Add(new ExtractionZone(
                    marker.ZoneId,
                    marker.DisplayName,
                    new Vector2F(position.x, position.z),
                    marker.Radius));
            }

            if (zones.Count == 0)
            {
                Debug.LogError(
                    "[RaidDemo] 场景里没有任何撤离点标记，已临时补一个。"
                    + "请执行菜单「RaidDemo → 生成灰盒测试场景」重新生成地图。",
                    this);
                zones.Add(new ExtractionZone(99, "临时撤离点", new Vector2F(0f, 25.5f), 3.5f));
            }

            m_ExtractionTracker = new ExtractionTracker(
                zones,
                m_RaidSettings.ExtractionDurationSeconds,
                m_EventBus);
        }

        /// <summary>按场景标记生成容器、抽取掉落并登记到容器注册表。</summary>
        private void BuildLootContainers()
        {
            m_LootContainers = new List<LootContainerRuntime>(16);

            var spawnPoints = UnityEngine.Object.FindObjectsByType<LootSpawnPoint>(FindObjectsSortMode.None);
            if (spawnPoints.Length == 0)
            {
                Debug.LogWarning(
                    "[RaidDemo] 地图上没有任何战利品容器标记，这一局将搜不到东西。"
                    + "请执行菜单「RaidDemo → 生成灰盒测试场景」重新生成地图。",
                    this);
                return;
            }

            if (m_ItemCatalog == null)
            {
                Debug.LogError("[RaidDemo] 未指定物品目录，战利品容器将全部是空的。", this);
            }

            var seed = m_LootSeed != 0 ? (uint)m_LootSeed : (uint)Environment.TickCount;
            var roller = new LootRoller(m_ItemCatalog, new DeterministicRandom(seed), new ItemFactory());

            for (var i = 0; i < spawnPoints.Length; i++)
            {
                var point = spawnPoints[i];
                var definition = LootContainerCatalog.Get(point.ContainerDefinitionId);
                if (definition == null)
                {
                    Debug.LogWarning(
                        $"[RaidDemo] 未知的容器定义 ID「{point.ContainerDefinitionId}」，已跳过该容器。",
                        point);
                    continue;
                }

                var grid = new InventoryGrid(
                    definition.GridSize.Width,
                    definition.GridSize.Height,
                    definition.DisplayName);
                var containerId = m_ContainerRegistry.Register(grid, ContainerKind.Loot);

                // 掉落在这里一次抽完：搜刮读条只是「打开箱子的成本」，
                // 而不是「逐件抽取」——逐件抽取会让玩家在读条时就猜出箱子里有什么。
                roller.Roll(definition.Table, grid, definition.FixedContents);

                m_LootContainers.Add(new LootContainerRuntime(
                    containerId,
                    definition,
                    point.transform.position,
                    m_LootSearchRangeMeters));
            }
        }

        /// <summary>创建战局界面。</summary>
        private void BuildRaidHud()
        {
            var host = new GameObject("RaidHud");
            host.transform.SetParent(transform, worldPositionStays: false);
            m_RaidHud = host.AddComponent<RaidHudView>();
            m_RaidHud.Initialize();
            m_RaidHud.SetKills(0);
        }

        /// <summary>把战局状态写进界面。</summary>
        private void UpdateRaidHud(bool playerAlive)
        {
            if (m_RaidHud == null)
            {
                return;
            }

            m_RaidHud.SetTimer(m_RaidSession.RemainingSeconds, m_RaidSession.IsActive);
            m_RaidHud.SetKills(m_RaidSession.Kills);

            var searching = m_LootSearch != null && m_LootSearch.IsSearching;
            var searchingContainer = searching ? FindLootContainer(m_LootSearch.TargetContainerId) : null;
            m_RaidHud.SetSearchProgress(
                searching,
                m_LootSearch != null ? m_LootSearch.Progress01 : 0f,
                searchingContainer != null ? $"搜刮中：{searchingContainer.Definition.DisplayName}" : "搜刮中…");

            var usingItem = m_ItemUse != null && m_ItemUse.IsUsing;
            m_RaidHud.SetUseProgress(
                usingItem,
                usingItem ? m_ItemUse.Progress01 : 0f,
                usingItem ? $"使用中：{m_ItemUse.DisplayName}" : null);

            if (m_InventoryScreen != null && m_InventoryScreen.IsOpen)
            {
                // 背包打开时不需要交互提示与撤离提示：玩家的注意力在物品上，
                // 而且此时输入被屏蔽，提示会变成无法执行的噪声。
                m_RaidHud.SetInteractionPrompt(null);
                m_RaidHud.SetExtraction(false, null, 0f);
                return;
            }

            var prompt = !searching && playerAlive && m_NearbyLoot != null
                ? $"按 E 搜索 {m_NearbyLoot.Definition.DisplayName}"
                : null;
            m_RaidHud.SetInteractionPrompt(prompt);

            var zone = m_ExtractionTracker != null ? m_ExtractionTracker.ActiveZone : null;
            var extracting = zone != null && m_ExtractionTracker.ProgressSeconds > 0f;
            m_RaidHud.SetExtraction(
                extracting,
                zone != null ? zone.DisplayName : string.Empty,
                m_ExtractionTracker != null ? m_ExtractionTracker.Progress01 : 0f);
        }

        /// <summary>把当前装备的头盔与护甲同步到战斗单位。</summary>
        /// <remarks>
        /// 订阅「背包变化」而不是「装备变化」：M2 没有单独的装备事件，而每次换装必然伴随背包变化。
        /// <b>没换装就直接返回</b>：SetArmor 会把耐久重置为满，每次整理背包都调用等于免费修甲。
        /// </remarks>
        private void RefreshPlayerArmor()
        {
            if (m_CombatWorld == null || m_PlayerCombatantId == 0 || m_Loadout == null)
            {
                return;
            }

            var helmet = m_Loadout.Equipment?.Get(EquipmentSlot.Head)?.Definition?.ArmorStats;
            var vest = m_Loadout.Equipment?.Get(EquipmentSlot.Body)?.Definition?.ArmorStats;
            if (ReferenceEquals(helmet, m_LastAppliedHelmet) && ReferenceEquals(vest, m_LastAppliedVest))
            {
                return;
            }

            if (m_CombatWorld.TryGet(m_PlayerCombatantId, out var state))
            {
                state.SetArmor(helmet, vest);
                m_LastAppliedHelmet = helmet;
                m_LastAppliedVest = vest;
            }
        }

        /// <summary>玩家当前是否存活。</summary>
        private bool IsPlayerAlive()
        {
            if (m_CombatWorld == null || m_PlayerCombatantId == 0)
            {
                return false;
            }

            return m_CombatWorld.TryGet(m_PlayerCombatantId, out var state) && state.IsAlive;
        }

        /// <summary>统计玩家造成的击杀，并记录「本帧挨打」用于打断物品使用。</summary>
        /// <remarks>
        /// 只认「攻击方是玩家」且「本次确实打死」的伤害事件。
        /// 让 AI 之间互相误伤也计入击杀会让结算数字变得不可信。
        /// </remarks>
        private void OnKillCounted(DamageAppliedEvent evt)
        {
            if (evt.TargetId == m_PlayerCombatantId)
            {
                m_PlayerDamagedThisFrame = true;
            }

            if (!evt.WasKilled || evt.AttackerId != m_PlayerCombatantId)
            {
                return;
            }

            m_RaidSession?.NotifyKill(evt.AttackerId);
        }

        /// <summary>按容器 ID 查找运行时容器。</summary>
        private LootContainerRuntime FindLootContainer(int containerId)
        {
            if (m_LootContainers == null)
            {
                return null;
            }

            for (var i = 0; i < m_LootContainers.Count; i++)
            {
                if (m_LootContainers[i].ContainerId == containerId)
                {
                    return m_LootContainers[i];
                }
            }

            return null;
        }

    }
}
