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
                // 朝向直接采用模拟层的结果，不做插值。
                //
                // 这里曾经按角速度插值，结果出现了明显的错位：目标朝向变化后，
                // 模型要过若干帧才转过去，而准星是即时的，两者看起来就不在一条线上。
                // 更糟的是每帧推进量取决于 deltaTime——当编辑器失焦导致 deltaTime 极小时，
                // 每帧只能转不到一度，模型会长时间停在错误朝向上。
                //
                // 俯视角下朝向是核心反馈（玩家靠它判断自己在瞄哪里），
                // 因此宁可牺牲一点圆滑，也要保证即时准确。
                FacingDegrees = Mathf.Atan2(evt.Facing.Y, evt.Facing.X) * Mathf.Rad2Deg;
            }

            ApplyTransform(instant: false);
        }

        private void ApplyTransform(bool instant)
        {
            var target = new Vector3(SimulatedPosition.x, m_GroundOffset, SimulatedPosition.y);
            transform.position = instant ? target : Vector3.Lerp(transform.position, target, 0.5f);

            // 坐标轴映射说明（这段映射容易搞错，特此写明推导依据）：
            //
            // 模拟层是二维 XY 平面：0 度指向 +X，90 度指向 +Y。
            // Unity 场景是 XZ 水平面，相机从 -Z 方向朝 +Z 俯视，因此：
            //   屏幕上方 = 世界 +Z，屏幕右方 = 世界 +X。
            //
            // 角色的占位模型朝向是本地 +Z（即模型的"鼻子"在 +Z），
            // 要让模型指向模拟层 +X 方向，需要把本地 +Z 转到世界 +Z，即旋转 90 度；
            // 要指向模拟层 +Y 方向，需要把本地 +Z 转到世界 +X，即旋转 0 度。
            //
            // 由此得到映射关系：Unity 旋转角 = 90 - 模拟角度。
            //
            // 历史记录：这里先后写错过两次——第一次多取了一个负号（差 180 度），
            // 第二次误以为两个坐标系轴向一一对应而直接使用模拟角度（差 90 度）。
            // 两次都表现为"角色朝向与准星不在一条线上"。
            transform.rotation = Quaternion.Euler(0f, 90f - FacingDegrees, 0f);
        }
    }
}
