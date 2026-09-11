using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Diagnostics;
using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的开发者模式装配部分，同时充当调试工具读取世界状态的入口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么启动层实现 <see cref="IAiDebugContext"/> 而不是让调试层自己找对象：</b>
    /// 依赖方向必须是单向的（启动层 → 调试层）。调试层若反过来引用启动层就会形成程序集循环，
    /// 因此改为"启动层把只读入口交给调试层"。</para>
    ///
    /// <para>这个文件里没有任何玩法逻辑：它只暴露几个只读属性，并负责在合适的时机
    /// 创建调试组件。调试工具的显示与绘制全部在 <c>RaidDemo.Diagnostics</c> 里。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap : IAiDebugContext
    {
        private AiDebugOverlay m_DebugOverlay;
        private Camera m_DebugViewCamera;

        /// <summary>开发者模式总控。未启用时该组件仍然存在，但关闭状态下零开销。</summary>
        public AiDebugOverlay DebugOverlay
        {
            get { return m_DebugOverlay; }
        }

        /// <inheritdoc />
        public AiDirector Director
        {
            get { return m_AiDirector; }
        }

        /// <inheritdoc />
        public IHitProbe Probe
        {
            get { return m_AiDirector != null ? m_AiDirector.Probe : null; }
        }

        /// <inheritdoc />
        public CombatWorld World
        {
            get { return m_CombatWorld; }
        }

        /// <inheritdoc />
        /// <remarks>
        /// 相机引用缓存在字段里，而不是每帧去 <c>GetComponent</c>：
        /// 调试标签每帧都要用它做反投影，每次都查一次组件是没必要的开销。
        /// </remarks>
        public Camera ViewCamera
        {
            get
            {
                if (m_DebugViewCamera == null)
                {
                    m_DebugViewCamera = m_CameraController != null
                        ? m_CameraController.GetComponent<Camera>()
                        : Camera.main;
                }

                return m_DebugViewCamera;
            }
        }

        /// <inheritdoc />
        public AiTargetInfo Target
        {
            get { return m_CurrentAiTarget; }
        }

        /// <inheritdoc />
        public MovementNoiseTier PlayerNoiseTier
        {
            get { return m_CurrentNoiseTier; }
        }

        /// <summary>
        /// 创建开发者模式工具。
        /// </summary>
        /// <remarks>
        /// 与其它表现层组件一样集中在这里创建（M3 的教训：实现完成但忘记接线，
        /// 表现为"功能看起来根本没做"）。默认两个开关都是关闭的。
        /// </remarks>
        private void BuildDebugTools()
        {
            var host = new GameObject("DeveloperTools");
            host.transform.SetParent(transform, worldPositionStays: false);

            m_DebugOverlay = host.AddComponent<AiDebugOverlay>();
            m_DebugOverlay.Initialize(this, m_EventBus);
        }
    }
}
