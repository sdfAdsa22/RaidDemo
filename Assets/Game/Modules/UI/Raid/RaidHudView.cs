using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 战局界面：剩余时间、击杀数、搜刮读条、撤离读秒与交互提示。
    /// </summary>
    /// <remarks>
    /// <para><b>它是一个纯粹的显示层。</b>所有数值都由装配层每帧写入，
    /// 本类不查询战局状态、不订阅事件、也不判断规则。
    /// 这样「界面显示什么」与「战局怎么算」可以分别修改，
    /// 联机时服务端推来的权威数值也能直接喂给同一套界面。</para>
    ///
    /// <para>与战斗界面分成两个组件：战斗界面（生命、武器、弹药）在 M3 就存在，
    /// 与战局无关；战局界面属于 M5 的闭环。合成一个类会让两块关注点互相纠缠。</para>
    ///
    /// <para>界面在运行时用代码构建，不依赖预制体，与项目其它界面一致。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RaidHudView : MonoBehaviour
    {
        /// <summary>界面参考分辨率。所有布局数值都按这个尺寸写。</summary>
        private const float ReferenceWidth = 1920f;

        private const float ReferenceHeight = 1080f;

        /// <summary>搜刮读条宽度（像素）。</summary>
        private const float SearchBarWidth = 300f;

        /// <summary>使用读条宽度（像素）。</summary>
        private const float UseBarWidth = 300f;

        /// <summary>回血提示的显示时长（秒）。</summary>
        private const float HealLabelSeconds = 1.6f;

        /// <summary>回血提示的颜色（与撤离成功的绿色同一族，表示「变好」）。</summary>
        private static readonly Color HealColor = new Color(0.40f, 0.90f, 0.55f);

        /// <summary>撤离读条宽度（像素）。</summary>
        private const float ExtractionBarWidth = 360f;

        /// <summary>剩余时间低于该值时倒计时转为警示色（秒）。</summary>
        private const float UrgentThresholdSeconds = 60f;

        private static readonly Color TextColor = new Color(0.94f, 0.94f, 0.96f);
        private static readonly Color DimColor = new Color(0.72f, 0.72f, 0.78f);
        private static readonly Color UrgentColor = new Color(1f, 0.42f, 0.35f);
        private static readonly Color BarBackColor = new Color(0.15f, 0.15f, 0.17f, 0.85f);
        private static readonly Color SearchFillColor = new Color(0.95f, 0.78f, 0.30f);
        private static readonly Color ExtractionFillColor = new Color(0.30f, 0.85f, 0.50f);

        private Text m_TimerLabel;
        private Text m_KillsLabel;
        private Text m_PromptLabel;
        private GameObject m_SearchRoot;
        private Text m_SearchLabel;
        private Image m_SearchFill;
        private GameObject m_ExtractionRoot;
        private Text m_ExtractionLabel;
        private Image m_ExtractionFill;
        private GameObject m_UseRoot;
        private Text m_UseLabel;
        private Image m_UseFill;
        private Text m_HealLabel;
        private float m_HealRemaining;

        /// <summary>构建界面。由装配层在战局开始时调用一次。</summary>
        public void Initialize()
        {
            var canvasHost = new GameObject("RaidHudCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasHost.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // 位于准星（100）之上、背包界面（200）之下：搜刮读条要盖住准星，
            // 但背包一旦打开就必须压住战局信息，否则拖拽物品时会被倒计时挡住视线。
            // 取 140 而不是与战斗界面相同的 150：两者相同时渲染顺序取决于对象创建顺序，
            // 而创建顺序会随装配流程变化，那种不稳定迟早会表现为「某次运行后弹药被倒计时挡住」。
            canvas.sortingOrder = 140;

            var scaler = canvasHost.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            var rootRect = (RectTransform)canvasHost.transform;
            m_TimerLabel = CreateLabel(
                rootRect, "00:00", new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(320f, 52f), 34, TextAnchor.MiddleCenter);

            m_KillsLabel = CreateLabel(
                rootRect, "击杀 0", new Vector2(1f, 1f),
                new Vector2(-48f, -32f), new Vector2(240f, 28f), 18, TextAnchor.MiddleRight);

            BuildExtraction(rootRect);
            BuildPrompt(rootRect);
            BuildSearch(rootRect);
            BuildUse(rootRect);
            BuildHeal(rootRect);
        }

        /// <summary>推进回血提示的倒计时。用非缩放时间，战局结算时也能正常消失。</summary>
        private void Update()
        {
            if (m_HealRemaining <= 0f)
            {
                return;
            }

            m_HealRemaining -= Time.unscaledDeltaTime;
            if (m_HealRemaining > 0f)
            {
                return;
            }

            m_HealRemaining = 0f;
            if (m_HealLabel != null)
            {
                m_HealLabel.gameObject.SetActive(false);
            }
        }

        /// <summary>写入医疗品使用读条。</summary>
        public void SetUseProgress(bool visible, float progress01, string label)
        {
            if (m_UseRoot == null)
            {
                return;
            }

            m_UseRoot.SetActive(visible);
            if (!visible)
            {
                return;
            }

            var ratio = Mathf.Clamp01(progress01);
            m_UseFill.rectTransform.sizeDelta = new Vector2(UseBarWidth * ratio, 0f);
            m_UseLabel.text = string.IsNullOrEmpty(label) ? "使用中…" : label;
        }

        /// <summary>显示一条回血提示，若干秒后自动消失。</summary>
        public void ShowHeal(int amount)
        {
            if (m_HealLabel == null || amount <= 0)
            {
                return;
            }

            m_HealLabel.text = $"生命 +{amount}";
            m_HealLabel.color = HealColor;
            m_HealLabel.gameObject.SetActive(true);
            m_HealRemaining = HealLabelSeconds;
        }

        /// <summary>写入战局倒计时。</summary>
        /// <param name="remainingSeconds">剩余秒数。</param>
        /// <param name="isActive">战局是否仍在进行；结束后倒计时转灰，避免看起来还能继续打。</param>
        public void SetTimer(float remainingSeconds, bool isActive)
        {
            if (m_TimerLabel == null)
            {
                return;
            }

            var clamped = Mathf.Max(0f, remainingSeconds);
            var minutes = Mathf.FloorToInt(clamped / 60f);
            var seconds = Mathf.FloorToInt(clamped % 60f);
            m_TimerLabel.text = $"{minutes:00}:{seconds:00}";
            m_TimerLabel.color = !isActive
                ? DimColor
                : clamped <= UrgentThresholdSeconds ? UrgentColor : TextColor;
        }

        /// <summary>写入击杀数。</summary>
        public void SetKills(int kills)
        {
            if (m_KillsLabel != null)
            {
                m_KillsLabel.text = $"击杀 {kills}";
            }
        }

        /// <summary>写入交互提示；传 null 或空串表示隐藏。</summary>
        public void SetInteractionPrompt(string prompt)
        {
            if (m_PromptLabel == null)
            {
                return;
            }

            var hasPrompt = !string.IsNullOrEmpty(prompt);
            m_PromptLabel.gameObject.SetActive(hasPrompt);
            if (hasPrompt)
            {
                m_PromptLabel.text = prompt;
            }
        }

        /// <summary>写入搜刮读条状态。</summary>
        public void SetSearchProgress(bool visible, float progress01, string label)
        {
            if (m_SearchRoot == null)
            {
                return;
            }

            m_SearchRoot.SetActive(visible);
            if (!visible)
            {
                return;
            }

            var ratio = Mathf.Clamp01(progress01);
            m_SearchFill.rectTransform.sizeDelta = new Vector2(SearchBarWidth * ratio, 0f);
            m_SearchLabel.text = string.IsNullOrEmpty(label) ? "搜刮中…" : label;
        }

        /// <summary>写入撤离读秒状态。</summary>
        public void SetExtraction(bool visible, string zoneName, float progress01)
        {
            if (m_ExtractionRoot == null)
            {
                return;
            }

            m_ExtractionRoot.SetActive(visible);
            if (!visible)
            {
                return;
            }

            var ratio = Mathf.Clamp01(progress01);
            m_ExtractionFill.rectTransform.sizeDelta = new Vector2(ExtractionBarWidth * ratio, 0f);
            m_ExtractionLabel.text = $"正在撤离 {zoneName}";
        }

        /// <summary>创建撤离读秒面板（标题 + 进度条）。</summary>
        private void BuildExtraction(RectTransform parent)
        {
            m_ExtractionRoot = new GameObject("ExtractionPanel", typeof(RectTransform));
            var rect = (RectTransform)m_ExtractionRoot.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -88f);
            rect.sizeDelta = new Vector2(ExtractionBarWidth, 52f);

            m_ExtractionLabel = CreateLabel(
                rect, "正在撤离", new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(ExtractionBarWidth, 26f), 18, TextAnchor.MiddleCenter);

            m_ExtractionFill = CreateBar(rect, ExtractionBarWidth, 14f, ExtractionFillColor);
            m_ExtractionRoot.SetActive(false);
        }

        /// <summary>创建底部的交互提示。</summary>
        private void BuildPrompt(RectTransform parent)
        {
            m_PromptLabel = CreateLabel(
                parent, string.Empty, new Vector2(0.5f, 0f),
                new Vector2(0f, 176f), new Vector2(560f, 32f), 20, TextAnchor.MiddleCenter);
            m_PromptLabel.gameObject.SetActive(false);
        }

        /// <summary>创建医疗品使用读条（在搜刮读条上方一排，避免两者同时出现时重叠）。</summary>
        private void BuildUse(RectTransform parent)
        {
            m_UseRoot = new GameObject("UsePanel", typeof(RectTransform));
            var rect = (RectTransform)m_UseRoot.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 166f);
            rect.sizeDelta = new Vector2(UseBarWidth, 46f);

            m_UseLabel = CreateLabel(
                rect, "使用中…", new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(UseBarWidth, 24f), 17, TextAnchor.MiddleCenter);

            m_UseFill = CreateBar(rect, UseBarWidth, 12f, HealColor);
            m_UseRoot.SetActive(false);
        }

        /// <summary>创建回血提示（显示在生命条上方，右侧留白处）。</summary>
        private void BuildHeal(RectTransform parent)
        {
            m_HealLabel = CreateLabel(
                parent, string.Empty, new Vector2(0f, 0f),
                new Vector2(200f, 250f), new Vector2(220f, 28f), 18, TextAnchor.MiddleLeft);
            m_HealLabel.color = HealColor;
            m_HealLabel.gameObject.SetActive(false);
        }

        /// <summary>创建搜刮读条。</summary>
        private void BuildSearch(RectTransform parent)
        {
            m_SearchRoot = new GameObject("SearchPanel", typeof(RectTransform));
            var rect = (RectTransform)m_SearchRoot.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 108f);
            rect.sizeDelta = new Vector2(SearchBarWidth, 46f);

            m_SearchLabel = CreateLabel(
                rect, "搜刮中…", new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(SearchBarWidth, 24f), 17, TextAnchor.MiddleCenter);

            m_SearchFill = CreateBar(rect, SearchBarWidth, 12f, SearchFillColor);
            m_SearchRoot.SetActive(false);
        }

        /// <summary>在父节点底部创建一个进度条，返回填充图像。</summary>
        private static Image CreateBar(RectTransform parent, float width, float height, Color fillColor)
        {
            var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
            var backRect = (RectTransform)back.transform;
            backRect.SetParent(parent, worldPositionStays: false);
            backRect.anchorMin = new Vector2(0.5f, 0f);
            backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = Vector2.zero;
            backRect.sizeDelta = new Vector2(width, height);
            back.GetComponent<Image>().color = BarBackColor;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fill.transform;
            fillRect.SetParent(backRect, worldPositionStays: false);
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.color = fillColor;
            return image;
        }

        /// <summary>创建一个文本标签。</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string content,
            Vector2 anchor,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            TextAnchor alignment)
        {
            var host = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(fontSize);
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = alignment;
            text.color = TextColor;
            text.raycastTarget = false;
            return text;
        }
    }
}
