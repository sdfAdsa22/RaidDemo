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
        private Image m_Fill;
        private Text m_Label;
        private Color m_BaseOutlineColor;
        private Color m_BaseFillColor;
        private bool m_Selected;

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
            m_Fill = EnsureImage("Fill", m_Outline.rectTransform, m_BaseFillColor);
            StretchToParent(m_Fill.rectTransform, 2f);
            m_Outline.color = m_BaseOutlineColor;
            StretchToParent(m_Outline.rectTransform, 0f);

            m_Label = EnsureLabel("Label", m_Outline.rectTransform);
            RefreshSelectionVisual();
        }

        /// <summary>
        /// 设置选中高亮。
        /// </summary>
        /// <remarks>
        /// 批量出售模式下，玩家需要一眼看出"哪些已经选进来了"。
        /// 高亮只改变描边与填充色，不改变物品本身的数据。
        /// </remarks>
        public void SetSelected(bool selected)
        {
            m_Selected = selected;
            RefreshSelectionVisual();
        }

        private void RefreshSelectionVisual()
        {
            if (m_Outline == null || m_Fill == null)
            {
                return;
            }

            if (!m_Selected)
            {
                m_Outline.color = m_BaseOutlineColor;
                m_Fill.color = m_BaseFillColor;
                return;
            }

            var selection = new Color(0.35f, 0.95f, 0.45f, 1f);
            m_Outline.color = selection;
            m_Fill.color = new Color(selection.r * 0.55f, selection.g * 0.55f, selection.b * 0.55f, FillAlpha);
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
