using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 一个网格容器的界面视图。
    /// </summary>
    /// <remarks>
    /// <para><b>本视图是只读的。</b>它从 InventoryGrid 读数据画格子，不提供任何修改数据的方法。</para>
    /// <para>所有改动都通过命令走 CommandRouter，改完之后由 InventoryChangedEvent 触发重画。</para>
    /// <para>这条单向环路比"点一下直接改界面"麻烦，但它保证界面永远不会显示未真正生效的状态，
    /// 也让联机时客户端的界面能如实反映服务端返回的真实结果。</para>
    /// </remarks>
    public sealed class InventoryGridView : MonoBehaviour
    {
        /// <summary>单个格子的边长（像素）。56 是 1080p 下既能放下名称、又不占满屏幕的尺寸。</summary>
        public const float CellSize = 56f;

        /// <summary>格子之间的缝隙（像素）。留缝是为了让相邻物品能被看出是两件。</summary>
        private const float CellGap = 1f;

        /// <summary>标题区高度（像素）。</summary>
        private const float TitleHeight = 22f;

        /// <summary>预览高亮的透明度。</summary>
        private const float PreviewAlpha = 0.45f;

        private static readonly Color PanelColor = new Color(0.16f, 0.16f, 0.18f, 0.92f);
        private static readonly Color CellColor = new Color(0.24f, 0.24f, 0.27f, 1f);
        private static readonly Color ValidPreviewColor = new Color(0.30f, 0.85f, 0.40f, PreviewAlpha);
        private static readonly Color InvalidPreviewColor = new Color(0.95f, 0.30f, 0.30f, PreviewAlpha);

        private readonly List<ItemCellView> m_ItemViews = new List<ItemCellView>();
        private readonly List<Image> m_CellImages = new List<Image>();

        private InventoryGrid m_Grid;
        private RectTransform m_CellsRoot;

        /// <summary>本视图对应的容器网格。</summary>
        public InventoryGrid Grid
        {
            get { return m_Grid; }
        }

        /// <summary>本视图对应的容器运行时 ID。命令需要它来定位容器。</summary>
        public int ContainerId { get; private set; }

        /// <summary>当前显示的所有物品视图。</summary>
        public IReadOnlyList<ItemCellView> ItemViews
        {
            get { return m_ItemViews; }
        }

        /// <summary>
        /// 构建视图。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="grid">要显示的容器。</param>
        /// <param name="containerId">容器的运行时 ID。</param>
        /// <param name="title">标题文字。</param>
        /// <param name="topLeft">视图左上角在父节点中的位置（像素，Y 轴向下为正）。</param>
        public void Build(RectTransform parent, InventoryGrid grid, int containerId, string title, Vector2 topLeft)
        {
            m_Grid = grid;
            ContainerId = containerId;

            var width = grid.Width * CellSize;
            var height = (grid.Height * CellSize) + TitleHeight;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = gameObject.AddComponent<RectTransform>();
            }

            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = new Vector2(width, height);

            var background = gameObject.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;

            CreateLabel(rect, title, new Vector2(6f, 0f), width, TitleHeight);

            m_CellsRoot = CreateCellsRoot(rect, width, grid.Height * CellSize);
            BuildCells(grid);
        }

        /// <summary>
        /// 按当前容器数据重画全部物品。
        /// </summary>
        /// <remarks>
        /// <para>采用全部销毁重建而不是增量更新：一个容器的物品数量在几十件量级，重建开销可以忽略。</para>
        /// <para>增量更新需要处理增删、堆叠数量变化、旋转等一堆分支，是界面代码里最容易藏 bug 的地方。</para>
        /// </remarks>
        public void Refresh()
        {
            for (var i = 0; i < m_ItemViews.Count; i++)
            {
                if (m_ItemViews[i] != null)
                {
                    Destroy(m_ItemViews[i].gameObject);
                }
            }

            m_ItemViews.Clear();
            ClearPreview();

            if (m_Grid == null)
            {
                return;
            }

            var items = m_Grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!m_Grid.TryGetOrigin(item, out var origin))
                {
                    continue;
                }

                var host = new GameObject("Item");
                host.transform.SetParent(m_CellsRoot, worldPositionStays: false);
                var view = host.AddComponent<ItemCellView>();
                view.Build(
                    item,
                    CellSize,
                    item.OccupiedSize,
                    new Vector2(origin.X * CellSize, origin.Y * CellSize));
                view.RefreshText();
                m_ItemViews.Add(view);
            }
        }

        /// <summary>
        /// 把屏幕坐标换算成格子坐标。
        /// </summary>
        /// <param name="screenPoint">屏幕坐标。</param>
        /// <param name="cell">换算出的格子坐标。</param>
        /// <returns>点击位置落在本视图的格子区域内返回 true。</returns>
        /// <remarks>
        /// <para>这里不走 uGUI 的射线系统，而是直接用矩形做换算。</para>
        /// <para>原因是拖拽需要"指针落在哪一格"这种格子级信息，而射线系统只能告诉你是哪个对象被命中。</para>
        /// <para>直接换算既省掉一个 EventSystem 依赖，也让指针映射保持简单。</para>
        /// </remarks>
        public bool TryGetCellAt(Vector2 screenPoint, out GridPoint cell)
        {
            cell = default;
            if (m_CellsRoot == null || m_Grid == null)
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    m_CellsRoot, screenPoint, null, out var local))
            {
                return false;
            }

            // 本节点锚点与轴心都在左上角，因此局部坐标 x 向右为正、y 向下为负。
            var x = Mathf.FloorToInt(local.x / CellSize);
            var y = Mathf.FloorToInt(-local.y / CellSize);
            cell = new GridPoint(x, y);
            return m_Grid.IsInside(cell);
        }

        /// <summary>
        /// 在指定区域显示落点预览。
        /// </summary>
        /// <param name="origin">预览区域左上角。</param>
        /// <param name="size">预览区域尺寸。</param>
        /// <param name="valid">是否合法。合法显示绿色，非法显示红色。</param>
        public void ShowPreview(GridPoint origin, GridSize size, bool valid)
        {
            ClearPreview();
            if (m_Grid == null)
            {
                return;
            }

            var color = valid ? ValidPreviewColor : InvalidPreviewColor;
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    var cell = new GridPoint(origin.X + x, origin.Y + y);
                    if (!m_Grid.IsInside(cell))
                    {
                        continue;
                    }

                    m_CellImages[(cell.Y * m_Grid.Width) + cell.X].color = color;
                }
            }
        }

        /// <summary>清除落点预览，把格子恢复成底色。</summary>
        public void ClearPreview()
        {
            for (var i = 0; i < m_CellImages.Count; i++)
            {
                m_CellImages[i].color = CellColor;
            }
        }

        /// <summary>构建格子底板。落点预览直接复用这些底板变色，不额外建对象。</summary>
        private void BuildCells(InventoryGrid grid)
        {
            m_CellImages.Clear();
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var host = new GameObject($"Cell_{x}_{y}", typeof(RectTransform), typeof(Image));
                    var rect = (RectTransform)host.transform;
                    rect.SetParent(m_CellsRoot, worldPositionStays: false);
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(x * CellSize, -y * CellSize);
                    rect.sizeDelta = new Vector2(CellSize - CellGap, CellSize - CellGap);

                    var image = host.GetComponent<Image>();
                    image.color = CellColor;
                    image.raycastTarget = false;
                    m_CellImages.Add(image);
                }
            }
        }

        /// <summary>创建格子区域节点。</summary>
        private static RectTransform CreateCellsRoot(RectTransform parent, float width, float height)
        {
            var host = new GameObject("Cells", typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -TitleHeight);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>创建一个标题文本。</summary>
        private static void CreateLabel(RectTransform parent, string content, Vector2 topLeft, float width, float height)
        {
            var host = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = new Vector2(width, height);

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(14);
            text.fontSize = 14;
            text.text = content;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color(0.92f, 0.92f, 0.95f);
            text.raycastTarget = false;
        }
    }
}
