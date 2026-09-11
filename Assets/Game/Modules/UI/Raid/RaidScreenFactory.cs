using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 战局外界面（主菜单、结算）共用的构建工具。
    /// </summary>
    /// <remarks>
    /// <para>主菜单与结算面板都由同样的三件事组成：全屏遮罩、文字、按钮。
    /// 与其在两个文件里各写一遍画布缩放与按钮命中，不如集中在这里。
    /// 否则「参考分辨率」这类全局参数迟早会在两处漂移。</para>
    ///
    /// <para>不使用 Unity 的 Button 与 EventSystem：本项目至今没有事件系统，
    /// 全部交互靠轮询鼠标位置（背包拖拽也是如此）。为两个按钮引入事件系统
    /// 会新增一套输入通路，而两套输入通路最容易出现「按钮点不动」这类问题。</para>
    /// </remarks>
    internal static class RaidScreenFactory
    {
        /// <summary>界面参考分辨率宽度。</summary>
        public const float ReferenceWidth = 1920f;

        /// <summary>界面参考分辨率高度。</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>创建一个全屏叠加画布，返回其根节点。</summary>
        public static RectTransform CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var host = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            host.transform.SetParent(parent, worldPositionStays: false);

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            return (RectTransform)host.transform;
        }

        /// <summary>创建一个居中的面板。</summary>
        public static RectTransform CreatePanel(
            RectTransform parent,
            string name,
            Vector2 size,
            Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            var image = host.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>创建一个左上对齐的文本标签（Y 轴向下为正）。</summary>
        public static Text CreateLabel(
            RectTransform parent,
            string content,
            Vector2 topLeft,
            Vector2 size,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            var host = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(fontSize);
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>创建一个简陋但可用的按钮（底色 + 居中文字）。</summary>
        public static RaidButtonWidget CreateButton(
            RectTransform parent,
            string caption,
            Vector2 topLeft,
            Vector2 size,
            Color normalColor,
            Color hoverColor)
        {
            var host = new GameObject("Button", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var background = host.GetComponent<Image>();
            background.color = normalColor;
            background.raycastTarget = false;

            var label = CreateLabel(
                rect,
                caption,
                Vector2.zero,
                size,
                22,
                TextAnchor.MiddleCenter,
                new Color(0.96f, 0.96f, 0.98f));

            return new RaidButtonWidget(rect, background, label, normalColor, hoverColor);
        }
    }

    /// <summary>
    /// 简易按钮的运行时状态。
    /// </summary>
    /// <remarks>
    /// 不是 MonoBehaviour：它只是把「矩形、底色、文字」捆在一起，
    /// 由所属界面每帧查询鼠标位置并决定是否触发。
    /// 按钮生命周期完全由界面掌控，界面隐藏时按钮一起隐藏，不会留下悬挂引用。
    /// </remarks>
    internal sealed class RaidButtonWidget
    {
        private readonly Color m_NormalColor;
        private readonly Color m_HoverColor;

        /// <summary>创建按钮状态。</summary>
        public RaidButtonWidget(
            RectTransform rect,
            Image background,
            Text label,
            Color normalColor,
            Color hoverColor)
        {
            Rect = rect;
            Background = background;
            Label = label;
            m_NormalColor = normalColor;
            m_HoverColor = hoverColor;
        }

        /// <summary>按钮矩形。</summary>
        public RectTransform Rect { get; }

        /// <summary>按钮底色。</summary>
        public Image Background { get; }

        /// <summary>按钮文字。</summary>
        public Text Label { get; }

        /// <summary>鼠标位置是否落在按钮上。</summary>
        /// <remarks>画布是 ScreenSpaceOverlay，因此相机参数传 null。</remarks>
        public bool Contains(Vector2 screenPoint)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, null);
        }

        /// <summary>按悬停状态刷新底色。</summary>
        public void SetHovered(bool hovered)
        {
            Background.color = hovered ? m_HoverColor : m_NormalColor;
        }
    }
}
