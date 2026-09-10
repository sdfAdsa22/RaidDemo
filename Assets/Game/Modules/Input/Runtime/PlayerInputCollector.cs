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

        /// <summary>当前角色所在位置，用于计算瞄准方向。由场景启动流程每帧更新。</summary>
        private Vector2 m_OriginPosition;

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
        /// 计算瞄准方向：把鼠标位置投影到世界平面，再减去角色位置。
        /// </summary>
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

            var screenPoint = mouse.position.ReadValue();
            var world = ScreenToWorldOnPlane(cam, screenPoint);
            var delta = world - m_OriginPosition;

            // 距离过近时方向不稳定（鼠标与角色几乎重合），此时保持原朝向更自然。
            return delta.sqrMagnitude < 0.0001f
                ? Vector2F.Zero
                : new Vector2F(delta.x, delta.y).Normalized;
        }

        /// <summary>
        /// 把屏幕坐标转换为角色所在水平面上的世界坐标。
        /// </summary>
        /// <remarks>
        /// 不直接使用 ScreenToWorldPoint，是因为它需要知道目标点的深度；
        /// 这里改用一条从摄像机出发的射线与水平面求交，结果与角色所在高度无关。
        /// </remarks>
        private static Vector2 ScreenToWorldOnPlane(Camera cam, Vector2 screenPoint)
        {
            var ray = cam.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, Vector3.zero);

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
