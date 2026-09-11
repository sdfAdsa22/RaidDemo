using System.Collections.Generic;
using RaidDemo.Presentation;
using RaidDemo.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.Diagnostics
{
    /// <summary>
    /// 每个 AI 头顶的调试标签：状态、检测结果、生命、弹匣。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用"屏幕空间画布 + 每帧反投影"而不是世界空间画布：</b>
    /// 世界空间的文字会被透视缩放，远处的小到看不清、近处的大到糊脸；
    /// 反投影到屏幕空间之后，无论距离远近字号都一致，调试时更容易扫读。
    /// 代价是文字不参与深度遮挡——调试标签本来就应该浮在最上层。</para>
    ///
    /// <para><b>画布不挂 <see cref="CanvasScaler"/>：</b>定位用的是
    /// <c>WorldToScreenPoint</c> 得到的真实屏幕像素，只有"常量像素"模式才对得上。
    /// 代价是高分辨率下字号偏小，对调试工具可以接受。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AiDebugLabels : MonoBehaviour
    {
        /// <summary>画布排序值。低于数据面板（500），高于受击闪烁（400）。</summary>
        private const int SortingOrder = 480;

        /// <summary>标签相对角色脚底的高度（米）。略高于角色头顶与体力弧。</summary>
        private const float HeightAboveHead = 2.4f;

        private const int FontSize = 13;
        private const float LabelWidth = 300f;
        private const float LabelHeight = 40f;

        private readonly List<Text> m_Labels = new List<Text>(8);

        private GameObject m_CanvasHost;
        private bool m_Visible;

        /// <summary>已创建的标签数量。用于确认池是否按预期增长。</summary>
        public int LabelCount
        {
            get { return m_Labels.Count; }
        }

        /// <summary>
        /// 创建画布。由调试总控在初始化时调用一次。
        /// </summary>
        /// <param name="parent">画布的父节点。</param>
        public void Initialize(Transform parent)
        {
            if (m_CanvasHost != null)
            {
                return;
            }

            m_CanvasHost = new GameObject("AiDebugLabelsCanvas", typeof(Canvas));
            m_CanvasHost.transform.SetParent(parent, worldPositionStays: false);

            var canvas = m_CanvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
        }

        /// <summary>开关标签显示。关闭时会立刻隐藏全部标签。</summary>
        public void SetVisible(bool visible)
        {
            m_Visible = visible;
            if (!visible)
            {
                HideAll();
            }
        }

        /// <summary>
        /// 按本帧的调试数据刷新全部标签。
        /// </summary>
        /// <param name="snapshot">调试数据。</param>
        /// <param name="camera">用于反投影的相机，可为 null（此时全部隐藏）。</param>
        public void Refresh(AiDebugSnapshot snapshot, Camera camera)
        {
            if (!m_Visible || snapshot == null || camera == null)
            {
                HideAll();
                return;
            }

            var entries = snapshot.Entries;
            EnsureCapacity(entries.Count);

            for (var i = 0; i < m_Labels.Count; i++)
            {
                var label = m_Labels[i];
                if (i >= entries.Count)
                {
                    SetLabelActive(label, false);
                    continue;
                }

                var entry = entries[i];

                // 标签挂在角色上方固定高度处，再反投影回屏幕：
                // 俯视角下这个位置不会遮挡角色本身，也不会盖住准星。
                var agentPosition = entry.Agent.Position;
                var world = new Vector3(agentPosition.X, HeightAboveHead, agentPosition.Y);
                var screen = camera.WorldToScreenPoint(world);

                if (screen.z <= 0f)
                {
                    // 点在相机背后，画出来会出现在相反方向的屏幕上。
                    SetLabelActive(label, false);
                    continue;
                }

                var rect = label.rectTransform;
                rect.anchoredPosition = new Vector2(screen.x, screen.y);

                label.text = BuildText(entry);
                label.color = EnemyAgentView.ResolveColor(entry.Agent.CurrentState);
                SetLabelActive(label, true);
            }
        }

        /// <summary>组装两行文本。</summary>
        private static string BuildText(in AiDebugSnapshot.Entry entry)
        {
            var agent = entry.Agent;
            var first = $"#{agent.CombatantId} {agent.CurrentState} {entry.TimeInState:F1}s";

            // 弹匣与备弹分开写：写成 "弹匣 27/90" 会被读成"27 发 / 容量 90"，
            // 而 90 其实是备弹。调试信息宁可多两个字，也不要让人会错意。
            var second = $"{entry.Detection.Describe()} · 生命 {agent.Health:F0} · 弹匣 {agent.MagazineAmmo} · 备弹 {agent.ReserveAmmo}";
            if (entry.HasMemory)
            {
                second += $" · 记忆 {entry.MemoryRemainingSeconds:F1}s";
            }

            return first + "\n" + second;
        }

        /// <summary>按需创建标签。</summary>
        private void EnsureCapacity(int count)
        {
            while (m_Labels.Count < count)
            {
                m_Labels.Add(CreateLabel(m_CanvasHost.transform, $"Label_{m_Labels.Count:D2}"));
            }
        }

        /// <summary>隐藏全部标签。</summary>
        private void HideAll()
        {
            for (var i = 0; i < m_Labels.Count; i++)
            {
                SetLabelActive(m_Labels[i], false);
            }
        }

        /// <summary>切换单个标签的显隐，避免重复写 activeSelf 造成无意义的开销。</summary>
        private static void SetLabelActive(Text label, bool visible)
        {
            if (label != null && label.gameObject.activeSelf != visible)
            {
                label.gameObject.SetActive(visible);
            }
        }

        /// <summary>创建一个文本标签。</summary>
        private static Text CreateLabel(Transform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);

            // 锚在画布左下角、轴心在底部中点：anchoredPosition 因此直接等于屏幕像素坐标，
            // 文字会以该点为中心向上展开。
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(LabelWidth, LabelHeight);

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(FontSize);
            text.fontSize = FontSize;
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            host.SetActive(false);
            return text;
        }
    }
}
