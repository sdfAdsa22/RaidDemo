using RaidDemo.Data;
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
    /// <para>色块颜色来自 <see cref="RarityPalette"/>，这是稀有度对玩家最直接的作用：
    /// 一眼扫过去就知道哪件东西值钱。</para>
    /// </remarks>
    public sealed class ItemCellView : MonoBehaviour
    {
        /// <summary>物品主体色块的透明度。压暗一点，让网格背景透出来，避免界面糊成一片。</summary>
        private const float FillAlpha = 0.85f;

        /// <summary>描边透明度。</summary>
        private const float OutlineAlpha = 1f;

        /// <summary>内边距（像素）。留出缝隙，相邻物品才不会看起来连成一块。</summary>
        private const float Padding = 2f;

        private RectTransform m_Rect;
        private Image m_Outline;
        private Image m_InnerLine;
        private Image m_Fill;
        private Text m_Label;
        private Text m_CheckBadge;
        private Color m_BaseOutlineColor;
        private Color m_BaseFillColor;
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

            var color = RarityPalette.GetColor(item.Definition.Rarity);
            m_BaseOutlineColor = new Color(color.r, color.g, color.b, OutlineAlpha);
            m_BaseFillColor = new Color(
                color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, FillAlpha);

            m_Outline = EnsureImage("Outline", m_Rect, color);
            // 黑色内分隔线：选中时夹在白色外描边与品质填充之间，
            // 让白色描边在任何亮度的品质色上都能被看清。
            m_InnerLine = EnsureImage("InnerLine", m_Outline.rectTransform, Color.clear);
            StretchToParent(m_InnerLine.rectTransform, 1f);
            m_Fill = EnsureImage("Fill", m_Outline.rectTransform, m_BaseFillColor);
            StretchToParent(m_Fill.rectTransform, 2f);
            m_Outline.color = m_BaseOutlineColor;
            StretchToParent(m_Outline.rectTransform, 0f);

            m_Label = EnsureLabel("Label", m_Outline.rectTransform);
            m_CheckBadge = EnsureCheckBadge(m_Outline.rectTransform);
            RefreshVisualState();
        }

        /// <summary>
        /// 设置选中状态。
        /// </summary>
        /// <remarks>
        /// 选中不替换稀有度填充色：白色外描边、黑色内分隔线与右上角勾
        /// 提供与品质颜色无关的形状信号，避免和绿色 / 蓝色品质混淆。
        /// </remarks>
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

            var outlineColor = m_BaseOutlineColor;
            var fillColor = m_BaseFillColor;
            var labelColor = Color.white;

            // 未选中且处于出售模式：整体压暗，选中项保持原亮度形成对比。
            if (m_Dimmed && !m_Selected)
            {
                outlineColor = Dim(outlineColor, 0.55f);
                fillColor = Dim(fillColor, 0.55f);
                labelColor = new Color(0.62f, 0.62f, 0.66f, 0.85f);
            }

            if (m_Selected)
            {
                outlineColor = Color.white;
                fillColor = m_BaseFillColor;
                labelColor = Color.white;
            }

            m_Outline.color = outlineColor;
            m_Fill.color = fillColor;
            m_Label.color = labelColor;
            m_InnerLine.color = m_Selected ? Color.black : Color.clear;
            if (m_CheckBadge != null)
            {
                m_CheckBadge.gameObject.SetActive(m_Selected);
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

        /// <summary>确保一个矩形 Image 存在。</summary>
        private static Image EnsureImage(string name, RectTransform parent, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            var image = host.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>确保文本组件存在。</summary>
        private static Text EnsureLabel(string name, RectTransform parent)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(12);
            text.fontSize = 12;
            text.alignment = TextAnchor.LowerCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            StretchToParent(rect, 2f);
            return text;
        }

        /// <summary>创建右上角的选中勾。</summary>
        private static Text EnsureCheckBadge(RectTransform parent)
        {
            var host = new GameObject("SelectedBadge", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-1f, -1f);
            rect.sizeDelta = new Vector2(18f, 18f);

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(14);
            text.fontSize = 14;
            text.text = "✓";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;

            var outline = host.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            host.SetActive(false);
            return text;
        }

        /// <summary>把子节点拉满父节点，四周留出指定边距。</summary>
        private static void StretchToParent(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }
    }
}
