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
    public sealed partial class PlayerInputCollector : MonoBehaviour
    {
        /// <summary>输入动作资产。对应工程内 Assets 下的 InputSystem_Actions。</summary>
        [SerializeField] private InputActionAsset m_Actions;

        /// <summary>Player 动作表名称。保持在序列化字段里，便于重命名而不改代码。</summary>
        [SerializeField] private string m_ActionMapName = "Player";

        /// <summary>移动动作名称。</summary>
        [SerializeField] private string m_MoveActionName = "Move";

        /// <summary>冲刺动作名称。</summary>
        [SerializeField] private string m_SprintActionName = "Sprint";

        /// <summary>射击动作名称。</summary>
        [SerializeField] private string m_AttackActionName = "Attack";

        /// <summary>换弹动作名称。</summary>
        [SerializeField] private string m_ReloadActionName = "Reload";

        /// <summary>切换到下一把武器的动作名称。</summary>
        [SerializeField] private string m_NextWeaponActionName = "Next";

        /// <summary>切换到上一把武器的动作名称。</summary>
        [SerializeField] private string m_PreviousWeaponActionName = "Previous";

        /// <summary>
        /// 交互动作名称（搜刮战利品、以后还会用于开门与拾取）。
        /// </summary>
        /// <remarks>
        /// 交互与射击的性质不同：射击是持续状态，交互是**一次性动作**——
        /// 按下的那一帧提交一次意图，之后由逻辑层决定要读条多久。
        /// 读条时长属于游戏规则，不能交给输入系统里的 Hold 交互去决定，
        /// 否则「搜刮要两秒」这条规则就被埋进了输入资产里，改数值要动资产而不是代码。
        /// </remarks>
        [SerializeField] private string m_InteractActionName = "Interact";

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

        /// <summary>射击动作。按住期间每帧都视为"想开火"。</summary>
        private InputAction m_AttackAction;

        /// <summary>换弹动作。只在按下的那一帧生效。</summary>
        private InputAction m_ReloadAction;

        /// <summary>切换到下一把武器。绑定在鼠标滚轮上滚。</summary>
        private InputAction m_NextWeaponAction;

        /// <summary>切换到上一把武器。绑定在鼠标滚轮下滚。</summary>
        private InputAction m_PreviousWeaponAction;

        /// <summary>交互动作。绑定在键盘 E 上。</summary>
        private InputAction m_InteractAction;

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

        /// <summary>脚本化输入是否请求射击。</summary>
        public bool ScriptedWantsToFire { get; set; }

        /// <summary>脚本化输入是否请求换弹。</summary>
        public bool ScriptedWantsToReload { get; set; }

        /// <summary>脚本化切换武器输入：+1 / -1 / 0。</summary>
        public int ScriptedWeaponSwitch { get; set; }

        /// <summary>脚本化交互输入。</summary>
        public bool ScriptedWantsToInteract { get; set; }

        /// <summary>脚本化「使用医疗品」输入。</summary>
        public bool ScriptedWantsToUseMedical { get; set; }

        /// <summary>是否启用脚本化输入。启用后真实设备输入被忽略。</summary>
        public bool UseScriptedInput { get; set; }

        /// <summary>
        /// 设置瞄准点的最大距离（米）。
        /// </summary>
        /// <param name="meters">最大距离。非正值表示不限制。</param>
        /// <remarks>
        /// 由战斗系统在换枪时写入，取当前武器的射程。
        /// 这样准星能标出的范围与子弹真正能打到的范围始终一致——
        /// 玩家不会朝着一个超出射程的目标瞄准，然后疑惑为什么打不中。
        /// </remarks>
        public void SetMaxAimDistance(float meters)
        {
            m_MaxAimDistance = meters;
        }

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
            m_AttackAction?.Enable();
            m_ReloadAction?.Enable();
            m_NextWeaponAction?.Enable();
            m_PreviousWeaponAction?.Enable();
            m_InteractAction?.Enable();
        }

        private void OnDisable()
        {
            m_MoveAction?.Disable();
            m_SprintAction?.Disable();
            m_AttackAction?.Disable();
            m_ReloadAction?.Disable();
            m_NextWeaponAction?.Disable();
            m_PreviousWeaponAction?.Disable();
            m_InteractAction?.Disable();
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
            m_AttackAction = map.FindAction(m_AttackActionName, false);
            m_ReloadAction = map.FindAction(m_ReloadActionName, false);
            m_NextWeaponAction = map.FindAction(m_NextWeaponActionName, false);
            m_PreviousWeaponAction = map.FindAction(m_PreviousWeaponActionName, false);
            m_InteractAction = map.FindAction(m_InteractActionName, false);

            if (m_MoveAction == null)
            {
                Debug.LogWarning($"[RaidDemo] 输入动作 {m_MoveActionName} 不存在，玩家将无法移动。", this);
                return;
            }

            m_IsInitialized = true;
        }
    }
}
