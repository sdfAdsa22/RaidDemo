using System.Collections.Generic;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.Diagnostics
{
    /// <summary>
    /// 开发者模式总控：按键开关、收集数据、驱动世界绘制与数据面板。
    /// </summary>
    /// <remarks>
    /// <para><b>按钮分工：</b><c>F1</c> 开关世界可视化（视野锥、听觉圈、视线、巡逻路线、寻路路径、记忆点、状态标签），
    /// <c>F2</c> 开关数据面板（玩家状态、AI 数值、状态迁移日志）。</para>
    ///
    /// <para><b>为什么直接读键盘而不是走输入动作表：</b>调试键不需要改键与重映射，
    /// 塞进玩家的输入映射只会让动作表多出一个与玩法无关的条目，还会让"玩家能做什么"这件事变模糊。</para>
    ///
    /// <para><b>关闭时零开销：</b>两个开关都关着时，<c>Update</c> 在读键之后立刻返回，
    /// 不收集数据、不绘制、不产生任何分配。调试工具不应该给正常游玩带来任何成本。</para>
    ///
    /// <para><b>只读：</b>本类只读取 AI 与战斗层的状态，绝不调用会改变玩法状态的逻辑。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AiDebugOverlay : MonoBehaviour
    {
        /// <summary>记忆标记的半径（米）。取小值以免与听觉圈混淆。</summary>
        private const float MemoryMarkerRadius = 0.55f;

        /// <summary>巡逻路线的采样点数上限（防御性上限，正常路线只有几个点）。</summary>
        private const int MaxRoutePoints = 32;

        private static readonly Color PatrolRouteColor = new Color(0.55f, 0.62f, 0.72f, 0.35f);
        private static readonly Color PathColor = new Color(0.45f, 0.95f, 0.95f, 0.85f);
        private static readonly Color MemoryColor = new Color(0.95f, 0.45f, 0.95f, 0.6f);
        /// <summary>
        /// 三档听觉参考圈的颜色。
        /// </summary>
        /// <remarks>第一版取了 0.18 的不透明度，在灰色地面上几乎看不见——调试图形的前提是"看得见"。</remarks>
        private static readonly Color ReferenceRingColor = new Color(0.45f, 0.62f, 0.9f, 0.32f);
        private static readonly Color SightLineColor = new Color(1f, 0.95f, 0.6f, 0.9f);

        /// <summary>枪声圈的显示时长（秒）。比一次点射的间隔略长，便于看清。</summary>
        private const float GunshotFlashSeconds = 0.4f;

        /// <summary>枪声圈的颜色。刻意用高亮度，与其他圈区分开。</summary>
        private static readonly Color GunshotRingColor = new Color(1f, 0.96f, 0.8f, 0.95f);

        private static readonly Color WalkRingColor = new Color(0.55f, 0.95f, 0.55f, 0.6f);
        private static readonly Color SprintRingColor = new Color(1f, 0.72f, 0.3f, 0.7f);
        private static readonly Color OverloadedRingColor = new Color(1f, 0.35f, 0.3f, 0.8f);

        private readonly AiDebugSnapshot m_Snapshot = new AiDebugSnapshot();
        private readonly List<Vector3> m_WorldPoints = new List<Vector3>(MaxRoutePoints);
        private readonly List<Vector2F> m_PlaneScratch = new List<Vector2F>(MaxRoutePoints);

        private AiDebugShapeRenderer m_Shapes;
        private AiDebugLabels m_Labels;
        private AiDebugPanel m_Panel;
        private IAiDebugContext m_Context;
        private System.IDisposable m_WeaponNoiseSubscription;
        private Vector3 m_GunshotPosition;
        private float m_GunshotRadius;
        private float m_GunshotFlashRemaining;
        private bool m_WorldVisible;
        private bool m_PanelVisible;

        /// <summary>世界可视化是否开启。</summary>
        public bool WorldVisible
        {
            get { return m_WorldVisible; }
        }

        /// <summary>数据面板是否开启。</summary>
        public bool PanelVisible
        {
            get { return m_PanelVisible; }
        }

        /// <summary>本帧的调试数据。测试与调试脚本可读取。</summary>
        public AiDebugSnapshot Snapshot
        {
            get { return m_Snapshot; }
        }

        /// <summary>本帧绘制的图形数量。用于确认"关闭时确实没有绘制"。</summary>
        public int ActiveShapeCount
        {
            get { return m_Shapes != null ? m_Shapes.ActiveCount : 0; }
        }

        /// <summary>调试线对象池的大小。用于确认绘制确实在复用对象。</summary>
        public int ShapePoolSize
        {
            get { return m_Shapes != null ? m_Shapes.PoolSize : 0; }
        }

        /// <summary>
        /// 初始化调试工具。由启动层在装配完成后调用一次。
        /// </summary>
        /// <param name="context">世界状态入口。</param>
        /// <param name="eventBus">事件总线（面板用它接收状态迁移日志）。</param>
        public void Initialize(IAiDebugContext context, EventBus eventBus)
        {
            m_Context = context;

            var shapesHost = new GameObject("DebugShapes");
            shapesHost.transform.SetParent(transform, worldPositionStays: false);
            m_Shapes = new AiDebugShapeRenderer(shapesHost.transform);

            m_Labels = gameObject.AddComponent<AiDebugLabels>();
            m_Labels.Initialize(transform);

            m_Panel = gameObject.AddComponent<AiDebugPanel>();
            m_Panel.Initialize(context, transform, eventBus);

            // 枪声是全图最吵的声源（默认 20 米），但它是瞬时的：听者只会看到"AI 突然朝那边走"。
            // 开火后短暂画一圈，才能把"这一枪惊动了多远"直接摆到眼前。
            m_WeaponNoiseSubscription = eventBus?.Subscribe<WeaponFiredEvent>(OnWeaponFired);

            // 默认全关：调试工具的初始状态必须是"不存在"。
            SetWorldVisible(false);
            SetPanelVisible(false);
        }

        /// <summary>开关世界可视化。供按键与自动化验证调用。</summary>
        public void SetWorldVisible(bool visible)
        {
            m_WorldVisible = visible;
            m_Labels?.SetVisible(visible);

            if (!visible)
            {
                m_Shapes?.HideAll();
            }
        }

        /// <summary>开关数据面板。</summary>
        public void SetPanelVisible(bool visible)
        {
            m_PanelVisible = visible;
            m_Panel?.SetVisible(visible);
        }

        private void OnDestroy()
        {
            m_WeaponNoiseSubscription?.Dispose();
            m_WeaponNoiseSubscription = null;
            m_Shapes?.Dispose();
            m_Shapes = null;
        }

        private void Update()
        {
            HandleToggleKeys();

            if (m_GunshotFlashRemaining > 0f)
            {
                m_GunshotFlashRemaining -= Time.deltaTime;
            }

            if (m_Context == null || (!m_WorldVisible && !m_PanelVisible))
            {
                return;
            }

            m_Snapshot.Refresh(m_Context);

            if (m_WorldVisible)
            {
                DrawWorld();
                m_Labels.Refresh(m_Snapshot, m_Context.ViewCamera);
            }

            if (m_PanelVisible)
            {
                m_Panel.Refresh(m_Snapshot);
            }
        }

        /// <summary>读取 F1 / F2。</summary>
        private void HandleToggleKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                SetWorldVisible(!m_WorldVisible);
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                SetPanelVisible(!m_PanelVisible);
            }
        }

        /// <summary>绘制世界可视化。</summary>
        private void DrawWorld()
        {
            m_Shapes.BeginFrame();

            DrawPlayerNoiseRings();
            DrawGunshotRing();

            var entries = m_Snapshot.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                DrawAgent(entries[i]);
            }

            m_Shapes.EndFrame();
        }

        /// <summary>
        /// 在**玩家**周围画听觉半径参考圈。
        /// </summary>
        /// <remarks>
        /// 听觉半径属于"噪音源"而不是"听者"：画在每个 AI 头上会让人误以为
        /// 半径是感知者的属性，而且无法回答"我现在这么跑，多远的人能听见"。
        /// 画在玩家周围时，一眼就能读出自己当前的安全距离。
        /// </remarks>
        private void DrawPlayerNoiseRings()
        {
            var profile = m_Snapshot.Profile;
            var target = m_Snapshot.Target;
            if (profile == null || !target.Exists)
            {
                return;
            }

            var center = new Vector3(target.Position.X, 0f, target.Position.Y);

            // 三档参考圈：始终画出，方便对照"还能再跑多远才被听见"。
            m_Shapes.DrawCircle(center, profile.HearingRadiusWalk, ReferenceRingColor);
            m_Shapes.DrawCircle(center, profile.HearingRadiusSprint, ReferenceRingColor);
            m_Shapes.DrawCircle(center, profile.HearingRadiusOverloaded, ReferenceRingColor);

            // 当前档位高亮。
            var radius = m_Snapshot.PlayerNoiseRadiusMeters;
            if (radius <= 0f)
            {
                return;
            }

            m_Shapes.DrawCircle(center, radius, ResolveNoiseColor(m_Snapshot.PlayerNoiseTier));
        }

        /// <summary>刚开过枪时，在枪口位置画一圈枪声半径。</summary>
        private void DrawGunshotRing()
        {
            if (m_GunshotFlashRemaining <= 0f || m_GunshotRadius <= 0f)
            {
                return;
            }

            var fade = m_GunshotFlashRemaining / GunshotFlashSeconds;
            var color = GunshotRingColor;
            color.a *= fade;

            m_Shapes.DrawCircle(m_GunshotPosition, m_GunshotRadius, color);
        }

        /// <summary>记录一次枪声，用于下一帧起短暂画出它的可听范围。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            m_GunshotPosition = new Vector3(evt.Origin.x, 0f, evt.Origin.z);
            // 半径取自开火方：手枪 8 米、步枪 12 米，画出来就能直接对比不同武器的暴露范围。
            m_GunshotRadius = evt.NoiseRadiusMeters;
            m_GunshotFlashRemaining = GunshotFlashSeconds;
        }

        /// <summary>绘制单个 AI 的视野锥、视线、路线、路径与记忆点。</summary>
        private void DrawAgent(in AiDebugSnapshot.Entry entry)
        {
            var agent = entry.Agent;
            var profile = m_Snapshot.Profile;
            if (profile == null)
            {
                return;
            }

            var position = new Vector3(agent.Position.X, 0f, agent.Position.Y);
            var stateColor = EnemyAgentView.ResolveColor(agent.CurrentState);

            // 视野锥：颜色跟随状态，一眼能看出"这个扇形是哪个 AI 的"。
            var visionColor = new Color(stateColor.r, stateColor.g, stateColor.b, 0.5f);
            m_Shapes.DrawFanOutline(
                position,
                agent.FacingDegrees,
                profile.ViewAngleDegrees,
                profile.ViewDistanceMeters,
                visionColor);

            // 看得见时才画视线：这条线是"它为什么开枪"的直接证据。
            if (entry.Detection.Sees && m_Snapshot.Target.Exists)
            {
                var targetPosition = new Vector3(
                    m_Snapshot.Target.Position.X,
                    0f,
                    m_Snapshot.Target.Position.Y);
                m_Shapes.DrawLine(position, targetPosition, SightLineColor);
            }

            DrawPatrolRoute(agent);
            DrawCurrentPath(agent, position);
            DrawMemoryMarker(agent);
        }

        /// <summary>绘制巡逻路线（闭合折线）。</summary>
        private void DrawPatrolRoute(AiAgent agent)
        {
            var route = agent.PatrolRoute;
            if (route == null || route.IsEmpty)
            {
                return;
            }

            route.CopyPoints(m_PlaneScratch);
            ToWorldPoints(m_PlaneScratch);
            m_Shapes.DrawPolyline(m_WorldPoints, loop: true, PatrolRouteColor);

            // 起点单独标一下：路线是循环的，"从哪里开始"会影响对 AI 走位的预期。
            if (m_WorldPoints.Count > 0)
            {
                m_Shapes.DrawCircle(m_WorldPoints[0], 0.4f, PatrolRouteColor);
            }
        }

        /// <summary>绘制当前寻路路径（从 AI 位置开始的折线）。</summary>
        private void DrawCurrentPath(AiAgent agent, Vector3 agentPosition)
        {
            var count = agent.CopyPathWaypoints(m_PlaneScratch);
            if (count <= 0)
            {
                return;
            }

            m_WorldPoints.Clear();
            m_WorldPoints.Add(agentPosition);
            for (var i = 0; i < m_PlaneScratch.Count; i++)
            {
                var point = m_PlaneScratch[i];
                m_WorldPoints.Add(new Vector3(point.X, 0f, point.Y));
            }

            m_Shapes.DrawPolyline(m_WorldPoints, loop: false, PathColor);
        }

        /// <summary>绘制记忆中的最后已知位置。</summary>
        private void DrawMemoryMarker(AiAgent agent)
        {
            var memory = agent.Context?.Memory;
            if (memory == null || !memory.HasMemory)
            {
                return;
            }

            var point = memory.LastKnownPosition;
            m_Shapes.DrawCircle(new Vector3(point.X, 0f, point.Y), MemoryMarkerRadius, MemoryColor);
        }

        /// <summary>把平面点列表转换成世界点列表。</summary>
        private void ToWorldPoints(List<Vector2F> planePoints)
        {
            m_WorldPoints.Clear();
            for (var i = 0; i < planePoints.Count; i++)
            {
                var point = planePoints[i];
                m_WorldPoints.Add(new Vector3(point.X, 0f, point.Y));
            }
        }

        /// <summary>噪音档位对应的高亮颜色：越吵越红。</summary>
        private static Color ResolveNoiseColor(NoiseTier tier)
        {
            switch (tier)
            {
                case NoiseTier.Walk:
                    return WalkRingColor;
                case NoiseTier.Sprint:
                    return SprintRingColor;
                case NoiseTier.Overloaded:
                    return OverloadedRingColor;
                default:
                    return ReferenceRingColor;
            }
        }
    }
}
