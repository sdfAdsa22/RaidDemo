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

        /// <summary>
        /// 表现层资产目录：音效、武器模型与战斗特效。
        /// </summary>
        /// <remarks>留空时全部表现自动退回灰盒与静默，游戏逻辑不受影响。</remarks>
        [SerializeField] private PresentationCatalog m_PresentationCatalog;

        /// <summary>
        /// 承载上限（千克）。
        /// </summary>
        /// <remarks>
        /// 从 20 提高到 50：旧值下一套基础装备（步枪 + 头盔 + 背心 ≈ 14.7 kg）就已经进入重装，
        /// 玩家还没开始搜刮就跑不动了。50 kg 让「穿装备」与「装战利品」不再互相挤占，
        /// 而贪婪的代价仍然由重量与速度承担。
        /// </remarks>
        [SerializeField] private float m_CarryCapacityKg = 50f;

        /// <summary>
        /// 会话作用域：事件总线、服务定位器与命令路由的持有者（M9 · P0 起由场景内直接创建改为会话化）。
        /// </summary>
        private SessionScope m_Session;

        private EventBus m_EventBus;
        private CommandRouter m_CommandRouter;
        private PlayerMovementProfile m_MovementProfile;
        private PlayerMoveCommandHandler m_MoveHandler;

        private ContainerRegistry m_ContainerRegistry;
        private PlayerLoadout m_Loadout;
        private EncumbranceProfile m_EncumbranceProfile;
        private InventoryScreenController m_InventoryScreen;
        private int m_BackpackContainerId;
        private int m_AmmoPouchContainerId;

        /// <summary>仓库的容器 ID（本场景内有效；网格本身跨场景存活）。</summary>
        private int m_StashContainerId;

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
            // 服务器进程不需要客户端装配：相机、输入、界面、玩家表现都属于客户端。
            // 服务器自己的会话由 ServerEntryPoint 建立（见 Session/ServerRuntime.cs）。
            if (ServerMode.IsActive)
            {
                // 但服务器需要场景里的内容目录（物品定义等）来构建权威逻辑，
                // 因此销毁自己之前先把它交出去。
                ServerMode.AcceptSceneContent(m_ItemCatalog, m_PresentationCatalog);
                Destroy(gameObject);
                return;
            }

            ApplyHotUpdateContent();   // 客户端：先用当前生效的内容解析目录（本地缓存优先）
            Initialize();
        }

        private void OnDestroy()
        {
            if (m_MetaProgress != null)
            {
                m_MetaProgress.Changed -= RefreshQuestTracker;
            }

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
            m_ArmorSubscription?.Dispose();
            m_ArmorSubscription = null;
            m_BackpackSubscription?.Dispose();
            m_BackpackSubscription = null;
            m_CodexMarker?.Dispose();
            m_CodexMarker = null;
            m_AiDirector?.Dispose();

            // 联机客户端：把战局专属的网络通道退订干净。
            // 网络管理器由大厅会话持有、跨场景存活，这里不退订的话，
            // 下一局加载战局场景时同名处理器注册不上（第二局会"看不到队友与敌人"）。
            DetachFromServer();

            m_Session?.Dispose();
            m_Session = null;
        }

        private void Update()
        {
            var flow = RaidFlowController.Ensure();
            var escapePressed = m_InputCollector != null && m_InputCollector.ReadPauseIntent();

            if (flow.IsPaused)
            {
                // 暂停期间只接受"继续"；其它输入全部冻结。
                if (escapePressed)
                {
                    flow.ResumeFromPause();
                }

                return;
            }

            // 主菜单状态下战局逻辑一律不推进：菜单只是「站在地图上的一个界面」，
            // 此时角色不该移动、敌人不该思考、计时不该走。
            if (m_MoveHandler == null || !m_RaidActive)
            {
                UpdatePreparation();
                return;
            }

            var inventoryOpen = m_InventoryScreen != null && m_InventoryScreen.IsOpen;
            var resultOpen = flow.State == RaidFlowController.FlowState.Result;

            // Esc 打开暂停菜单的判定拆在 SceneBootstrap.Pause.cs：见那里的注释。
            if (TryHandleRaidPauseInput(flow, escapePressed, inventoryOpen, resultOpen))
            {
                return;
            }

            // 阵亡期间也屏蔽操作：否则玩家在倒计时里还能跑动与开枪，
            // 会让"已经死了"这件事完全无法从画面上看出来。
            var inputBlocked = inventoryOpen || resultOpen || IsPlayerDefeated();

            // 编辑器在失去焦点时会自动解除光标锁定；玩家点回游戏窗口后需要重新锁上。
            // 这里只在应用有焦点时维持锁定，避免与操作系统的焦点切换互相抢控制权。
            // 背包界面打开时不抢光标：那时玩家需要鼠标来拖拽物品。
            var shouldLockCursor = CursorLockPolicy.ShouldLockCursor(inventoryOpen, resultOpen);
            if (resultOpen)
            {
                // 结算界面需要鼠标；ShowResult 解锁之后这里绝不能再把它锁回去。
                m_InputCollector?.ReleaseCursor();
            }
            else if (shouldLockCursor
                && m_RaidActive
                && m_InputCollector != null
                && Application.isFocused
                && Cursor.lockState != CursorLockMode.Locked)
            {
                m_InputCollector.SetCursorLock(true);
            }

            // 先回写最新位置再采集输入：瞄准点必须基于最新位置（否则开局第一帧会用默认 (0,0)）。
            if (m_InputCollector != null && m_PlayerMotor != null)
            {
                m_InputCollector.SetOriginPosition(m_PlayerMotor.SimulatedPosition);
                m_InputCollector.SetOriginHeight(m_PlayerMotor.transform.position.y + MuzzleHeight);   // 瞄准平面 = 枪口高度（U-95 方案 A）
            }

            if (inputBlocked)
            {
                // 翻背包时角色必须立刻停住。只清意图而不停模拟，角色会按上一次的
                // 输入继续滑行，玩家一松手就发现人物跑出掩体了。
                //
                // 联机时还要连"待上行意图"一起清：上行用的是这几个字段，
                // 只清本地意图的话服务器会继续按上一帧的方向移动玩家（客户端则原地不动，
                // 每次快照都把它往前拉一下）——P4.5-b 把这条规则统一到两个场景。
                ClearLocalMovementIntent();
            }
            else
            {
                CollectInput();
                DispatchMoveCommand();
            }

            // 命令只更新意图，模拟推进由 Tick 完成：
            // 这样即使某帧没有输入，角色也会按上一次意图继续移动。
            //
            // 联机客户端的推进方式不同：它必须按 60 Hz 固定步长跑（与服务器一致），
            // 每个固定步产生一条输入并记录一条预测，因此这里只做分流。
            if (IsMultiplayerClient)
            {
                // 网络节拍要用**未缩放时间**：结算 / 暂停 / 角色选择会把 timeScale 压成 0，
                // 而帧循环仍在跑。如果节拍跟着缩放时间走，客户端就会停止发送任何协议消息，
                // 服务器在 10 秒（NGO 协议超时）后把连接踢掉——表现是"结算界面停留一会儿就掉线"
                // （2026-09-14 联机基础问题修复的定位结论）。
                TickMultiplayerClient(Time.deltaTime, Time.unscaledDeltaTime);
            }
            else
            {
                m_MoveHandler.Tick(Time.deltaTime);
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

            // 会话作用域负责创建与释放会话级服务，并把它们注册为当前场景的静态入口。
            // 单机走的是「本机内嵌服务器」形态：权威逻辑与客户端同进程，但边界已经按服务器设计。
            // 联机客户端按启动参数决定日志等级：诊断联机问题时，
            // 客户端侧的证据（"我到底发了什么"）和服务器侧同样重要。
            m_Session = IsMultiplayerProcess && ClientMode.Options != null
                ? new SessionScope("联机客户端", ClientMode.Options.MinimumLogLevel)
                : SessionScope.CreateLocal();
            m_EventBus = m_Session.Events;
            m_CommandRouter = m_Session.Commands;

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

            // 联机客户端：连上服务器就等于已经在战局里。停在主菜单不仅语义不对，
            // 还会把 timeScale 压成 0——网络栈依赖时间推进，那样连接永远建不起来。
            if (IsMultiplayerProcess && flow.State != RaidFlowController.FlowState.InRaid)
            {
                flow.EnterRaidDirectly();
            }

            if (flow.State == RaidFlowController.FlowState.InRaid)
            {
                // P2-2 起 AI 只在服务器上跑：联机客户端不再自己生成敌人。
                // 否则同一张地图上会有两套互相独立的 AI —— 位置、状态与伤害判定都不一致，
                // 而且客户端那套并不权威（打死了服务器也不认）。
                // 客户端要做的只是把服务器给的结果画出来，那部分在 20.5 接入。
                if (!IsMultiplayerProcess)
                {
                    InitializeAi();
                }

                InitializeRaid();
                m_RaidActive = true;
                // 战局里右侧面板留给战利品，不自动显示仓库。
                m_InventoryScreen.SetStashContainer(0);
                flow.HideScreens();
            }
            else
            {
                // 战局未开始：界面仍然接受输入，因为主菜单阶段就是「出击准备」——
                // 玩家按 Tab 打开仓库，把装备与弹药搬到身上，再按 Enter 出发。
                m_InventoryScreen.InputEnabled = true;
                // 准备界面：界面打开时右侧面板显示仓库（战局里这个值会被清掉，
                // 那时右侧面板只在搜刮读条完成后出现）。
                m_InventoryScreen.SetStashContainer(m_StashContainerId);
                if (m_CombatHud != null)
                {
                    m_CombatHud.SetVisible(false);
                }
                flow.ShowMainMenu();
            }

            // 表现与输入的最后接线（玩家载体复位、相机跟随、准星、联机入口、光标锁定）。
            // 单独成方法：让 Initialize 只保留"装配顺序"这条主线，顺序依赖仍一眼可数。
            FinishPlayerWiring(spawnFacing);
        }

    }
}
