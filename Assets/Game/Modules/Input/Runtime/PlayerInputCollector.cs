using System;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.Input
{
    /// <summary>
    /// 把玩家输入采集为游戏命令的意图。
    /// </summary>
    /// <remarks>
    /// <para>本组件只负责"读取输入并翻译成意图"，不执行任何游戏逻辑。
    /// 它采集到的意图由调用方（场景启动流程）转成命令交给命令路由处理。
    /// 这条分工是刻意的：输入层若直接移动角色，联机阶段就必须重写，
    /// 而走命令路由后，单机与联机共用同一套逻辑。</para>
    ///
    /// <para>瞄准方向的处理方式：使用鼠标指针在世界平面上的投影点减去角色当前位置，
    /// 得到瞄准向量。这样朝向与移动方向完全解耦，玩家可以在侧向移动时保持朝前瞄准。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerInputCollector : MonoBehaviour
    {
        /// <summary>输入动作资产。对应工程内 Assets 下的 InputSystem_Actions。</summary>
        [SerializeField] private InputActionAsset m_Actions;

        /// <summary>Player 动作表名称。保持在序列化字段里，便于重命名而不改代码。</summary>
        [SerializeField] private string m_ActionMapName = "Player";

        /// <summary>移动动作名称。</summary>
        [SerializeField] private string m_MoveActionName = "Move";

        /// <summary>冲刺动作名称。</summary>
        [SerializeField] private string m_SprintActionName = "Sprint";

        /// <summary>
        /// 摄像机引用，用于把屏幕坐标转换为世界坐标。
        /// 未指定时自动使用 Camera.main，便于灰盒场景快速搭建。
        /// </summary>
        [SerializeField] private Camera m_AimCamera;

        /// <summary>本组件所属玩家的编号。单机固定为 0，联机时由服务端分配。</summary>
        [SerializeField] private int m_PlayerId;

        /// <summary>
        /// 瞄准死区半径（米）。
        /// </summary>
        /// <remarks>
        /// 当鼠标投影点与角色的距离小于该值时，方向向量会因过短而剧烈抖动，
        /// 表现为"鼠标移到角色身上时角色乱转"。此时保持上一次的朝向更符合直觉。
        /// 取 0.5 米是经验值：足够吸住抖动，又不至于让玩家感到瞄准迟钝。
        /// </remarks>
        [SerializeField] private float m_AimDeadZoneRadius = 0.5f;

        /// <summary>
        /// 最大瞄准距离（米）。小于等于 0 表示不限制。
        /// </summary>
        /// <remarks>
        /// 限制瞄准距离可避免准星被指到极远处，使角色朝向失去意义。
        /// 后续接入武器系统时，该值应由当前武器的射程驱动，而不是固定值。
        /// </remarks>
        [SerializeField] private float m_MaxAimDistance = 20f;

        /// <summary>是否在进入游戏时锁定并隐藏鼠标光标。</summary>
        [SerializeField] private bool m_LockCursorOnPlay = true;

        /// <summary>当前角色所在位置，用于计算瞄准方向。由场景启动流程每帧更新。</summary>
        private Vector2 m_OriginPosition;

        /// <summary>角色所处的地面高度。用于把鼠标投影到角色所在的水平面而非固定的 y 等于 0 平面。</summary>
        private float m_OriginHeight;

        /// <summary>
        /// 屏幕空间中的瞄准点位置（像素，原点在左下角）。
        /// </summary>
        /// <remarks>
        /// 锁定鼠标光标后无法依赖光标的绝对位置，因此自行维护一个瞄准点，
        /// 每帧由鼠标移动增量推进。这个点同时决定角色朝向与准星位置，
        /// 保证两者始终对齐——若各自独立计算，必然出现视觉偏差。
        /// </remarks>
        private Vector2 m_AimScreenPosition;

        private bool m_HasAimPosition;

        /// <summary>
        /// 世界空间中的瞄准点（水平面坐标）。
        /// </summary>
        /// <remarks>
        /// 这是准星应当指向的位置。它与角色朝向同源，因此两者天然对齐。
        /// </remarks>
        private Vector2F m_AimWorldPosition;

        private InputAction m_MoveAction;
        private InputAction m_SprintAction;
        private bool m_IsInitialized;

        /// <summary>
        /// 脚本化输入的移动方向。设置后优先于真实设备输入。
        /// </summary>
        /// <remarks>
        /// 这个通道有两个用途：自动化测试可以在不驱动真实设备的前提下验证整条输入链路；
        /// 开发期也可以用它复现特定输入组合（例如"一直按住前进并奔跑"）来观察行为。
        /// 未启用时（<see cref="UseScriptedInput"/> 为 false）完全不参与输入解析。
        /// </remarks>
        public Vector2 ScriptedMoveDirection { get; set; }

        /// <summary>脚本化输入是否请求奔跑。</summary>
        public bool ScriptedWantsToSprint { get; set; }

        /// <summary>是否启用脚本化输入。启用后真实设备输入被忽略。</summary>
        public bool UseScriptedInput { get; set; }

        /// <summary>脚本化输入的瞄准方向。设置后优先于鼠标瞄准。</summary>
        public Vector2F ScriptedLookDirection { get; set; }

        /// <summary>本帧是否请求奔跑。</summary>
        public bool WantsToSprint { get; private set; }

        /// <summary>本帧的期望瞄准方向（单位向量）。无法计算时返回零向量。</summary>
        public Vector2F LookDirection { get; private set; }

        /// <summary>所属玩家编号。</summary>
        public int PlayerId => m_PlayerId;

        /// <summary>
        /// 设置角色当前位置。场景启动流程在每帧更新位置后调用，
        /// 使瞄准方向基于最新位置计算，避免角色快速移动时瞄准点滞后。
        /// </summary>
        public void SetOriginPosition(Vector2 position)
        {
            m_OriginPosition = position;
        }

        /// <summary>
        /// 设置角色所处的地面高度。
        /// </summary>
        /// <remarks>
        /// 把鼠标投影到角色所在平面而不是固定的世界零平面，
        /// 是为了让瞄准方向在角色位于不同高度的地形上时依然准确。
        /// 灰盒阶段所有地面同高，但接口提前留好，后续加入高低差时无需改动。
        /// </remarks>
        public void SetOriginHeight(float height)
        {
            m_OriginHeight = height;
        }

        /// <summary>当前瞄准点在屏幕空间的位置（像素，原点在左下角）。</summary>
        public Vector2 AimScreenPosition => m_AimScreenPosition;

        /// <summary>世界空间中的瞄准点（水平面坐标）。准星应指向该位置。</summary>
        public Vector2F AimWorldPosition => m_AimWorldPosition;

        /// <summary>
        /// 启用或停用鼠标锁定。
        /// </summary>
        /// <remarks>
        /// 编辑器下由场景启动流程在获得与失去焦点时切换，
        /// 避免开发过程中光标一直被锁住而无法操作其他窗口。
        /// </remarks>
        public void SetCursorLock(bool locked)
        {
            if (!m_LockCursorOnPlay)
            {
                return;
            }

            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;

            if (!locked)
            {
                // 解除锁定时丢弃瞄准点跟踪状态，使下次重新锁定时从屏幕中心开始，
                // 避免沿用上一次的旧位置导致准星突然跳变。
                m_HasAimPosition = false;
            }
        }

        /// <summary>释放鼠标锁定。用于暂停、打开界面等需要操作光标的场合。</summary>
        public void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            m_HasAimPosition = false;
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
            m_MoveAction?.Enable();
            m_SprintAction?.Enable();
        }

        private void OnDisable()
        {
            m_MoveAction?.Disable();
            m_SprintAction?.Disable();
        }

        /// <summary>
        /// 读取本帧输入。由场景启动流程在每帧调用一次。
        /// </summary>
        /// <returns>本帧的移动意图。无法读取输入时返回零向量。</returns>
        public Vector2F ReadMoveIntent(out bool wantsToSprint)
        {
            Initialize();

            wantsToSprint = false;
            LookDirection = Vector2F.Zero;

            if (UseScriptedInput)
            {
                wantsToSprint = ScriptedWantsToSprint;
                WantsToSprint = wantsToSprint;
                LookDirection = ScriptedLookDirection;
                return new Vector2F(ScriptedMoveDirection.x, ScriptedMoveDirection.y);
            }

            if (!m_IsInitialized)
            {
                return Vector2F.Zero;
            }

            var move = m_MoveAction.ReadValue<Vector2>();
            wantsToSprint = m_SprintAction.IsPressed();
            WantsToSprint = wantsToSprint;

            LookDirection = ResolveLookDirection();

            return new Vector2F(move.x, move.y);
        }

        /// <summary>
        /// 推进瞄准点并计算角色朝向。
        /// </summary>
        /// <remarks>
        /// 流程是：鼠标移动增量推进屏幕瞄准点，限制在屏幕内，
        /// 再由射线与地面求交得到世界瞄准点，最后限制在最大射程内并求朝向。
        /// 世界瞄准点会被保存下来，供准星绘制复用，
        /// 从而保证准星与角色朝向指向同一个位置。
        /// </remarks>
        private Vector2F ResolveLookDirection()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return Vector2F.Zero;
            }

            var cam = m_AimCamera != null ? m_AimCamera : Camera.main;
            if (cam == null)
            {
                return Vector2F.Zero;
            }

            UpdateAimScreenPosition(mouse);

            var world = ScreenToWorldOnPlane(cam, m_AimScreenPosition, m_OriginHeight);
            var worldAim = AimResolver.ClampToMaxRange(
                new Vector2F(world.x, world.y),
                new Vector2F(m_OriginPosition.x, m_OriginPosition.y),
                m_MaxAimDistance);

            m_AimWorldPosition = worldAim;
            m_HasAimPosition = true;

            // 死区判定与方向归一化由共享层的纯函数完成，便于单元测试覆盖边界情形。
            // 返回零向量表示"保持上一次朝向不变"。
            return AimResolver.Resolve(
                worldAim,
                new Vector2F(m_OriginPosition.x, m_OriginPosition.y),
                m_AimDeadZoneRadius);
        }

        /// <summary>
        /// 用鼠标移动增量推进屏幕瞄准点。
        /// </summary>
        /// <remarks>
        /// 鼠标被锁定时，系统光标固定在窗口内不再移动，但仍然会上报移动增量。
        /// 因此这里不使用光标的绝对位置，而是自行累加增量维护瞄准点，
        /// 这样才能在锁定状态下正常瞄准。
        /// 未锁定时直接采用光标位置，保持编辑器下的常规操作习惯。
        /// </remarks>
        private void UpdateAimScreenPosition(Mouse mouse)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                if (!m_HasAimPosition)
                {
                    // 首次进入锁定时以屏幕中心作为初始瞄准点，避免从角落开始。
                    m_AimScreenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    m_HasAimPosition = true;
                }

                m_AimScreenPosition += mouse.delta.ReadValue();

                var clamp = AimResolver.ClampToScreen(
                    new Vector2F(m_AimScreenPosition.x, m_AimScreenPosition.y),
                    Screen.width,
                    Screen.height);
                m_AimScreenPosition = new Vector2(clamp.X, clamp.Y);
                return;
            }

            // 未锁定状态（例如编辑器失去焦点，或玩家打开了界面菜单）：
            // 此时鼠标位置由系统光标决定，但系统光标可能位于游戏窗口之外，
            // 其坐标对游戏没有意义。因此这里重置跟踪状态，
            // 等下次重新锁定时再从屏幕中心开始。
            if (!m_HasAimPosition)
            {
                m_AimScreenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
        }

        /// <summary>
        /// 把屏幕坐标转换为角色所在水平面上的世界坐标。
        /// </summary>
        /// <remarks>
        /// 不直接使用 ScreenToWorldPoint，是因为它需要知道目标点的深度；
        /// 这里改用一条从摄像机出发的射线与水平面求交，结果与角色所在高度无关。
        /// </remarks>
        private static Vector2 ScreenToWorldOnPlane(Camera cam, Vector2 screenPoint, float planeHeight)
        {
            var ray = cam.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));

            if (plane.Raycast(ray, out var distance))
            {
                var hit = ray.GetPoint(distance);
                return new Vector2(hit.x, hit.z);
            }

            return Vector2.zero;
        }

        /// <summary>
        /// 解析输入动作。允许重复调用，未配置资产时不会抛异常，
        /// 便于在尚未接好引用的灰盒场景中安全运行。
        /// </summary>
        private void Initialize()
        {
            if (m_IsInitialized || m_Actions == null)
            {
                return;
            }

            var map = m_Actions.FindActionMap(m_ActionMapName, false);
            if (map == null)
            {
                Debug.LogWarning($"[RaidDemo] 输入动作表 {m_ActionMapName} 不存在，玩家将无法操作。", this);
                return;
            }

            m_MoveAction = map.FindAction(m_MoveActionName, false);
            m_SprintAction = map.FindAction(m_SprintActionName, false);

            if (m_MoveAction == null)
            {
                Debug.LogWarning($"[RaidDemo] 输入动作 {m_MoveActionName} 不存在，玩家将无法移动。", this);
                return;
            }

            m_IsInitialized = true;
        }
    }
}
