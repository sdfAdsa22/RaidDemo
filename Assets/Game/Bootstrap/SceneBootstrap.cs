using System.Collections.Generic;
using RaidDemo.Input;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.Simulation;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 场景装配入口：把模拟、命令路由、输入与表现连接起来。
    /// </summary>
    /// <remarks>
    /// <para>这是单机模式下的组合根（Composition Root）。它负责创建各层实例、
    /// 完成依赖注入与命令处理器注册，然后驱动每帧循环。</para>
    ///
    /// <para>它是唯一允许同时引用所有模块的地方。其余模块之间只能通过接口、
    /// 命令与事件通信，不得互相直接引用。</para>
    ///
    /// <para>联机模式下，这套装配的职责会拆分到客户端与服务端两侧，
    /// 但模拟与命令链路保持不变——这正是把逻辑从 MonoBehaviour 中剥离出来的价值。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class SceneBootstrap : MonoBehaviour
    {
        /// <summary>玩家初始位置。</summary>
        [SerializeField] private Vector2 m_PlayerSpawnPosition = Vector2.zero;

        /// <summary>玩家初始朝向（度）。0 度指向世界 X 轴正方向。</summary>
        [SerializeField] private float m_PlayerSpawnFacingDegrees;

        /// <summary>玩家碰撞体半径（米）。与场景生成器里创建胶囊时用的值保持一致。</summary>
        private const float PlayerBodyRadius = 0.4f;

        /// <summary>玩家身高（米）。用于移动碰撞的胶囊扫掠。</summary>
        private const float PlayerBodyHeight = 1.8f;

        /// <summary>玩家表现层组件。</summary>
        [SerializeField] private PlayerMotor m_PlayerMotor;

        /// <summary>输入采集组件。</summary>
        [SerializeField] private PlayerInputCollector m_InputCollector;

        /// <summary>跟随相机。</summary>
        [SerializeField] private TopDownCameraController m_CameraController;

        /// <summary>瞄准准星。未指定时会在运行时自动创建。</summary>
        [SerializeField] private AimCrosshair m_Crosshair;

        /// <summary>物品目录。留空时背包系统仍然可用，但没有可搜刮的物品。</summary>
        [SerializeField] private ItemCatalog m_ItemCatalog;

        /// <summary>主背包的网格尺寸（列 x 行）。</summary>
        [SerializeField] private Vector2Int m_BackpackSize = new Vector2Int(5, 5);

        /// <summary>弹药挂的格数。固定为一行，横向排列。</summary>
        [SerializeField] private int m_AmmoPouchCells = 5;

        /// <summary>
        /// 承载上限（千克）。
        /// </summary>
        /// <remarks>
        /// 从 20 提高到 50：旧值下一套基础装备（步枪 + 头盔 + 背心 ≈ 14.7 kg）就已经进入重装，
        /// 玩家还没开始搜刮就跑不动了。50 kg 让「穿装备」与「装战利品」不再互相挤占，
        /// 而贪婪的代价仍然由重量与速度承担。
        /// </remarks>
        [SerializeField] private float m_CarryCapacityKg = 50f;

        private EventBus m_EventBus;
        private ServiceLocator m_Services;
        private CommandRouter m_CommandRouter;
        private PlayerMovementProfile m_MovementProfile;
        private PlayerMoveCommandHandler m_MoveHandler;

        private ContainerRegistry m_ContainerRegistry;
        private PlayerLoadout m_Loadout;
        private EncumbranceProfile m_EncumbranceProfile;
        private InventoryScreenController m_InventoryScreen;
        private int m_BackpackContainerId;
        private int m_AmmoPouchContainerId;

        /// <summary>上一帧的负重状态。只有它发生变化时才重新计算移动修正并广播事件。</summary>
        private EncumbranceState m_LastEncumbranceState = EncumbranceState.Light;

        private bool m_HasEncumbranceState;

        /// <summary>当前帧的移动意图。</summary>
        private Vector2F m_PendingMoveIntent;

        private Vector2F m_PendingLookDirection;
        private bool m_PendingWantsToSprint;

        /// <summary>命令序号，单调递增。单机模式下仅用于调试记录，联机时用于丢包检测与回滚重放。</summary>
        private uint m_CommandSequence;

        /// <summary>命令路由实例，供调试与测试使用。</summary>
        public CommandRouter Router => m_CommandRouter;

        /// <summary>移动模拟器，供调试与测试读取状态。</summary>
        public PlayerMovementSimulator Movement => m_MoveHandler?.Simulator;

        /// <summary>容器注册表，供调试与测试读取。</summary>
        public ContainerRegistry Containers => m_ContainerRegistry;

        /// <summary>角色携带物，供调试与测试读取。</summary>
        public PlayerLoadout Loadout => m_Loadout;

        /// <summary>背包界面，供调试与测试读取。</summary>
        public InventoryScreenController InventoryScreen => m_InventoryScreen;

        /// <summary>
        /// 初始化顺序设为很早，保证其他组件的 OnEnable 能查询到已注册的服务。
        /// </summary>
        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            // 释放订阅。AI 调度器订阅了伤害事件，漏掉这一步会在退出播放模式时留下悬挂引用。
            m_WeaponNoiseSubscription?.Dispose();
            m_WeaponNoiseSubscription = null;
            m_KillSubscription?.Dispose();
            m_KillSubscription = null;
            m_RaidEndedSubscription?.Dispose();
            m_RaidEndedSubscription = null;
            m_LootSearchSubscription?.Dispose();
            m_LootSearchSubscription = null;
            m_ExtractionSubscription?.Dispose();
            m_ExtractionSubscription = null;
            m_ItemUseSubscription?.Dispose();
            m_ItemUseSubscription = null;
            m_AiDirector?.Dispose();
            ServiceLocatorHolder.Clear();
            m_Services?.Clear();
        }

        private void Update()
        {
            // 主菜单状态下战局逻辑一律不推进：菜单只是「站在地图上的一个界面」，
            // 此时角色不该移动、敌人不该思考、计时不该走。
            if (m_MoveHandler == null || !m_RaidActive)
            {
                return;
            }

            var inventoryOpen = m_InventoryScreen != null && m_InventoryScreen.IsOpen;

            // 阵亡期间也屏蔽操作：否则玩家在倒计时里还能跑动与开枪，
            // 会让"已经死了"这件事完全无法从画面上看出来。
            var inputBlocked = inventoryOpen || IsPlayerDefeated();

            // 编辑器在失去焦点时会自动解除光标锁定；玩家点回游戏窗口后需要重新锁上。
            // 这里只在应用有焦点时维持锁定，避免与操作系统的焦点切换互相抢控制权。
            // 背包界面打开时不抢光标：那时玩家需要鼠标来拖拽物品。
            if (!inventoryOpen
                && m_RaidActive
                && m_InputCollector != null
                && Application.isFocused
                && Cursor.lockState != CursorLockMode.Locked)
            {
                m_InputCollector.SetCursorLock(true);
            }

            if (inputBlocked)
            {
                // 翻背包时角色必须立刻停住。只清意图而不停模拟，角色会按上一次的
                // 输入继续滑行，玩家一松手就发现人物跑出掩体了。
                m_MoveHandler.ClearIntent();
            }
            else
            {
                CollectInput();
                DispatchMoveCommand();
            }

            // 命令只更新意图，模拟推进由 Tick 完成：
            // 这样即使某帧没有输入，角色也会按上一次意图继续移动。
            m_MoveHandler.Tick(Time.deltaTime);

            // 把最新位置回写给输入层，使下一帧的瞄准方向基于最新位置计算。
            if (m_InputCollector != null && m_PlayerMotor != null)
            {
                m_InputCollector.SetOriginPosition(m_PlayerMotor.SimulatedPosition);
                m_InputCollector.SetOriginHeight(m_PlayerMotor.transform.position.y);
            }

            if (inventoryOpen)
            {
                // 翻背包时准星没有意义，而且它会盖在面板上，因此直接隐藏。
                if (m_Crosshair != null)
                {
                    m_Crosshair.Hide();
                }
            }
            else
            {
                UpdateCrosshair();
            }

            UpdateEncumbrance();
            UpdateCombat(Time.deltaTime, inputBlocked);
            UpdateAi(Time.deltaTime, inputBlocked);
            UpdateRaid(Time.deltaTime, inputBlocked);
        }

        /// <summary>
        /// 建立全部服务与对象，并完成命令处理器注册。
        /// </summary>
        /// <remarks>
        /// 各步骤的顺序有依赖：事件总线必须先注册，表现层组件才能在 OnEnable 中订阅。
        /// </remarks>
        public void Initialize()
        {
            // 编辑器在失去焦点时会停掉帧循环，而本项目大量依赖「后台跑着」的开发方式：
            // 自动化验证、录制演示、切到别的窗口看日志，都会因此得到一个时间不走的假象。
            // 显式要求后台继续运行，把这个坑堵在源头。
            Application.runInBackground = true;

            m_Services = new ServiceLocator();
            m_EventBus = new EventBus();
            m_CommandRouter = new CommandRouter();

            m_Services.Register(m_EventBus);
            m_Services.Register(m_CommandRouter);
            m_Services.Register(new LogService(LogLevel.Info));
            ServiceLocatorHolder.Set(m_Services);

            m_MovementProfile = new PlayerMovementProfile();
            var profileError = m_MovementProfile.Validate();
            if (profileError != null)
            {
                Debug.LogError($"[RaidDemo] 移动配置不合法：{profileError}。将使用默认值继续运行。", this);
                m_MovementProfile.ResetToDefault();
            }

            var spawnFacing = Vector2F.FromDegrees(m_PlayerSpawnFacingDegrees);

            // 移动碰撞：没有它，玩家能直接穿过集装箱与厂房隔断，
            // 地图上的掩体对玩家来说就等于不存在。
            // 实现放在表现层（要用 PhysX），模拟层只认识接口。
            var collisionWorld = m_PlayerMotor != null
                ? new PhysicsMovementCollisionService(
                    m_PlayerMotor.transform,
                    PlayerBodyHeight,
                    skin: 0.02f)
                : null;

            var simulator = new PlayerMovementSimulator(
                m_MovementProfile,
                new Vector2F(m_PlayerSpawnPosition.x, m_PlayerSpawnPosition.y),
                spawnFacing,
                collisionWorld,
                PlayerBodyRadius);

            m_MoveHandler = new PlayerMoveCommandHandler(simulator, m_EventBus);
            m_CommandRouter.Register(m_MoveHandler);

            InitializeInventory();
            InitializeCombat();

            // 流程状态决定这一局是否真的开始。主菜单状态下不生成敌人、不抽掉落、不开始计时，
            // 但地图与背包仍然装配完成——这样菜单背景就是真实的战局场景，
            // 玩家点「出击」之后看到的画面与菜单里看到的是同一个地方。
            var flow = RaidFlowController.Ensure();
            if (flow.State == RaidFlowController.FlowState.InRaid)
            {
                InitializeAi();
                InitializeRaid();
                m_RaidActive = true;
                flow.HideScreens();
            }
            else
            {
                // 战局未开始：禁止背包界面响应按键，否则在主菜单里按 Tab 会弹出背包面板。
                m_InventoryScreen.InputEnabled = false;
                if (m_CombatHud != null)
                {
                    m_CombatHud.SetVisible(false);
                }
                flow.ShowMainMenu();
            }

            // 表现层需要在事件总线就绪之后重新订阅，否则 OnEnable 阶段拿不到服务。
            if (m_PlayerMotor != null)
            {
                m_PlayerMotor.Rebind(m_EventBus);
                m_PlayerMotor.SnapTo(m_PlayerSpawnPosition, new Vector2(spawnFacing.X, spawnFacing.Y));
            }

            if (m_CameraController != null && m_PlayerMotor != null)
            {
                m_CameraController.SetTarget(m_PlayerMotor.transform, snap: true);
            }

            EnsureCrosshair();

            if (m_InputCollector == null)
            {
                Debug.LogWarning("[RaidDemo] 未指定输入采集组件，玩家将无法操作。", this);
            }
            else
            {
                // 进入游戏即锁定并隐藏鼠标光标，由准星代替光标指示瞄准位置。
                m_InputCollector.SetCursorLock(true);
            }
        }

    }
}
