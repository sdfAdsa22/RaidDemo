using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 安全屋的界面：交互提示、提示条，以及出口的地图选择。
    /// </summary>
    /// <remarks>
    /// <para>安全屋没有战局 HUD（没有生命、弹药、倒计时），因此它需要自己的轻量界面。
    /// 三样东西合成一个组件：它们都属于「安全屋说了什么」，而分开三个文件只会让装配更碎。</para>
    ///
    /// <para>地图选择用键盘数字键而不是可点击按钮：出口是一个**走近再按 E** 的地方，
    /// 此时玩家的手已经在键盘上了。1 个可用 + 2 个上锁，让「选择」这件事在演示里看得见。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SafeHouseUI : MonoBehaviour
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        private static readonly Color TextColor = new Color(0.94f, 0.94f, 0.96f);
        private static readonly Color DimColor = new Color(0.62f, 0.62f, 0.68f);
        private static readonly Color LockedColor = new Color(0.48f, 0.48f, 0.52f);
        private static readonly Color HighlightColor = new Color(0.36f, 0.85f, 0.52f);
        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.96f);

        private Action m_OnDeploy;
        private Text m_PromptLabel;
        private Text m_HintLabel;
        private GameObject m_MapRoot;
        private float m_HintRemaining;

        /// <summary>地图选择面板是否打开。</summary>
        public bool IsOpen
        {
            get { return m_MapRoot != null && m_MapRoot.activeSelf; }
        }

        /// <summary>构建界面。</summary>
        public void Initialize(Action onDeploy)
        {
            m_OnDeploy = onDeploy;

            var canvasHost = new GameObject("SafeHouseCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasHost.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;

            var scaler = canvasHost.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            var root = (RectTransform)canvasHost.transform;
            m_PromptLabel = CreateLabel(root, string.Empty, new Vector2(0.5f, 0f), new Vector2(0f, 220f), 22, TextColor);
            m_HintLabel = CreateLabel(root, string.Empty, new Vector2(0.5f, 0f), new Vector2(0f, 176f), 18, DimColor);
            m_HintLabel.gameObject.SetActive(false);

            BuildMapPanel(root);
        }

        /// <summary>设置交互提示；传 null 表示隐藏。</summary>
        public void SetPrompt(string prompt)
        {
            if (m_PromptLabel == null)
            {
                return;
            }

            var has = !string.IsNullOrEmpty(prompt);
            m_PromptLabel.gameObject.SetActive(has);
            if (has)
            {
                m_PromptLabel.text = prompt;
            }
        }

        /// <summary>显示一条短提示，几秒后自动消失。</summary>
        public void ShowHint(string hint)
        {
            if (m_HintLabel == null)
            {
                return;
            }

            m_HintLabel.text = hint;
            m_HintLabel.gameObject.SetActive(true);
            m_HintRemaining = 2.5f;
        }

        /// <summary>打开地图选择。</summary>
        public void ShowMap()
        {
            if (m_MapRoot != null)
            {
                m_MapRoot.SetActive(true);
            }
        }

        /// <summary>关闭地图选择。</summary>
        public void HideMap()
        {
            if (m_MapRoot != null)
            {
                m_MapRoot.SetActive(false);
            }
        }

        /// <summary>构建地图选择面板：1 个可用 + 2 个上锁。</summary>
        private void BuildMapPanel(RectTransform parent)
        {
            var host = new GameObject("MapPanel", typeof(RectTransform), typeof(Image));
            m_MapRoot = host;
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(560f, 340f);
            host.GetComponent<Image>().color = PanelColor;

            CreateLabel(rect, "选择出击地图", new Vector2(0.5f, 1f), new Vector2(0f, -40f), 26, TextColor);
            CreateLabel(rect, "1  工业区与集装箱仓库　　可用", new Vector2(0f, 1f), new Vector2(60f, -120f), 20, HighlightColor);
            CreateLabel(rect, "2  港口　　未开放", new Vector2(0f, 1f), new Vector2(60f, -170f), 20, LockedColor);
            CreateLabel(rect, "3  农场　　未开放", new Vector2(0f, 1f), new Vector2(60f, -220f), 20, LockedColor);
            CreateLabel(rect, "按 1 出击　·　按 Esc 返回", new Vector2(0.5f, 0f), new Vector2(0f, 44f), 17, DimColor);

            m_MapRoot.SetActive(false);
        }

        /// <summary>处理地图选择与提示计时。</summary>
        private void Update()
        {
            if (m_HintRemaining > 0f)
            {
                m_HintRemaining -= Time.unscaledDeltaTime;
                if (m_HintRemaining <= 0f && m_HintLabel != null)
                {
                    m_HintLabel.gameObject.SetActive(false);
                }
            }

            if (!IsOpen)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                HideMap();
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                HideMap();
                m_OnDeploy?.Invoke();
                return;
            }

            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.digit3Key.wasPressedThisFrame)
            {
                ShowHint("这张地图还没有开放");
            }
        }

        /// <summary>创建一个文本标签。</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string content,
            Vector2 anchor,
            Vector2 anchoredPosition,
            int fontSize,
            Color color)
        {
            var host = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(460f, 34f);

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(fontSize);
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = anchor.x > 0f ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }
    }
}
