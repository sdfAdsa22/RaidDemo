using TMPro;
using RaidDemo.Kernel;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// M7 批次 4 的界面构建工具：画布、面板、文字、按钮、进度条。
    /// </summary>
    /// <remarks>
    /// <para><b>与既有的 <c>RaidScreenFactory</c> 是什么关系：</b>那一套是 M5 的灰盒工具
    /// （纯色方板 + 旧版 Text），它服务的界面会逐个迁移到本类；迁移完成后
    /// <c>RaidScreenFactory</c> 一并删除。两套并存期间**不要互相调用**——
    /// 混用会让"这个面板用的是哪套配色"变得说不清。</para>
    ///
    /// <para><b>布局约定</b>与旧工具保持一致：锚点取左上角、Y 轴向下为正、
    /// 尺寸按 1920×1080 的参考分辨率给出。这样迁移是"换构建函数"而不是"重算坐标"。</para>
    /// </remarks>
    internal static class UiFactory
    {
        /// <summary>界面参考分辨率宽度。</summary>
        public const float ReferenceWidth = 1920f;

        /// <summary>界面参考分辨率高度。</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>
        /// 界面层的日志出口。
        /// </summary>
        /// <remarks>界面构建发生在运行时（没有装配层传入的服务定位器），
        /// 因此这里就地持有一个统一日志服务的实例——用的是同一个 LogService，
        /// 级别与格式与工程其余部分一致，而不是各写各的 Debug.Log。</remarks>
        private static readonly LogService s_Log = new LogService(LogLevel.Warning);

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
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)host.transform;
        }

        /// <summary>
        /// 铺满整屏的实色层。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">节点名。</param>
        /// <param name="color">颜色。</param>
        /// <remarks>主菜单用它做**不透明独立背景**；其它界面用 <see cref="CreateVeil"/> 压暗世界。</remarks>
        public static Image CreateBackdrop(RectTransform parent, string name, Color color)
        {
            var rect = CreateRect(parent, name);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>半透明遮罩：面板打开时压暗背后的世界。</summary>
        public static Image CreateVeil(RectTransform parent, string name)
        {
            return CreateBackdrop(parent, name, UiPalette.Veil);
        }

        /// <summary>按九宫格贴图创建一个可拉伸的面板。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">节点名。</param>
        /// <param name="size">尺寸（参考像素）。</param>
        /// <param name="sprite">九宫格贴图。</param>
        /// <param name="anchoredPosition">相对左上角的偏移，Y 向下为正。</param>
        public static RectTransform CreatePanel(
            RectTransform parent,
            string name,
            Vector2 size,
            Sprite sprite,
            Vector2 anchoredPosition)
        {
            var rect = CreateRect(parent, name);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(anchoredPosition.x, -anchoredPosition.y);
            rect.sizeDelta = size;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>居中面板（主菜单、结算这类"一屏一件事"的界面用）。</summary>
        public static RectTransform CreateCenteredPanel(RectTransform parent, string name, Vector2 size, Sprite sprite)
        {
            var rect = CreateRect(parent, name);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>
        /// 创建一个左上对齐的文本标签（Y 轴向下为正）。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="content">文字内容。</param>
        /// <param name="topLeft">相对父节点的左上偏移。</param>
        /// <param name="size">文本框尺寸。</param>
        /// <param name="fontSize">字号（参考像素）。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <param name="color">文字颜色。</param>
        /// <param name="wrap">是否允许换行。默认不换行（界面文字多是单行标签）。</param>
        public static TextMeshProUGUI CreateLabel(
            RectTransform parent,
            string content,
            Vector2 topLeft,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color,
            bool wrap = false)
        {
            var rect = CreateRect(parent, "Label");
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyDefaultFont(text);
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            return text;
        }

        /// <summary>创建一个按钮，返回其状态对象。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="caption">按钮文字。</param>
        /// <param name="topLeft">相对父节点的左上偏移。</param>
        /// <param name="size">按钮尺寸。</param>
        /// <param name="kind">视觉等级。</param>
        public static UiButton CreateButton(
            RectTransform parent,
            string caption,
            Vector2 topLeft,
            Vector2 size,
            UiButtonKind kind = UiButtonKind.Normal)
        {
            var isPrimary = kind == UiButtonKind.Primary;
            var rect = CreatePanel(parent, "Button", size, isPrimary ? UiSprites.ButtonPrimary : UiSprites.Button, topLeft);
            var background = rect.GetComponent<Image>();

            var labelColor = isPrimary ? Color.white : UiPalette.Ink;
            var label = CreateLabel(
                rect,
                caption,
                Vector2.zero,
                size,
                UiPalette.BodySize,
                TextAlignmentOptions.Center,
                labelColor);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = Vector2.zero;

            return new UiButton(
                rect,
                background,
                label,
                isPrimary ? UiSprites.ButtonPrimary : UiSprites.Button,
                isPrimary ? UiSprites.ButtonPrimaryPressed : UiSprites.ButtonPressed,
                labelColor);
        }

        /// <summary>创建一个进度条，返回填充部分（调用方按比例设置其宽度）。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="topLeft">相对父节点的左上偏移。</param>
        /// <param name="size">整体尺寸。</param>
        /// <param name="fillColor">填充颜色。</param>
        /// <param name="fill">填充部分的矩形，供调用方调整宽度。</param>
        public static RectTransform CreateBar(
            RectTransform parent,
            Vector2 topLeft,
            Vector2 size,
            Color fillColor,
            out Image fill)
        {
            var track = CreatePanel(parent, "Bar", size, UiSprites.Track, topLeft);

            var fillRect = CreateRect(track, "Fill");
            Stretch(fillRect);
            fillRect.offsetMin = new Vector2(UiPalette.OutlineWidth, UiPalette.OutlineWidth);
            fillRect.offsetMax = new Vector2(-UiPalette.OutlineWidth, -UiPalette.OutlineWidth);
            fillRect.anchorMax = new Vector2(0f, 1f);

            fill = fillRect.gameObject.AddComponent<Image>();
            fill.sprite = UiSprites.Fill;
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = 1f;
            fill.color = fillColor;
            fill.raycastTarget = false;
            return track;
        }

        /// <summary>创建一个空节点（供摆放子元素）。</summary>
        public static RectTransform CreateRect(RectTransform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>把矩形拉伸到铺满父节点。</summary>
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// 给文字指定项目默认字体。
        /// </summary>
        /// <remarks>
        /// <para>运行时创建的 TextMeshProUGUI 会去读 TMP 的项目默认字体；显式再赋一次是为了
        /// 让"字体没配好"这件事在**布局阶段**就暴露（拿到 null 时打印一次警告），
        /// 而不是等到玩家看到一片空白才发现。</para>
        /// <para>字体资产由菜单 <c>RaidDemo/UI/重建中文字体资产</c> 生成（Noto Sans SC，动态模式）。</para>
        /// </remarks>
        private static void ApplyDefaultFont(TextMeshProUGUI text)
        {
            var font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                s_Log.Warning("TMP 缺少默认字体，界面文字会显示为空白。" +
                              "请执行菜单 RaidDemo/UI/重建中文字体资产。");
                return;
            }

            text.font = font;
        }
    }
}
