using RaidDemo.Data;
using RaidDemo.Input;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.Simulation;
using RaidDemo.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的装配入口：局外空间的组合根。
    /// </summary>
    /// <remarks>
    /// <para>它比战局的 <c>SceneBootstrap</c> 轻得多——没有 AI、没有战局会话、没有计时。
    /// 但**背包与装备那套东西是同一套**：仓库与随身携带物都来自跨场景存活的
    /// <see cref="MetaProgress"/>，界面、拖拽、命令路由全部复用。</para>
    ///
    /// <para>它负责四件事：让玩家能走动、能在设施前按 E、能从出口出击、以及把仓库界面打开。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class SafeHouseBootstrap : MonoBehaviour
    {
        /// <summary>玩家出生点。</summary>
        [SerializeField] private Vector2 m_PlayerSpawnPosition;
        [SerializeField] private PlayerMotor m_PlayerMotor;
        [SerializeField] private PlayerInputCollector m_InputCollector;
        [SerializeField] private TopDownCameraController m_CameraController;
        [SerializeField] private ItemCatalog m_ItemCatalog;
        /// <summary>会话作用域：会话级服务的创建与释放由它统一管理（M9 · P0）。</summary>
        private SessionScope m_Session;
        private EventBus m_EventBus;
        private CommandRouter m_CommandRouter;
        private PlayerMovementProfile m_MovementProfile;
        private PlayerMoveCommandHandler m_MoveHandler;
        private ContainerRegistry m_Registry;
        private PlayerLoadout m_Loadout;
        private InventoryScreenController m_InventoryScreen;
        private int m_BackpackContainerId;
        private int m_AmmoPouchContainerId;
        private int m_StashContainerId;
        private uint m_CommandSequence;
        private Vector2F m_PendingMove;
        private Vector2F m_PendingLook;
        private bool m_PendingSprint;
        private SafeHouseInteractable m_Nearby;
        private SafeHouseUI m_Ui;
        private RaidFlowController m_Flow;

        /// <summary>初始化顺序与战局一致：服务 → 命令 → 界面 → 表现。</summary>
        private void Awake()
        {
            // 服务器进程不装配客户端世界：安全屋是纯客户端场景（相机、界面、设施交互）。
            if (ServerMode.IsActive)
            {
                // 服务器需要物品目录：安全屋里的仓库界面要用它，而服务端的进度存档（P5）
                // 也要靠它把存档里的物品 ID 还原成定义。战局场景加载时会再交接一次
                // （同一份目录资产），因此后到的交接不会造成分叉。
                ServerMode.AcceptSceneContent(m_ItemCatalog, m_PresentationCatalog);

                // 但服务器要**托管**这间屋子（P4.5-b）：把场景里的出生点交给它，
                // 服务器据此决定"玩家回到安全屋时站在哪"——与客户端进场景时站的位置完全一致。
                ServerMode.AcceptSafeHouseSpawn(m_PlayerSpawnPosition);
                Destroy(gameObject);
                return;
            }

            Application.runInBackground = true;

            // 热更：先用本地已下载的内容解析目录（毫秒级，不阻塞启动），
            // 随后在后台检查更新；若内容有变化且玩家还在主菜单，会重载场景让它立即生效。
            ApplyHotUpdateContent();
            StartCoroutine(RunHotUpdate());

            // 与战局一致：会话作用域负责服务，场景只负责装配内容。
            // 联机时把创建者标签与日志等级对齐到客户端参数——排障时"这条日志来自哪个场景、
            // 哪个角色"要能一眼看出来（安全屋与战局是两个各自装配的客户端场景）。
            m_Session = IsMultiplayerProcess
                ? new SessionScope(
                    "联机客户端·安全屋",
                    ClientMode.Options != null ? ClientMode.Options.MinimumLogLevel : LogLevel.Info)
                : SessionScope.CreateLocal();
            m_EventBus = m_Session.Events;
            m_CommandRouter = m_Session.Commands;

            m_MovementProfile = new PlayerMovementProfile();
            var facing = Vector2F.FromDegrees(0f);
            var simulator = new PlayerMovementSimulator(
                m_MovementProfile,
                new Vector2F(m_PlayerSpawnPosition.x, m_PlayerSpawnPosition.y),
                facing,
                m_PlayerMotor != null
                    ? new PhysicsMovementCollisionService(m_PlayerMotor.transform, 1.8f, 0.02f)
                    : null,
                0.4f);

            m_MoveHandler = new PlayerMoveCommandHandler(simulator, m_EventBus);
            m_CommandRouter.Register(m_MoveHandler);

            InitializeInventory();
            InitializeCombat();
            InitializeCodex();

            if (m_PlayerMotor != null)
            {
                m_PlayerMotor.Rebind(m_EventBus);
                m_PlayerMotor.SnapTo(m_PlayerSpawnPosition, new Vector2(facing.X, facing.Y));
            }

            if (m_CameraController != null && m_PlayerMotor != null)
            {
                m_CameraController.SetTarget(m_PlayerMotor.transform, snap: true);
                BindOcclusionPeephole();
            }

            var uiHost = new GameObject("SafeHouseUI");
            uiHost.transform.SetParent(transform, worldPositionStays: false);
            m_Ui = uiHost.AddComponent<SafeHouseUI>();
            m_Ui.Initialize(StartRaid);
            if (m_Progress != null)
            {
                m_Progress.Changed += RefreshWallet;
                RefreshWallet();
            }

            // 启动时先显示极简主菜单（开始 / 退出）；从战局回来时直接进安全屋。
            var flow = RaidFlowController.Ensure();
            m_Flow = flow;

            // 联机进程（-connect 或大厅会话）：安全屋就是"进房之后的第一站"，
            // 主菜单那一层绝不能盖上来——它会把 timeScale 压成 0，而连接握手、
            // 自动进房、移动链路全都依赖时间推进（第一次联调就卡在这里）。
            if (IsMultiplayerProcess)
            {
                EnterSafeHouseAsMultiplayer(flow);
            }
            else if (flow.State == RaidFlowController.FlowState.MainMenu)
            {
                flow.ShowMainMenu();
            }
            else
            {
                flow.HideScreens();
                m_InputCollector?.SetCursorLock(true);
            }
        }

        /// <summary>
        /// 绑定遮挡透视孔：安全屋与战局共用同一套相机表现，因此这里也要挂一份，
        /// 否则进出场景时会出现「战局能透视、安全屋不能」的不一致。
        /// </summary>
        private void BindOcclusionPeephole()
        {
            var camera = m_CameraController != null ? m_CameraController.GetComponent<Camera>() : null;
            if (camera == null || m_PlayerMotor == null)
            {
                return;
            }

            var peephole = camera.GetComponent<OcclusionPeepholeController>();
            if (peephole == null)
            {
                peephole = camera.gameObject.AddComponent<OcclusionPeepholeController>();
            }

            peephole.Bind(m_PlayerMotor.transform, camera);
        }

        private void OnDestroy()
        {
            if (m_Progress != null)
            {
                m_Progress.Changed -= RefreshWallet;
                m_Progress.Changed -= RefreshCodexBoardText;
            }

            // 联机：退订连接回调并释放移动链路（不清干净会在下一场景留下悬挂引用）。
            DetachMultiplayer();

            // 释放订阅：场景重载后旧总线会被丢弃，但不释放会在退出播放模式时留下悬挂引用。
            m_ChangedSubscription?.Dispose();
            m_ChangedSubscription = null;
            m_CodexMarker?.Dispose();
            m_CodexMarker = null;
            m_Session?.Dispose();
            m_Session = null;
        }

        /// <summary>把最新余额写进安全屋右上角。</summary>
        private void RefreshWallet()
        {
            m_Ui?.SetMoney(m_Progress != null ? m_Progress.Money : 0);
        }

        /// <summary>出战：单机直接切战局场景；联机走服务器的门禁与开局。</summary>
        private void StartRaid()
        {
            TryDeployFromSafeHouse();
        }

        private void Update()
        {
            if (m_MoveHandler == null)
            {
                return;
            }

            // 联机会话可能在本场景 Awake 之后才建立（命令行 -connect 路径），
            // 因此每帧检查一次"该不该接上共享世界"。
            EnsureMultiplayerAttached();

            var flow = m_Flow != null ? m_Flow : RaidFlowController.Ensure();
            var escapePressed = m_InputCollector != null && m_InputCollector.ReadPauseIntent();

            if (flow.IsPaused)
            {
                if (escapePressed)
                {
                    flow.ResumeFromPause();
                }

                return;
            }

            var uiOpen = (m_InventoryScreen != null && m_InventoryScreen.IsOpen)
                || (m_Ui != null && m_Ui.IsOpen)
                || (m_MerchantScreen != null && m_MerchantScreen.IsOpen)
                // 联机界面与房间界面同样会挡住安全屋的操作：它们需要鼠标，
                // 而且 Esc 要归它们（退出输入框 / 返回上一界面）。
                || flow.IsBlockingScreenVisible;

            // 安全屋里按 Esc 打开暂停菜单；界面打开时 Esc 先交给界面自己处理。
            if (!uiOpen
                && flow.State == RaidFlowController.FlowState.SafeHouse
                && escapePressed
                // 界面可能在同一帧里刚用 Esc 关掉自己（并标记为已消费）：
                // 此时 uiOpen 已经是 false，若不看这个标记就会"关界面的同时弹出暂停菜单"。
                && !UiEscapeGuard.WasConsumedThisFrame)
            {
                flow.ShowPauseMenu(warnAbandon: false);
                return;
            }

            // 主菜单与结算界面属于"需要鼠标"的流程状态：
            // 它们不是战局/安全屋里的操作面板，不能因为背包没开就锁光标。
            var needsMouse = flow.State == RaidFlowController.FlowState.MainMenu
                || flow.State == RaidFlowController.FlowState.Result
                || flow.IsBlockingScreenVisible;

            if (needsMouse)
            {
                // 需要鼠标的界面：释放光标（ReleaseCursor 不受"是否启用光标锁定"开关影响）。
                m_InputCollector?.ReleaseCursor();
            }
            var uiBlocking = uiOpen || needsMouse;

            // 商人界面关闭后恢复背包的 Tab 输入；打开商人时会临时关掉它，
            // 避免两个全屏界面叠在一起。
            if (m_MerchantScreen != null
                && !m_MerchantScreen.IsOpen
                && (m_CodexScreen == null || !m_CodexScreen.IsOpen)
                && m_InventoryScreen != null
                && !m_InventoryScreen.InputEnabled)
            {
                m_InventoryScreen.InputEnabled = true;
            }

            var shouldLockCursor = CursorLockPolicy.ShouldLockCursor(uiOpen, needsMouse);
            if (needsMouse)
            {
                // ReleaseCursor 不受"是否启用光标锁定"开关影响，能保证菜单一定可见。
                m_InputCollector?.ReleaseCursor();
            }
            else if (shouldLockCursor
                     && m_InputCollector != null
                     && Application.isFocused
                     && Cursor.lockState != CursorLockMode.Locked)
            {
                m_InputCollector.SetCursorLock(true);
            }

            // 与战局同一条规则：先把上一帧推进后的最新位置回写给输入层，再做本帧输入采集。
            // 顺序反了的话瞄准点基于旧位置计算，开局第一帧甚至会用到默认的 (0,0)。
            if (m_InputCollector != null && m_PlayerMotor != null)
            {
                m_InputCollector.SetOriginPosition(m_PlayerMotor.SimulatedPosition);
                m_InputCollector.SetOriginHeight(m_PlayerMotor.transform.position.y + AimPlaneHeight);   // 瞄准平面 = 枪口高度（U-95 方案 A）
            }

            if (uiBlocking)
            {
                // 界面挡住操作时必须连"待上行意图"一起清掉：
                // 只清本地意图的话，客户端仍会把上一帧的移动方向发给服务器——
                // 表现是"翻着背包人还在往前走"（战局侧同一处也已按这条规则处理）。
                ClearMovementIntent();
                m_WeaponController?.SetTriggerHeld(false);
            }
            else
            {
                CollectInput();
                UpdateWeapon();
            }

            // 联机：固定步预测 + 上行 + 快照对账（与战局同一条链路）。
            // 单机：按帧推进本地模拟。
            if (IsMultiplayerSafeHouse)
            {
                TickMultiplayerSafeHouse(Time.deltaTime, Time.unscaledDeltaTime);
            }
            else
            {
                m_MoveHandler.Tick(Time.deltaTime);
            }

            UpdateInteraction(uiBlocking);
        }

        /// <summary>
        /// 读取战斗输入并派发命令。与战局用同一条链路，因此手感一致。
        /// </summary>
        private void UpdateWeapon()
        {
            if (m_WeaponController == null || m_InputCollector == null)
            {
                return;
            }

            // 瞄准方向来自鼠标在角色平面上的投影点，与战局完全相同。
            var aim = m_InputCollector.LookDirection;
            m_WeaponController.SetAimDirection(aim);
            m_WeaponController.SetAimWorldPoint(m_InputCollector.AimWorldPosition);

            m_InputCollector.ReadCombatIntent(out var wantsToFire, out var wantsToReload);

            if (wantsToReload)
            {
                var reloadResult = m_CommandRouter.Dispatch(new PlayerReloadIntent(
                    m_InputCollector.PlayerId, ++m_CommandSequence));
                // 与战局同一条提示链路：安全屋试枪时也能看到"没有匹配弹药"的原因。
                var hint = ReloadFeedback.BuildMessage(
                    reloadResult.Code,
                    m_WeaponController?.Runtime?.Weapon?.CaliberId,
                    m_Loadout);
                if (hint != null)
                {
                    m_CombatHud?.ShowHint(hint);
                }
            }

            m_WeaponController.SetTriggerHeld(wantsToFire);
            UpdateWeaponPresentation();

            // 控制器的时间推进必须每帧调用：射速间隔与换弹进度都在它内部累计，
            // 不推进的话「按 R 没反应、开枪也不响」——而界面上看不出任何异常。
            m_WeaponController.Tick(Time.deltaTime);

            if (!wantsToFire)
            {
                return;
            }

            m_CommandRouter.Dispatch(new PlayerFireIntent(
                m_InputCollector.PlayerId,
                aim,
                ++m_CommandSequence));
        }

        /// <summary>读取输入并派发移动命令。与战局用同一条链路，手感因此完全一致。</summary>
        private void CollectInput()
        {
            if (m_InputCollector == null)
            {
                m_PendingMove = Vector2F.Zero;
                m_PendingLook = Vector2F.Zero;
                m_PendingSprint = false;
                return;
            }

            m_PendingMove = m_InputCollector.ReadMoveIntent(out var sprint);
            m_PendingLook = m_InputCollector.LookDirection;
            m_PendingSprint = sprint;

            m_CommandRouter.Dispatch(new PlayerMoveIntent(
                m_InputCollector.PlayerId,
                m_PendingMove,
                m_PendingLook,
                m_PendingSprint,
                ++m_CommandSequence));
        }

    }
}
