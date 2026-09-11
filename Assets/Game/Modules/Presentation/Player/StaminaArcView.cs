using System;
using RaidDemo.Kernel;
using RaidDemo.Simulation;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 角色头顶的弧形体力槽。
    /// </summary>
    /// <remarks>
    /// <para>用两条 <see cref="LineRenderer"/> 画一段圆弧：底下那条是底色（完整圆弧），
    /// 上面那条是当前体力对应的部分。画在角色上方并始终面向相机，
    /// 因此不论角色朝哪走，玩家都能一眼看到体力还剩多少。</para>
    ///
    /// <para><b>体力满时淡出。</b>满体力是绝大多数时候的常态，
    /// 一条常驻的满格圆弧只会变成视觉噪声；只有在体力真的被消耗时出现，
    /// 它才是一条"有事发生"的信息。</para>
    ///
    /// <para>力竭时颜色转为红色并保持显示——那是需要玩家立刻反应的状态。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StaminaArcView : MonoBehaviour
    {
        /// <summary>圆弧的半径（米）。</summary>
        private const float Radius = 0.45f;

        /// <summary>圆弧距离角色脚底的高度（米）。</summary>
        private const float Height = 2.35f;

        /// <summary>圆弧覆盖的角度范围（度）。留出缺口，读起来更像一条进度条而不是一个圈。</summary>
        private const float SweepDegrees = 260f;

        /// <summary>用于绘制圆弧的线段数。越多越圆滑，36 段在灰盒阶段已经看不出折角。</summary>
        private const int SegmentCount = 36;

        /// <summary>线条宽度（米）。</summary>
        private const float LineWidth = 0.06f;

        /// <summary>体力低于该比例时变为警示色。</summary>
        private const float LowStaminaRatio = 0.3f;

        /// <summary>体力满之后圆弧完全淡出所需的秒数。</summary>
        private const float FadeOutSeconds = 0.6f;

        /// <summary>满体力时的透明度。0 表示完全隐藏。</summary>
        private const float HiddenAlpha = 0f;

        /// <summary>正常状态下的弧度颜色。</summary>
        private static readonly Color FillColor = new Color(0.35f, 0.85f, 0.95f, 0.95f);

        /// <summary>体力偏低时的弧度颜色。</summary>
        private static readonly Color LowColor = new Color(1f, 0.65f, 0.25f, 0.95f);

        /// <summary>力竭时的弧度颜色。</summary>
        private static readonly Color ExhaustedColor = new Color(1f, 0.3f, 0.3f, 0.95f);

        /// <summary>底色圆弧的颜色。</summary>
        private static readonly Color BackColor = new Color(0.1f, 0.1f, 0.12f, 0.55f);

        private Transform m_Target;
        private Camera m_Camera;

        private LineRenderer m_Back;
        private LineRenderer m_Fill;
        private Transform m_Root;

        private float m_Stamina = 1f;
        private float m_MaxStamina = 1f;
        private bool m_IsExhausted;
        private float m_Visibility = HiddenAlpha;
        private IDisposable m_Subscription;

        /// <summary>
        /// 初始化。
        /// </summary>
        /// <param name="target">跟随的角色根节点。</param>
        /// <param name="viewCamera">用于让圆弧面向的相机。</param>
        /// <param name="eventBus">事件总线，用于接收体力变化。</param>
        public void Initialize(Transform target, Camera viewCamera, EventBus eventBus)
        {
            m_Target = target;
            m_Camera = viewCamera;

            BuildRenderers();
            UpdateTransform();
            ApplyVisibility();

            if (eventBus != null)
            {
                m_Subscription?.Dispose();
                m_Subscription = eventBus.Subscribe<PlayerMovementChanged>(OnMovementChanged);
            }
        }

        private void OnDestroy()
        {
            m_Subscription?.Dispose();
        }

        private void LateUpdate()
        {
            UpdateTransform();

            var ratio = m_MaxStamina > 0f ? Mathf.Clamp01(m_Stamina / m_MaxStamina) : 0f;

            // 只在体力未满时显示；满体力时快速淡出。
            var wanted = ratio >= 0.999f && !m_IsExhausted ? HiddenAlpha : 1f;
            m_Visibility = Mathf.MoveTowards(m_Visibility, wanted, Time.deltaTime / FadeOutSeconds);

            UpdateFill(ratio);
            ApplyVisibility();
        }

        /// <summary>收到移动状态变化时记录体力值。</summary>
        private void OnMovementChanged(PlayerMovementChanged evt)
        {
            m_Stamina = evt.Stamina;
            m_MaxStamina = evt.MaxStamina;
            m_IsExhausted = evt.IsExhausted;
        }

        /// <summary>创建两条圆弧线。</summary>
        private void BuildRenderers()
        {
            var rootHost = new GameObject("StaminaArc");
            rootHost.transform.SetParent(transform, worldPositionStays: false);
            m_Root = rootHost.transform;

            m_Back = CreateArcLine("Back", SegmentCount + 1);
            m_Fill = CreateArcLine("Fill", SegmentCount + 1);
            SetArcPositions(m_Back, 1f);
        }

        /// <summary>创建一个指定点数的线段渲染器。</summary>
        private LineRenderer CreateArcLine(string name, int pointCount)
        {
            var host = new GameObject(name);
            host.transform.SetParent(m_Root, worldPositionStays: false);

            var line = host.AddComponent<LineRenderer>();
            line.positionCount = pointCount;
            line.useWorldSpace = false;
            line.startWidth = LineWidth;
            line.endWidth = LineWidth;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>按给定比例铺满圆弧上的点。</summary>
        private static void SetArcPositions(LineRenderer line, float ratio)
        {
            var clamped = Mathf.Clamp01(ratio);
            var total = line.positionCount;

            for (var i = 0; i < total; i++)
            {
                var t = total > 1 ? (float)i / (total - 1) : 0f;
                var degrees = -90f - (SweepDegrees * 0.5f) + (SweepDegrees * clamped * t);
                var radians = degrees * Mathf.Deg2Rad;
                line.SetPosition(i, new Vector3(Mathf.Cos(radians) * Radius, Mathf.Sin(radians) * Radius, 0f));
            }
        }

        /// <summary>刷新前景圆弧的长度与颜色。</summary>
        private void UpdateFill(float ratio)
        {
            SetArcPositions(m_Fill, ratio);

            var color = m_IsExhausted
                ? ExhaustedColor
                : ratio <= LowStaminaRatio ? LowColor : FillColor;

            // 透明度跟随可见度：满体力时整条圆弧淡出，而不是突然消失。
            color.a *= m_Visibility;
            m_Fill.startColor = color;
            m_Fill.endColor = color;
        }

        /// <summary>把整个圆弧摆到角色上方并面向相机。</summary>
        private void UpdateTransform()
        {
            if (m_Root == null || m_Target == null)
            {
                return;
            }

            m_Root.position = m_Target.position + (Vector3.up * Height);

            if (m_Camera != null)
            {
                // 与相机同向：圆心在角色身上，圆弧平面始终正对镜头。
                m_Root.rotation = Quaternion.LookRotation(m_Camera.transform.forward, m_Camera.transform.up);
            }
        }

        /// <summary>按当前可见度设置两条线的透明度。</summary>
        private void ApplyVisibility()
        {
            var visible = m_Visibility > 0.01f;
            if (m_Back != null)
            {
                m_Back.enabled = visible;
                var back = BackColor;
                back.a *= m_Visibility;
                m_Back.startColor = back;
                m_Back.endColor = back;
            }

            if (m_Fill != null)
            {
                m_Fill.enabled = visible;
            }
        }
    }
}
