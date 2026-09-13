using TMPro;
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
        private static readonly Color HealColor = UiPalette.Ok;

        /// <summary>撤离读条宽度（像素）。</summary>
        private const float ExtractionBarWidth = 360f;

        /// <summary>剩余时间低于该值时倒计时转为警示色（秒）。</summary>
        private const float UrgentThresholdSeconds = 60f;

        /// <summary>
        /// 战局 HUD 压在世界上，用小样里的"白色胶囊 + 深墨文字"。
        /// </summary>
        /// <remarks>顶部倒计时与底部读条都要在任何背景（亮草地、暗厂房）上读清，
        /// 因此每一项都自带一块有描边的底板，而不是靠文字描边硬撑。</remarks>
        private static readonly Color TextColor = UiPalette.Ink;
        private static readonly Color DimColor = UiPalette.InkSoft;
        private static readonly Color UrgentColor = UiPalette.Bad;
        private static readonly Color SearchFillColor = UiPalette.Warn;
        private static readonly Color ExtractionFillColor = UiPalette.Ok;

        private TextMeshProUGUI m_TimerLabel;
        private TextMeshProUGUI m_KillsLabel;
        private TextMeshProUGUI m_QuestLabel;
        private GameObject m_QuestPlate;
        private TextMeshProUGUI m_PromptLabel;
        private GameObject m_PromptPlate;
        private GameObject m_SearchRoot;
        private TextMeshProUGUI m_SearchLabel;
        private Image m_SearchFill;
        private GameObject m_ExtractionRoot;
        private TextMeshProUGUI m_ExtractionLabel;
        private Image m_ExtractionFill;
        private GameObject m_UseRoot;
        private TextMeshProUGUI m_UseLabel;
        private Image m_UseFill;
        private TextMeshProUGUI m_HealLabel;
        private GameObject m_HealPlate;
        private float m_HealRemaining;

        /// <summary>构建界面。由装配层在战局开始时调用一次。</summary>
        public void Initialize()
        {
            // 位于准星（100）之上、背包界面（200）之下：搜刮读条要盖住准星，
            // 但背包一旦打开就必须压住战局信息，否则拖拽物品时会被倒计时挡住视线。
            // 取 140 而不是与战斗界面相同的 150：两者相同时渲染顺序取决于对象创建顺序，
            // 而创建顺序会随装配流程变化，那种不稳定迟早会表现为「某次运行后弹药被倒计时挡住」。
            var canvas = UiFactory.CreateCanvas(transform, "RaidHudCanvas", 140);
            var rootRect = canvas;

            // 倒计时：顶中一块底板 + 大号等宽数字。等宽数字让秒数变化时整行不左右跳动。
            var timerPlate = UiFactory.CreateAnchored(
                rootRect, "TimerPlate", UiSprites.Chip,
                anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                offset: new Vector2(0f, -20f), size: new Vector2(200f, 58f));
            m_TimerLabel = UiFactory.CreateAnchoredLabel(
                timerPlate, "00:00",
                anchor: new Vector2(0.5f, 0.5f), pivot: new Vector2(0.5f, 0.5f),
                offset: Vector2.zero, size: new Vector2(180f, 48f),
                fontSize: 34f, alignment: TextAlignmentOptions.Center, color: TextColor);

            var killsPlate = UiFactory.CreateAnchored(
                rootRect, "KillsPlate", UiSprites.Chip,
                anchor: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f),
                offset: new Vector2(-28f, -20f), size: new Vector2(170f, 44f));
            m_KillsLabel = UiFactory.CreateAnchoredLabel(
                killsPlate, "击杀 0",
                anchor: new Vector2(0.5f, 0.5f), pivot: new Vector2(0.5f, 0.5f),
                offset: Vector2.zero, size: new Vector2(150f, 34f),
                fontSize: 20f, alignment: TextAlignmentOptions.Center, color: TextColor);

            // 任务追踪放在左上角：不与顶部倒计时、右侧击杀数争夺视线焦点。
            var questPlate = UiFactory.CreateAnchored(
                rootRect, "QuestPlate", UiSprites.Chip,
                anchor: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                offset: new Vector2(28f, -20f), size: new Vector2(560f, 44f));
            m_QuestLabel = UiFactory.CreateAnchoredLabel(
                questPlate, string.Empty,
                anchor: new Vector2(0.5f, 0.5f), pivot: new Vector2(0.5f, 0.5f),
                offset: Vector2.zero, size: new Vector2(536f, 34f),
                fontSize: 16f, alignment: TextAlignmentOptions.Left, color: new Color(0.88f, 0.92f, 0.62f));
            m_QuestPlate = questPlate.gameObject;
            questPlate.gameObject.SetActive(false);

            BuildExtraction(rootRect);
            BuildPrompt(rootRect);
            BuildSearch(rootRect);
            BuildUse(rootRect);
            BuildHeal(rootRect);
        }

        /// <summary>写入任务追踪文本；传 null 或空串时隐藏。</summary>
        public void SetQuestTracker(string text)
        {
            if (m_QuestLabel == null)
            {
                return;
            }

            var has = !string.IsNullOrEmpty(text);
            if (m_QuestPlate != null)
            {
                m_QuestPlate.SetActive(has);
            }

            if (has)
            {
                m_QuestLabel.text = text;
            }
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
            if (m_HealPlate != null)
            {
                m_HealPlate.SetActive(false);
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
            if (m_HealPlate != null)
            {
                m_HealPlate.SetActive(true);
            }

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
            if (m_PromptPlate != null)
            {
                m_PromptPlate.SetActive(hasPrompt);
            }

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
            var plate = UiFactory.CreateAnchored(
                parent, "ExtractionPanel", UiSprites.Chip,
                anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                offset: new Vector2(0f, -88f), size: new Vector2(ExtractionBarWidth + 40f, 74f));
            m_ExtractionRoot = plate.gameObject;

            m_ExtractionLabel = UiFactory.CreateAnchoredLabel(
                plate, "正在撤离",
                anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                offset: new Vector2(0f, -8f), size: new Vector2(ExtractionBarWidth, 28f),
                fontSize: 19f, alignment: TextAlignmentOptions.Center, color: TextColor);

            m_ExtractionFill = CreateBar(plate, ExtractionBarWidth, 14f, ExtractionFillColor);
            m_ExtractionRoot.SetActive(false);
        }

        /// <summary>创建底部的交互提示。</summary>
        private void BuildPrompt(RectTransform parent)
        {
            var plate = UiFactory.CreateAnchored(
                parent, "PromptPanel", UiSprites.Chip,
                anchor: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                offset: new Vector2(0f, 236f), size: new Vector2(620f, 44f));

            m_PromptLabel = UiFactory.CreateAnchoredLabel(
                plate, string.Empty,
                anchor: new Vector2(0.5f, 0.5f), pivot: new Vector2(0.5f, 0.5f),
                offset: Vector2.zero, size: new Vector2(596f, 36f),
                fontSize: 20f, alignment: TextAlignmentOptions.Center, color: TextColor);
            plate.gameObject.SetActive(false);
            m_PromptPlate = plate.gameObject;
        }

        /// <summary>创建医疗品使用读条（在搜刮读条上方一排，避免两者同时出现时重叠）。</summary>
        private void BuildUse(RectTransform parent)
        {
            var plate = UiFactory.CreateAnchored(
                parent, "UsePanel", UiSprites.Chip,
                anchor: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                offset: new Vector2(0f, 164f), size: new Vector2(UseBarWidth + 40f, 68f));
            m_UseRoot = plate.gameObject;

            m_UseLabel = UiFactory.CreateAnchoredLabel(
                plate, "使用中…",
                anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                offset: new Vector2(0f, -8f), size: new Vector2(UseBarWidth, 26f),
                fontSize: 18f, alignment: TextAlignmentOptions.Center, color: TextColor);

            m_UseFill = CreateBar(plate, UseBarWidth, 12f, HealColor);
            m_UseRoot.SetActive(false);
        }

        /// <summary>创建回血提示（显示在生命条上方，右侧留白处）。</summary>
        private void BuildHeal(RectTransform parent)
        {
            var plate = UiFactory.CreateAnchored(
                parent, "HealPlate", UiSprites.Chip,
                anchor: new Vector2(0f, 0f), pivot: new Vector2(0f, 0f),
                offset: new Vector2(56f, 244f), size: new Vector2(200f, 40f));

            m_HealLabel = UiFactory.CreateAnchoredLabel(
                plate, string.Empty,
                anchor: new Vector2(0.5f, 0.5f), pivot: new Vector2(0.5f, 0.5f),
                offset: Vector2.zero, size: new Vector2(180f, 32f),
                fontSize: 19f, alignment: TextAlignmentOptions.Center, color: HealColor);
            m_HealPlate = plate.gameObject;
            plate.gameObject.SetActive(false);
        }

        /// <summary>创建搜刮读条。</summary>
        private void BuildSearch(RectTransform parent)
        {
            var plate = UiFactory.CreateAnchored(
                parent, "SearchPanel", UiSprites.Chip,
                anchor: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                offset: new Vector2(0f, 92f), size: new Vector2(SearchBarWidth + 40f, 68f));
            m_SearchRoot = plate.gameObject;

            m_SearchLabel = UiFactory.CreateAnchoredLabel(
                plate, "搜刮中…",
                anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                offset: new Vector2(0f, -8f), size: new Vector2(SearchBarWidth, 26f),
                fontSize: 18f, alignment: TextAlignmentOptions.Center, color: TextColor);

            m_SearchFill = CreateBar(plate, SearchBarWidth, 12f, SearchFillColor);
            m_SearchRoot.SetActive(false);
        }

        /// <summary>
        /// 在底板内部创建一个进度条，返回填充图像。
        /// </summary>
        /// <param name="parent">底板（已按内容高度排好版）。</param>
        /// <param name="width">进度条宽度。</param>
        /// <param name="height">进度条高度。</param>
        /// <param name="fillColor">填充颜色。</param>
        /// <remarks>位置固定为"距底板顶部 44 像素、左右各留 20"：
        /// 三个读条的底板布局一致（上方一行标题、下方一条进度），因此不必逐个传坐标。</remarks>
        private static Image CreateBar(RectTransform parent, float width, float height, Color fillColor)
        {
            UiFactory.CreateBar(parent, new Vector2(20f, 44f), new Vector2(width, height), fillColor, out var fill);
            return fill;
        }
    }
}
