using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>按钮的视觉等级。</summary>
    internal enum UiButtonKind
    {
        /// <summary>次要操作：白底墨描边。</summary>
        Normal = 0,

        /// <summary>主要操作：青绿底，一屏最多一个。</summary>
        Primary = 1,
    }

    /// <summary>
    /// 简易按钮：矩形 + 九宫格底色 + 文字，由所属界面每帧轮询鼠标状态。
    /// </summary>
    /// <remarks>
    /// <para>与工程既有的 <c>RaidButtonWidget</c> 一样，它不是 MonoBehaviour，
    /// 也**不使用 Unity 的 Button 与 EventSystem**：整个项目至今没有事件系统，
    /// 背包拖拽与按钮点击都是轮询鼠标。为一个按钮引入第二套输入通路，
    /// 最容易产生的就是"按钮点不动"这类只有实机才能发现的问题。</para>
    ///
    /// <para>三种状态的表现刻意做成"厚片按钮"的物理感：悬停时整体抬起 2 像素并微微提亮，
    /// 按下时换成没有底部阴影的贴图、文字下沉 3 像素——这是卡通扁平风格里
    /// 成本最低、传达最直接的可点击反馈。</para>
    /// </remarks>
    internal sealed class UiButton
    {
        /// <summary>悬停时整体抬起的像素数。</summary>
        private const float HoverLift = 2f;

        /// <summary>按下时文字下沉的像素数。</summary>
        private const float PressedLabelDrop = 3f;

        private readonly Image m_Background;
        private readonly TextMeshProUGUI m_Label;
        private readonly Sprite m_NormalSprite;
        private readonly Sprite m_PressedSprite;
        private readonly Color m_LabelColor;
        private readonly Vector2 m_BasePosition;
        private readonly Vector2 m_LabelBasePosition;
        private bool m_Hovered;
        private bool m_Pressed;

        /// <summary>创建按钮状态。</summary>
        public UiButton(
            RectTransform rect,
            Image background,
            TextMeshProUGUI label,
            Sprite normalSprite,
            Sprite pressedSprite,
            Color labelColor)
        {
            Rect = rect;
            m_Background = background;
            m_Label = label;
            m_NormalSprite = normalSprite;
            m_PressedSprite = pressedSprite;
            m_LabelColor = labelColor;
            m_BasePosition = rect.anchoredPosition;
            m_LabelBasePosition = label.rectTransform.anchoredPosition;
        }

        /// <summary>按钮矩形。</summary>
        public RectTransform Rect { get; }

        /// <summary>按钮文字。</summary>
        public TextMeshProUGUI Label => m_Label;

        /// <summary>是否可交互。禁用时点击无效且文字变灰。</summary>
        public bool Interactable { get; set; } = true;

        /// <summary>鼠标位置是否落在按钮上。</summary>
        /// <remarks>画布是 ScreenSpaceOverlay，因此相机参数传 null。</remarks>
        public bool Contains(Vector2 screenPoint)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, null);
        }

        /// <summary>刷新悬停状态。</summary>
        public void SetHovered(bool hovered)
        {
            m_Hovered = hovered && Interactable;
        }

        /// <summary>
        /// 刷新按下状态并应用外观。
        /// </summary>
        /// <param name="pressed">鼠标左键是否正按在这个按钮上。</param>
        public void ApplyVisual(bool pressed)
        {
            m_Pressed = pressed && m_Hovered;

            Rect.anchoredPosition = m_BasePosition + new Vector2(0f, m_Hovered && !m_Pressed ? HoverLift : 0f);
            m_Background.sprite = m_Pressed ? m_PressedSprite : m_NormalSprite;
            m_Background.color = Interactable
                ? (m_Hovered && !m_Pressed ? new Color(0.99f, 0.98f, 0.95f) : Color.white)
                : new Color(0.82f, 0.80f, 0.76f);
            m_Label.color = Interactable ? m_LabelColor : UiPalette.InkDisabled;
            m_Label.rectTransform.anchoredPosition = m_LabelBasePosition +
                                                     new Vector2(0f, m_Pressed ? -PressedLabelDrop : 0f);
        }
    }
}
