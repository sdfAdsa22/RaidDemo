using System;
using RaidDemo.Kernel;
using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 把移动模拟的结果应用到角色的 Transform 上。
    /// </summary>
    /// <remarks>
    /// 本组件是模拟层到表现层的适配器，属于整个项目中最薄的一层。
    /// 它不含任何移动规则：速度、体力、朝向全部由 PlayerMovementSimulator 决定，
    /// 这里只负责把结果搬运到 Unity 的 Transform 上。
    ///
    /// 之所以这样切分，是因为移动规则需要能在无头服务端运行，
    /// 而 Transform 只在客户端存在。把两者混在一起会让服务端无法复用移动逻辑。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerMotor : MonoBehaviour
    {
        /// <summary>朝向旋转的速度（度/秒）。值越大转向越干脆。</summary>
        [SerializeField] private float m_FacingDegreesPerSecond = 900f;

        /// <summary>高度偏移，使角色模型底部贴合地面。</summary>
        [SerializeField] private float m_GroundOffset;

        /// <summary>所属玩家编号，用于过滤事件。</summary>
        [SerializeField] private int m_PlayerId;

        private EventBus m_EventBus;
        private IDisposable m_Subscription;

        /// <summary>当前模拟位置（水平面）。</summary>
        public Vector2 SimulatedPosition { get; private set; }

        /// <summary>当前朝向角度（度）。</summary>
        public float FacingDegrees { get; private set; }

        private void OnEnable()
        {
            // 事件总线由场景启动流程注册。若此时尚未就绪则跳过，
            // SceneBootstrap 会在初始化完成后调用 Rebind 补上订阅。
            if (!ServiceLocatorHolder.TryGet(out m_EventBus))
            {
                return;
            }

            m_Subscription = m_EventBus.Subscribe<PlayerMovementChanged>(OnMovementChanged);
        }

        private void OnDisable()
        {
            m_Subscription?.Dispose();
            m_Subscription = null;
        }

        /// <summary>在事件总线就绪后重新建立订阅。由场景启动流程调用。</summary>
        public void Rebind(EventBus eventBus)
        {
            m_Subscription?.Dispose();
            m_EventBus = eventBus;
            m_Subscription = m_EventBus.Subscribe<PlayerMovementChanged>(OnMovementChanged);
        }

        /// <summary>直接把位置与朝向设置到目标值，不经过插值。用于场景初始化与传送。</summary>
        public void SnapTo(Vector2 position, Vector2 facing)
        {
            SimulatedPosition = position;
            FacingDegrees = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            ApplyTransform(instant: true);
        }

        private void OnMovementChanged(PlayerMovementChanged evt)
        {
            if (evt.PlayerId != m_PlayerId)
            {
                return;
            }

            SimulatedPosition = new Vector2(evt.Position.X, evt.Position.Y);

            if (!evt.Facing.IsNearlyZero)
            {
                var target = Mathf.Atan2(evt.Facing.Y, evt.Facing.X) * Mathf.Rad2Deg;

                // 直接赋值会让角色在鼠标快速划过时瞬间翻转，因此按角速度插值。
                FacingDegrees = Mathf.MoveTowardsAngle(
                    FacingDegrees,
                    target,
                    m_FacingDegreesPerSecond * Time.deltaTime);
            }

            ApplyTransform(instant: false);
        }

        private void ApplyTransform(bool instant)
        {
            var target = new Vector3(SimulatedPosition.x, m_GroundOffset, SimulatedPosition.y);
            transform.position = instant ? target : Vector3.Lerp(transform.position, target, 0.5f);

            // 坐标轴映射说明：模拟层使用二维 XY 平面，Unity 场景使用 XZ 水平面，
            // 因此二维 Y 对应三维 Z；绕三维 Y 轴旋转时角度取负。
            transform.rotation = Quaternion.Euler(0f, -FacingDegrees, 0f);
        }
    }
}
