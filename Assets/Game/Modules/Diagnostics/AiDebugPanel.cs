using System;
using System.Collections.Generic;
using System.Text;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using RaidDemo.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.Diagnostics
{
    /// <summary>
    /// 开发者数据面板：玩家状态、每个 AI 的详细数值，以及最近的状态迁移日志。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么面板要有"状态迁移日志"：</b>数值面板回答"它现在怎么样"，
    /// 而日志回答"它是怎么变成现在这样的"。排查 AI 时后者的价值往往更高——
    /// "从巡逻变成调查是因为听到动静"与"因为看不见目标又回到巡逻"是完全不同的两种处境。</para>
    ///
    /// <para>日志由 <see cref="AiStateChangedEvent"/> 驱动，因此它显示的**就是状态机自己记录的理由**，
    /// 不存在"面板的解释与真实原因不一致"的可能。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AiDebugPanel : MonoBehaviour
    {
        /// <summary>画布排序值。高于受击闪烁（400）与准星（100），保证调试信息不被遮挡。</summary>
        private const int SortingOrder = 500;

        /// <summary>保留的迁移日志条数。</summary>
        private const int MaxLogEntries = 8;

        private const float PanelWidth = 660f;
        private const float PanelHeight = 470f;
        private const float Margin = 16f;
        private const int FontSize = 13;
        private const int TitleFontSize = 15;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color TitleColor = new Color(1f, 0.85f, 0.45f);
        private static readonly Color TextColor = new Color(0.93f, 0.95f, 0.98f);
        private static readonly Color DimColor = new Color(0.68f, 0.72f, 0.78f);
        private static readonly Color LogColor = new Color(0.72f, 0.85f, 1f);

        private readonly List<string> m_Log = new List<string>(MaxLogEntries);
        private readonly StringBuilder m_Builder = new StringBuilder(512);

        private IAiDebugContext m_Context;
        private IDisposable m_Subscription;
        private GameObject m_CanvasHost;
        private Text m_TitleLabel;
        private Text m_PlayerLabel;
        private Text m_ParameterLabel;
        private Text m_AgentLabel;
        private Text m_LogLabel;
        private bool m_Visible;

        /// <summary>当前保留的日志条数。</summary>
        public int LogCount
        {
            get { return m_Log.Count; }
        }

        /// <summary>
        /// 创建面板并订阅状态迁移事件。
        /// </summary>
        /// <param name="context">世界状态入口。</param>
        /// <param name="parent">画布的父节点。</param>
        /// <param name="eventBus">事件总线，用于接收状态迁移日志。</param>
        public void Initialize(IAiDebugContext context, Transform parent, EventBus eventBus)
        {
            m_Context = context;
            BuildLayout(parent);

            m_Subscription?.Dispose();
            m_Subscription = eventBus?.Subscribe<AiStateChangedEvent>(OnStateChanged);
        }

        /// <summary>开关面板显示。</summary>
        public void SetVisible(bool visible)
        {
            m_Visible = visible;
            if (m_CanvasHost != null)
            {
                m_CanvasHost.SetActive(visible);
            }
        }

        /// <summary>按本帧数据刷新面板文本。</summary>
        public void Refresh(AiDebugSnapshot snapshot)
        {
            if (!m_Visible || snapshot == null)
            {
                return;
            }

            m_PlayerLabel.text = BuildPlayerText(snapshot);
            m_ParameterLabel.text = BuildParameterText(snapshot);
            m_AgentLabel.text = BuildAgentText(snapshot);
            m_LogLabel.text = m_Log.Count == 0 ? "（还没有状态迁移）" : string.Join("\n", m_Log);
        }

        private void OnDestroy()
        {
            m_Subscription?.Dispose();
            m_Subscription = null;
        }

        /// <summary>记录一条迁移日志，最新的在最上面。</summary>
        private void OnStateChanged(AiStateChangedEvent evt)
        {
            m_Log.Insert(0, $"[{evt.Time,5:F1}s] AI#{evt.CombatantId} {evt.Previous} → {evt.Current}（{evt.Reason}）");
            while (m_Log.Count > MaxLogEntries)
            {
                m_Log.RemoveAt(m_Log.Count - 1);
            }
        }

        /// <summary>玩家状态行。</summary>
        private string BuildPlayerText(AiDebugSnapshot snapshot)
        {
            m_Builder.Clear();
            m_Builder.Append("玩家：");

            if (snapshot.Target.Exists && m_Context != null && m_Context.World != null
                && m_Context.World.TryGet(m_Context.PlayerCombatantId, out var player))
            {
                m_Builder.Append($"生命 {player.Health:F0}/{player.MaxHealth:F0}");
            }
            else
            {
                m_Builder.Append("已阵亡或未登记");
            }

            m_Builder.Append($"　噪音：{DescribeNoiseTier(snapshot.PlayerNoiseTier)}");
            if (snapshot.PlayerNoiseRadiusMeters > 0f)
            {
                m_Builder.Append($"（可听半径 {snapshot.PlayerNoiseRadiusMeters:F0} 米）");
            }

            // A-02：把速度与播放倍率摆在一起，滑步问题可以当场判断是倍率没跟速度走、
            // 还是剪辑本身的设计速度估错了。
            m_Builder.Append($"　速度 {snapshot.PlayerSpeedMetersPerSecond:F1} m/s");
            m_Builder.Append($"　动画倍率 {snapshot.PlayerPlaybackRate:F2}×");
            m_Builder.Append(snapshot.PlayerSpotted ? "　⚠ 已被发现" : "　未被发现");
            return m_Builder.ToString();
        }

        /// <summary>参数行：把当前生效的感知参数摆出来，方便对照调参。</summary>
        private string BuildParameterText(AiDebugSnapshot snapshot)
        {
            var profile = snapshot.Profile;
            if (profile == null)
            {
                return "参数：不可用";
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "参数：必定发现 {0:F0} 米　警惕到 {1:F0} 米　视角 {2:F0}°　听觉 {3:F0}/{4:F0}/{5:F0} 米\n" +
                "　　　反应：必定 {6:F1}s／警惕确认 {7:F1}s　记忆 {8:F1}s　枪声＝武器射程",
                profile.GuaranteedDetectionDistance,
                profile.ViewDistanceMeters,
                profile.ViewAngleDegrees,
                profile.HearingRadiusWalk,
                profile.HearingRadiusSprint,
                profile.HearingRadiusOverloaded,
                profile.GuaranteedReactionSeconds,
                profile.AlertConfirmSeconds,
                profile.MemorySeconds);
        }

        /// <summary>每个 AI 一行。</summary>
        private string BuildAgentText(AiDebugSnapshot snapshot)
        {
            m_Builder.Clear();
            m_Builder.Append($"AI（存活 {snapshot.AliveAgentCount}）　时钟 {snapshot.ElapsedSeconds:F1}s");

            var entries = snapshot.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var agent = entry.Agent;
                m_Builder.Append('\n');
                m_Builder.Append(
                    $"#{agent.CombatantId} {agent.CurrentState} {entry.TimeInState:F1}s　" +
                    $"{entry.Detection.Describe()} {entry.Detection.DistanceMeters:F1}米/{entry.Detection.AngleDegrees:F0}°　" +
                    $"生命 {agent.Health:F0}　弹匣 {agent.MagazineAmmo}　备弹 {agent.ReserveAmmo}");

                if (entry.HasMemory)
                {
                    m_Builder.Append($"　记忆 {entry.MemoryRemainingSeconds:F1}s");
                }
            }

            return m_Builder.ToString();
        }

        /// <summary>噪音档位的中文名。</summary>
        private static string DescribeNoiseTier(NoiseTier tier)
        {
            switch (tier)
            {
                case NoiseTier.Walk:
                    return "步行";
                case NoiseTier.Sprint:
                    return "奔跑";
                case NoiseTier.Overloaded:
                    return "超载";
                default:
                    return "静止";
            }
        }

        /// <summary>搭建面板布局。</summary>
        private void BuildLayout(Transform parent)
        {
            m_CanvasHost = new GameObject("AiDebugPanelCanvas", typeof(Canvas));
            m_CanvasHost.transform.SetParent(parent, worldPositionStays: false);

            var canvas = m_CanvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var backgroundRect = (RectTransform)background.transform;
            backgroundRect.SetParent(m_CanvasHost.transform, worldPositionStays: false);
            backgroundRect.anchorMin = new Vector2(0f, 1f);
            backgroundRect.anchorMax = new Vector2(0f, 1f);
            backgroundRect.pivot = new Vector2(0f, 1f);
            backgroundRect.anchoredPosition = new Vector2(Margin, -Margin);
            backgroundRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            var image = background.GetComponent<Image>();
            image.color = PanelColor;
            image.raycastTarget = false;

            var top = -Margin;
            m_TitleLabel = CreateText(backgroundRect, "Title", "开发者模式　F1 世界可视化　F2 数据面板", top - 6f, 22f, TitleFontSize, TitleColor);
            m_PlayerLabel = CreateText(backgroundRect, "Player", string.Empty, top - 30f, 20f, FontSize, TextColor);
            m_ParameterLabel = CreateText(backgroundRect, "Parameters", string.Empty, top - 54f, 20f, FontSize, DimColor);
            m_AgentLabel = CreateText(backgroundRect, "Agents", string.Empty, top - 78f, 200f, FontSize, TextColor);
            m_LogLabel = CreateText(backgroundRect, "Log", string.Empty, top - 292f, 170f, FontSize, LogColor);

            // 面板默认关闭：调试工具的默认状态必须是"不存在"。
            SetVisible(false);
        }

        /// <summary>创建一个左对齐文本块。</summary>
        private static Text CreateText(
            RectTransform parent,
            string name,
            string content,
            float topOffset,
            float height,
            int fontSize,
            Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, topOffset);
            rect.sizeDelta = new Vector2(PanelWidth - 24f, height);

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(fontSize);
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = TextAnchor.UpperLeft;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
