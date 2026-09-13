using RaidDemo.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包里一件物品的视觉表现。
    /// </summary>
    /// <remarks>
    /// <para>它只负责"把一件物品画成多大、什么颜色、写什么字"，
    /// 不持有任何规则、也不处理任何输入。拖拽由 <see cref="InventoryScreenController"/>
    /// 统一处理——因为拖拽是一个跨格子的状态机，让每个物品各自处理会立刻出现
    /// "谁负责画预览"这种责任不清的问题。</para>
    ///
    /// <para><b>配色取自稀有度，但用的是"浅底变体"</b>（M7 批次 4）：
    /// 物品格现在是奶油纸面上的一块浅色卡片，填充色由稀有度色向白色稀释 78%，
    /// 描边直接用稀有度色。深色文字压在浅填充上，五个档位一眼能分开，
    /// 而深色背景上用的原色仍然留给世界里的伤害数字与标记。</para>
    /// </remarks>
    public sealed class ItemCellView : MonoBehaviour
    {
        /// <summary>内边距（像素）。留出缝隙，相邻物品才不会看起来连成一块。</summary>
        private const float Padding = 2f;

        /// <summary>压暗系数：批量出售模式下未选中的物品用它变暗。</summary>
        private const float DimFactor = 0.55f;

        private RectTransform m_Rect;
        private Image m_Outline;
        private Image m_Fill;
        private TextMeshProUGUI m_Label;
        private Image m_Badge;
        private Color m_RarityOutline;
        private Color m_RarityFill;
        private bool m_Selected;
        private bool m_Dimmed;

        /// <summary>本视图对应的物品实例。</summary>
        public ItemInstance Item { get; private set; }

        /// <summary>本视图占据的矩形。</summary>
        public RectTransform Rect
        {
            get { return m_Rect; }
        }

        /// <summary>
        /// 构建视图。
        /// </summary>
        /// <param name="item">要显示的物品。</param>
        /// <param name="cellSize">单个格子的边长（像素）。</param>
        /// <param name="sizeInCells">物品占用的格子尺寸。</param>
        /// <param name="topLeft">物品左上角在父节点中的位置（像素，Y 轴向下）。</param>
        public void Build(ItemInstance item, float cellSize, GridSize sizeInCells, Vector2 topLeft)
        {
            Item = item;

            gameObject.name = $"Item_{item.Definition.Id}_{item.InstanceId}";
            m_Rect = gameObject.GetComponent<RectTransform>();
            if (m_Rect == null)
            {
                m_Rect = gameObject.AddComponent<RectTransform>();
            }

            m_Rect.anchorMin = new Vector2(0f, 1f);
            m_Rect.anchorMax = new Vector2(0f, 1f);
            m_Rect.pivot = new Vector2(0f, 1f);
            m_Rect.anchoredPosition = new Vector2(topLeft.x + Padding, -topLeft.y - Padding);
            m_Rect.sizeDelta = new Vector2(
                (sizeInCells.Width * cellSize) - (Padding * 2f),
                (sizeInCells.Height * cellSize) - (Padding * 2f));

            m_RarityOutline = UiPalette.ItemOutline(item.Definition.Rarity);
            m_RarityFill = UiPalette.ItemFill(item.Definition.Rarity);

            m_Fill = EnsureImage("Fill", m_Rect, UiSprites.Block, m_RarityFill);
            UiFactory.Stretch(m_Fill.rectTransform);
            m_Fill.rectTransform.offsetMin = new Vector2(UiPalette.OutlineWidth, UiPalette.OutlineWidth);
            m_Fill.rectTransform.offsetMax = new Vector2(-UiPalette.OutlineWidth, -UiPalette.OutlineWidth);

            m_Outline = EnsureImage("Outline", m_Rect, UiSprites.RingWhite, m_RarityOutline);
            UiFactory.Stretch(m_Outline.rectTransform);

            m_Label = EnsureLabel(m_Rect);
            m_Badge = EnsureBadge(m_Rect);
            EnsureCaliberBadge(item.Definition);
            RefreshVisualState();
        }

        /// <summary>
        /// 给武器与弹药格加一枚口径徽标。
        /// </summary>
        /// <param name="definition">物品定义。</param>
        /// <remarks>
        /// <para>放在左上角：右上角被"选中对勾"占用，底部是物品名。
        /// 徽标按口径固定着色（见 <see cref="RaidDemo.Data.CaliberPalette"/>），
        /// 于是"橙色的枪配橙色的子弹"不需要读文字就能对上。</para>
        /// <para>只有武器与弹药有口径；其它物品不加，避免每格都挂一个"未知口径"。</para>
        /// </remarks>
        private void EnsureCaliberBadge(RaidDemo.Data.IItemDefinition definition)
        {
            var caliber = RaidDemo.Data.CaliberPalette.ResolveCaliber(definition);
            if (string.IsNullOrEmpty(caliber) || m_Rect.Find("CaliberBadge") != null)
            {
                return;
            }

            CaliberBadge.CreateAnchored(
                m_Rect,
                caliber,
                anchor: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                offset: new Vector2(3f, -3f),
                size: new Vector2(40f, 15f));
        }

        /// <summary>
        /// 设置选中状态。
        /// </summary>
        /// <remarks>选中不替换稀有度填充：描边换成主题的青绿、右上角加一个角标，
        /// 这两个信号与品质颜色无关，因此在任何稀有度上都读得出来。</remarks>
        public void SetSelected(bool selected)
        {
            m_Selected = selected;
            RefreshVisualState();
        }

        /// <summary>设置压暗状态。批量出售模式下未选中的物品会被压暗。</summary>
        public void SetDimmed(bool dimmed)
        {
            m_Dimmed = dimmed;
            RefreshVisualState();
        }

        /// <summary>按选中与压暗状态重画描边、填充与角标。</summary>
        private void RefreshVisualState()
        {
            if (m_Outline == null || m_Fill == null || m_Label == null)
            {
                return;
            }

            var outlineColor = m_RarityOutline;
            var fillColor = m_RarityFill;
            var labelColor = UiPalette.Ink;

            if (m_Dimmed && !m_Selected)
            {
                outlineColor = Dim(outlineColor, DimFactor);
                fillColor = Dim(fillColor, DimFactor);
                labelColor = UiPalette.InkDisabled;
            }

            if (m_Selected)
            {
                outlineColor = UiPalette.Teal;
                fillColor = m_RarityFill;
                labelColor = UiPalette.Ink;
            }

            m_Outline.color = outlineColor;
            m_Fill.color = fillColor;
            m_Label.color = labelColor;
            if (m_Badge != null)
            {
                m_Badge.gameObject.SetActive(m_Selected);
            }
        }

        /// <summary>按比例压暗颜色，保留原来的透明度。</summary>
        private static Color Dim(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }

        /// <summary>刷新数量与名称文本。</summary>
        public void RefreshText()
        {
            if (m_Label == null || Item == null)
            {
                return;
            }

            var name = Item.Definition.DisplayName;
            m_Label.text = Item.StackCount > 1 ? $"{name} x{Item.StackCount}" : name;
        }

        /// <summary>确保一个九宫格 Image 存在。</summary>
        private static Image EnsureImage(string name, RectTransform parent, Sprite sprite, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            var image = host.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>确保物品名文本存在（压在格子底部，最多两行）。</summary>
        private static TextMeshProUGUI EnsureLabel(RectTransform parent)
        {
            var host = new GameObject("Label", typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            UiFactory.Stretch(rect);
            rect.offsetMin = new Vector2(4f, 3f);
            rect.offsetMax = new Vector2(-4f, -3f);

            var text = host.AddComponent<TextMeshProUGUI>();
            var font = TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = 13f;
            text.alignment = TextAlignmentOptions.Bottom;
            text.color = UiPalette.Ink;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        /// <summary>创建右上角的选中角标（青绿圆片 + 白勾）。</summary>
        private static Image EnsureBadge(RectTransform parent)
        {
            var host = new GameObject("SelectedBadge", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-3f, -3f);
            rect.sizeDelta = new Vector2(22f, 22f);

            var badge = host.GetComponent<Image>();
            // 实心圆角块 + 白勾：22 像素的尺寸下，空心环里的对勾会看不清。
            badge.sprite = UiSprites.Block;
            badge.type = Image.Type.Sliced;
            badge.pixelsPerUnitMultiplier = 1f;
            badge.color = UiPalette.Teal;
            badge.raycastTarget = false;

            var labelHost = new GameObject("Mark", typeof(RectTransform));
            var labelRect = (RectTransform)labelHost.transform;
            labelRect.SetParent(rect, worldPositionStays: false);
            UiFactory.Stretch(labelRect);

            var mark = labelHost.AddComponent<TextMeshProUGUI>();
            var font = TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                mark.font = font;
            }

            mark.text = "✓";
            mark.fontSize = 15f;
            mark.alignment = TextAlignmentOptions.Center;
            mark.color = Color.white;
            mark.raycastTarget = false;

            host.SetActive(false);
            return badge;
        }
    }
}
