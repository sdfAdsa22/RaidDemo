using RaidDemo.Input;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.Simulation;
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
    public sealed class SceneBootstrap : MonoBehaviour
    {
        /// <summary>玩家初始位置。</summary>
        [SerializeField] private Vector2 m_PlayerSpawnPosition = Vector2.zero;

        /// <summary>玩家初始朝向（度）。0 度指向世界 X 轴正方向。</summary>
        [SerializeField] private float m_PlayerSpawnFacingDegrees;

        /// <summary>玩家表现层组件。</summary>
        [SerializeField] private PlayerMotor m_PlayerMotor;

        /// <summary>输入采集组件。</summary>
        [SerializeField] private PlayerInputCollector m_InputCollector;

        /// <summary>跟随相机。</summary>
        [SerializeField] private TopDownCameraController m_CameraController;

        /// <summary>瞄准准星。未指定时会在运行时自动创建。</summary>
        [SerializeField] private AimCrosshair m_Crosshair;

        private EventBus m_EventBus;
        private ServiceLocator m_Services;
        private CommandRouter m_CommandRouter;
        private PlayerMovementProfile m_MovementProfile;
        private PlayerMoveCommandHandler m_MoveHandler;

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

        /// <summary>
        /// 初始化顺序设为很早，保证其他组件的 OnEnable 能查询到已注册的服务。
        /// </summary>
        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            ServiceLocatorHolder.Clear();
            m_Services?.Clear();
        }

        private void Update()
        {
            if (m_MoveHandler == null)
            {
                return;
            }

            // 编辑器在失去焦点时会自动解除光标锁定；玩家点回游戏窗口后需要重新锁上。
            // 这里只在应用有焦点时维持锁定，避免与操作系统的焦点切换互相抢控制权。
            if (m_InputCollector != null && Application.isFocused && Cursor.lockState != CursorLockMode.Locked)
            {
                m_InputCollector.SetCursorLock(true);
            }

            CollectInput();
            DispatchMoveCommand();

            // 命令只更新意图，模拟推进由 Tick 完成：
            // 这样即使某帧没有输入，角色也会按上一次意图继续移动。
            m_MoveHandler.Tick(Time.deltaTime);

            // 把最新位置回写给输入层，使下一帧的瞄准方向基于最新位置计算。
            if (m_InputCollector != null && m_PlayerMotor != null)
            {
                m_InputCollector.SetOriginPosition(m_PlayerMotor.SimulatedPosition);
                m_InputCollector.SetOriginHeight(m_PlayerMotor.transform.position.y);
            }

            UpdateCrosshair();
        }

        /// <summary>
        /// 建立全部服务与对象，并完成命令处理器注册。
        /// </summary>
        /// <remarks>
        /// 各步骤的顺序有依赖：事件总线必须先注册，表现层组件才能在 OnEnable 中订阅。
        /// </remarks>
        public void Initialize()
        {
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
            var simulator = new PlayerMovementSimulator(
                m_MovementProfile,
                new Vector2F(m_PlayerSpawnPosition.x, m_PlayerSpawnPosition.y),
                spawnFacing);

            m_MoveHandler = new PlayerMoveCommandHandler(simulator, m_EventBus);
            m_CommandRouter.Register(m_MoveHandler);

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

        /// <summary>
        /// 确保准星组件存在。
        /// </summary>
        /// <remarks>
        /// 准星在运行时创建而非烘焙进场景，原因是它属于纯表现层元素，
        /// 没有需要美术调整的序列化状态，放在运行时创建可以让场景文件保持干净。
        /// </remarks>
        private void EnsureCrosshair()
        {
            if (m_Crosshair != null)
            {
                return;
            }

            var host = new GameObject("AimCrosshair");
            host.transform.SetParent(transform, worldPositionStays: false);
            m_Crosshair = host.AddComponent<AimCrosshair>();
        }

        /// <summary>
        /// 更新准星位置。
        /// </summary>
        /// <remarks>
        /// 准星位置由世界瞄准点反投影回屏幕得到，与角色朝向同源，
        /// 因此不会出现"准星在一个地方、角色朝另一个地方"的偏差。
        /// </remarks>
        private void UpdateCrosshair()
        {
            if (m_Crosshair == null || m_InputCollector == null || m_CameraController == null)
            {
                return;
            }

            var worldAim = m_InputCollector.AimWorldPosition;
            if (worldAim.IsNearlyZero)
            {
                m_Crosshair.Hide();
                return;
            }

            var cam = m_CameraController.GetComponent<Camera>();
            if (cam == null)
            {
                m_Crosshair.Hide();
                return;
            }

            // 瞄准点位于角色所在高度，反投影时使用相同高度，避免透视造成的偏移。
            var world = new Vector3(worldAim.X, m_PlayerMotor.transform.position.y, worldAim.Y);
            var screen = cam.WorldToScreenPoint(world);

            if (screen.z < 0f)
            {
                // 点在相机背后，此时不应绘制准星。
                m_Crosshair.Hide();
                return;
            }

            m_Crosshair.SetScreenPosition(new Vector2(screen.x, screen.y));
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 编辑器下失去焦点时释放光标，避免开发过程中无法操作其他窗口。
            // 发行构建中窗口失去焦点并不是常见场景，保持锁定更符合预期。
#if UNITY_EDITOR
            if (m_InputCollector != null)
            {
                m_InputCollector.SetCursorLock(hasFocus);
            }
#endif
        }

        /// <summary>读取本帧输入。</summary>
        private void CollectInput()
        {
            if (m_InputCollector == null)
            {
                m_PendingMoveIntent = Vector2F.Zero;
                m_PendingLookDirection = Vector2F.Zero;
                m_PendingWantsToSprint = false;
                return;
            }

            m_PendingMoveIntent = m_InputCollector.ReadMoveIntent(out var sprint);
            m_PendingLookDirection = m_InputCollector.LookDirection;
            m_PendingWantsToSprint = sprint;
        }

        /// <summary>
        /// 把输入意图封装成命令并交给命令路由。
        /// </summary>
        /// <remarks>
        /// 这是整个项目最关键的架构约束的落点：输入不直接驱动移动，
        /// 而是先变成命令经统一入口执行。联机时只需把这里的
        /// 本地执行替换为网络发送，模拟逻辑无需改动。
        /// </remarks>
        private void DispatchMoveCommand()
        {
            var intent = new PlayerMoveIntent(
                m_InputCollector != null ? m_InputCollector.PlayerId : 0,
                m_PendingMoveIntent,
                m_PendingLookDirection,
                m_PendingWantsToSprint,
                ++m_CommandSequence);

            var result = m_CommandRouter.Dispatch(intent);
            if (!result.Success)
            {
                Debug.LogWarning($"[RaidDemo] 移动命令被拒绝：{result.Code} - {result.Message}", this);
            }
        }
    }
}
