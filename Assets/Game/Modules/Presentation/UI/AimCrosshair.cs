using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 屏幕空间的瞄准准星。
    /// </summary>
    /// <remarks>
    /// <para>准星位置由外部每帧写入（来自瞄准世界点反投影回屏幕），
    /// 本组件只负责把它画在屏幕上，不参与任何瞄准计算。
    /// 这样准星与角色朝向可以共用同一个世界坐标，从而天然对齐。</para>
    ///
    /// <para>使用 uGUI 而非 OnGUI 绘制，原因有两点：一是 uGUI 是运行时 UI 的标准做法，
    /// 能被 Unity 的渲染与截图流程正确处理；二是 OnGUI 每帧触发多次且无法参与批处理，
    /// 在高帧率下开销明显。</para>
    ///
    /// <para>使用屏幕空间叠加画布，准星不会被场景几何遮挡，尺寸也不受透视影响。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AimCrosshair : MonoBehaviour
    {
        /// <summary>准星线条长度（像素）。按 1080p 以上分辨率可辨识的尺寸取值。</summary>
        [SerializeField] private float m_ArmLength = 16f;

        /// <summary>准星线条粗细（像素）。</summary>
        [SerializeField] private float m_Thickness = 3f;

        /// <summary>中心空隙半径（像素）。留出空隙便于看清瞄准点本身。</summary>
        [SerializeField] private float m_GapRadius = 7f;

        /// <summary>准星颜色。</summary>
        [SerializeField] private Color m_Color = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>线宽（含描边）。描边保证准星在浅色背景上依然可见。</summary>
        [SerializeField] private float m_OutlineThickness = 6f;

        /// <summary>描边颜色。</summary>
        [SerializeField] private Color m_OutlineColor = new Color(0f, 0f, 0f, 0.55f);

        private RectTransform m_Root;
        private RectTransform m_Left;
        private RectTransform m_Right;
        private RectTransform m_Top;
        private RectTransform m_Bottom;

        private Canvas m_Canvas;
        private bool m_IsVisible;

        /// <summary>设置准星位置。坐标以屏幕像素为单位，原点在左下角。</summary>
        public void SetScreenPosition(Vector2 screenPosition)
        {
            EnsureCreated();
            if (m_Root == null)
            {
                return;
            }

            m_Root.anchoredPosition = screenPosition;
            SetVisible(true);
        }

        /// <summary>隐藏准星。</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        private void Awake()
        {
            EnsureCreated();
        }

        private void SetVisible(bool visible)
        {
            if (m_IsVisible == visible || m_Canvas == null)
            {
                return;
            }

            m_IsVisible = visible;
            m_Canvas.enabled = visible;
        }

        /// <summary>
        /// 创建画布与四条准星线。只执行一次。
        /// </summary>
        /// <remarks>
        /// 全部在运行时创建而非依赖预制体，原因是准星是纯粹的程序化图形，
        /// 没有需要美术调整的序列化状态；放在代码里可以让场景文件保持干净，
        /// 也避免预制体与代码之间的引用维护成本。
        /// </remarks>
        private void EnsureCreated()
        {
            if (m_Canvas != null)
            {
                return;
            }

            var canvasObject = new GameObject("AimCrosshairCanvas");
            canvasObject.transform.SetParent(transform, worldPositionStays: false);

            m_Canvas = canvasObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 排序值取得较高，确保准星绘制在 HUD 之上。
            m_Canvas.sortingOrder = 100;

            var rootObject = new GameObject("Root");
            rootObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
            m_Root = rootObject.AddComponent<RectTransform>();
            m_Root.anchorMin = Vector2.zero;
            m_Root.anchorMax = Vector2.zero;
            m_Root.pivot = new Vector2(0.5f, 0.5f);
            m_Root.sizeDelta = Vector2.zero;

            // 四条短线构成十字，中心留出空隙。
            m_Left = CreateBar("Left", new Vector2(-m_GapRadius - (m_ArmLength * 0.5f), 0f), new Vector2(m_ArmLength, m_Thickness));
            m_Right = CreateBar("Right", new Vector2(m_GapRadius + (m_ArmLength * 0.5f), 0f), new Vector2(m_ArmLength, m_Thickness));
            m_Top = CreateBar("Top", new Vector2(0f, m_GapRadius + (m_ArmLength * 0.5f)), new Vector2(m_Thickness, m_ArmLength));
            m_Bottom = CreateBar("Bottom", new Vector2(0f, -m_GapRadius - (m_ArmLength * 0.5f)), new Vector2(m_Thickness, m_ArmLength));

            m_IsVisible = true;
        }

        /// <summary>
        /// 创建一条准星线。
        /// </summary>
        /// <remarks>
        /// 每条线由内外两层图像组成：外层是深色描边，内层是亮色主体。
        /// 这样在浅色地面和深色地面上都能保持可见，而不必依赖阴影或后期效果。
        /// </remarks>
        private RectTransform CreateBar(string name, Vector2 position, Vector2 size)
        {
            var outline = new GameObject(name + "Outline");
            outline.transform.SetParent(m_Root, worldPositionStays: false);
            var outlineRect = outline.AddComponent<RectTransform>();
            outlineRect.anchoredPosition = position;
            outlineRect.sizeDelta = size + new Vector2(m_OutlineThickness - m_Thickness, m_OutlineThickness - m_Thickness);
            var outlineImage = outline.AddComponent<Image>();
            outlineImage.color = m_OutlineColor;
            outlineImage.raycastTarget = false;

            var bar = new GameObject(name);
            bar.transform.SetParent(outline.transform, worldPositionStays: false);
            var barRect = bar.AddComponent<RectTransform>();
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = size;
            var image = bar.AddComponent<Image>();
            image.color = m_Color;
            image.raycastTarget = false;

            return barRect;
        }
    }
}
