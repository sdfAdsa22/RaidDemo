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
        private EventBus m_EventBus;
        private ServiceLocator m_Services;
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
            Application.runInBackground = true;

            m_Services = new ServiceLocator();
            m_EventBus = new EventBus();
            m_CommandRouter = new CommandRouter();
            m_Services.Register(m_EventBus);
            m_Services.Register(m_CommandRouter);
            m_Services.Register(new LogService(LogLevel.Info));
            ServiceLocatorHolder.Set(m_Services);

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
            if (flow.State == RaidFlowController.FlowState.MainMenu)
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

            // 释放订阅：场景重载后旧总线会被丢弃，但不释放会在退出播放模式时留下悬挂引用。
            m_ChangedSubscription?.Dispose();
            m_ChangedSubscription = null;
            m_CodexMarker?.Dispose();
            m_CodexMarker = null;
            ServiceLocatorHolder.Clear();
            m_Services?.Clear();
        }

        /// <summary>把最新余额写进安全屋右上角。</summary>
        private void RefreshWallet()
        {
            m_Ui?.SetMoney(m_Progress != null ? m_Progress.Money : 0);
        }

        /// <summary>出战：切到战局场景。</summary>
        private void StartRaid()
        {
            RaidFlowController.Ensure().StartRaid();
        }

        private void Update()
        {
            if (m_MoveHandler == null)
            {
                return;
            }

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
                || (m_MerchantScreen != null && m_MerchantScreen.IsOpen);

            // 安全屋里按 Esc 打开暂停菜单；界面打开时 Esc 先交给界面自己处理。
            if (!uiOpen
                && flow.State == RaidFlowController.FlowState.SafeHouse
                && escapePressed)
            {
                flow.ShowPauseMenu(warnAbandon: false);
                return;
            }

            // 主菜单与结算界面属于"需要鼠标"的流程状态：
            // 它们不是战局/安全屋里的操作面板，不能因为背包没开就锁光标。
            var needsMouse = flow.State == RaidFlowController.FlowState.MainMenu
                || flow.State == RaidFlowController.FlowState.Result;
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

            if (uiBlocking)
            {
                m_MoveHandler.ClearIntent();
                m_WeaponController?.SetTriggerHeld(false);
            }
            else
            {
                CollectInput();
                UpdateWeapon();
            }

            m_MoveHandler.Tick(Time.deltaTime);

            if (m_InputCollector != null && m_PlayerMotor != null)
            {
                m_InputCollector.SetOriginPosition(m_PlayerMotor.SimulatedPosition);
                m_InputCollector.SetOriginHeight(m_PlayerMotor.transform.position.y);
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
                m_CommandRouter.Dispatch(new PlayerReloadIntent(
                    m_InputCollector.PlayerId, ++m_CommandSequence));
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
