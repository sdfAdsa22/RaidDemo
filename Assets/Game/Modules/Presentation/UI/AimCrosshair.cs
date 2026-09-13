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
    /// <para><b>两种画法，优先用贴图：</b>M7 批次 4 起改用 Kenney Crosshair Pack 的贴图
    /// （Outline 变体自带深色描边，压在亮绿草地上仍然看得清）；拿不到贴图时退回程序化的四条短线。
    /// 兜底不是"开发期偷懒"，而是分发要求：别人克隆仓库时少了素材包，
    /// 准星也必须存在，因为**没有准星的射击游戏等于不能玩**。</para>
    /// <para>换弹时换成另一张造型（虚线圆环）而不是只改颜色：形状的差异在余光里也能分辨，
    /// 而颜色差异会被背景色干扰。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AimCrosshair : MonoBehaviour
    {
        /// <summary>贴图模式的显示边长（像素）。74 像素的原图按此尺寸显示。</summary>
        [SerializeField] private float m_SpriteSize = 48f;

        /// <summary>程序化准星的线条长度（像素）。</summary>
        [SerializeField] private float m_ArmLength = 16f;

        /// <summary>程序化准星的线条粗细（像素）。</summary>
        [SerializeField] private float m_Thickness = 3f;

        /// <summary>程序化准星的中心空隙半径（像素）。</summary>
        [SerializeField] private float m_GapRadius = 7f;

        /// <summary>程序化准星的主体色。</summary>
        [SerializeField] private Color m_Color = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>程序化准星换弹时的颜色（主题的青绿）。</summary>
        [SerializeField] private Color m_ReloadColor = new Color(0.18f, 0.66f, 0.63f, 0.95f);

        /// <summary>程序化准星的描边线宽。</summary>
        [SerializeField] private float m_OutlineThickness = 6f;

        /// <summary>程序化准星的描边颜色。</summary>
        [SerializeField] private Color m_OutlineColor = new Color(0f, 0f, 0f, 0.55f);

        private RectTransform m_Root;
        private Canvas m_Canvas;
        private Image m_SpriteImage;
        private Image[] m_BarFills;
        private Sprite m_NormalSprite;
        private Sprite m_ReloadSprite;
        private bool m_IsVisible;
        private bool m_Reloading;

        /// <summary>
        /// 绑定准星贴图。由启动层在装配表现层时调用。
        /// </summary>
        /// <param name="normal">常规准星贴图，可为 null。</param>
        /// <param name="reload">换弹中的准星贴图，可为 null。</param>
        public void Initialize(Sprite normal, Sprite reload)
        {
            m_NormalSprite = normal;
            m_ReloadSprite = reload;
            ApplySprite();
        }

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

        /// <summary>设置是否正在换弹。换弹时换成另一张造型。</summary>
        /// <param name="reloading">是否正在换弹。</param>
        public void SetReloading(bool reloading)
        {
            if (m_Reloading == reloading)
            {
                return;
            }

            m_Reloading = reloading;
            EnsureCreated();
            ApplySprite();
            ApplyBarColor();
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
        /// 创建画布与准星本体。只执行一次。
        /// </summary>
        /// <remarks>全部在运行时创建而非依赖预制体：准星是纯程序化图形，
        /// 没有需要美术调整的序列化状态，放在代码里可以让场景文件保持干净。</remarks>
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
            // 排序值低于 HUD（140/150）与背包（200）：准星是瞄准参考，不该盖住任何信息面板。
            m_Canvas.sortingOrder = 100;

            var rootObject = new GameObject("Root");
            rootObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
            m_Root = rootObject.AddComponent<RectTransform>();
            m_Root.anchorMin = Vector2.zero;
            m_Root.anchorMax = Vector2.zero;
            m_Root.pivot = new Vector2(0.5f, 0.5f);
            m_Root.sizeDelta = Vector2.zero;

            if (HasSprites())
            {
                BuildSprite();
            }
            else
            {
                BuildBars();
            }

            m_IsVisible = true;
        }

        /// <summary>是否拿到了贴图（两张都要有，否则换弹时没法切造型）。</summary>
        private bool HasSprites()
        {
            return m_NormalSprite != null && m_ReloadSprite != null;
        }

        /// <summary>贴图模式：一张 Image 就够。</summary>
        private void BuildSprite()
        {
            var host = new GameObject("Sprite", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(m_Root, worldPositionStays: false);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(m_SpriteSize, m_SpriteSize);

            m_SpriteImage = host.GetComponent<Image>();
            m_SpriteImage.preserveAspect = true;
            m_SpriteImage.raycastTarget = false;
            ApplySprite();
        }

        /// <summary>把当前状态对应的贴图写进 Image。</summary>
        private void ApplySprite()
        {
            if (m_SpriteImage == null)
            {
                return;
            }

            m_SpriteImage.sprite = m_Reloading ? m_ReloadSprite : m_NormalSprite;
            m_SpriteImage.color = Color.white;
        }

        /// <summary>
        /// 程序化兜底：四条短线构成十字，中心留出空隙。
        /// </summary>
        /// <remarks>每条线由内外两层图像组成（深色描边 + 亮色主体），
        /// 这样在浅色地面和深色地面上都能保持可见。</remarks>
        private void BuildBars()
        {
            m_BarFills = new Image[4];

            CreateBar(0, new Vector2(-m_GapRadius - (m_ArmLength * 0.5f), 0f), new Vector2(m_ArmLength, m_Thickness));
            CreateBar(1, new Vector2(m_GapRadius + (m_ArmLength * 0.5f), 0f), new Vector2(m_ArmLength, m_Thickness));
            CreateBar(2, new Vector2(0f, m_GapRadius + (m_ArmLength * 0.5f)), new Vector2(m_Thickness, m_ArmLength));
            CreateBar(3, new Vector2(0f, -m_GapRadius - (m_ArmLength * 0.5f)), new Vector2(m_Thickness, m_ArmLength));
            ApplyBarColor();
        }

        /// <summary>创建一条准星线。</summary>
        private void CreateBar(int index, Vector2 position, Vector2 size)
        {
            var outline = new GameObject("Bar" + index + "Outline", typeof(RectTransform), typeof(Image));
            var outlineRect = (RectTransform)outline.transform;
            outlineRect.SetParent(m_Root, worldPositionStays: false);
            outlineRect.anchoredPosition = position;
            outlineRect.sizeDelta = size + new Vector2(
                m_OutlineThickness - m_Thickness,
                m_OutlineThickness - m_Thickness);

            var outlineImage = outline.GetComponent<Image>();
            outlineImage.color = m_OutlineColor;
            outlineImage.raycastTarget = false;

            var bar = new GameObject("Bar" + index, typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)bar.transform;
            barRect.SetParent(outlineRect, worldPositionStays: false);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = size;

            var image = bar.GetComponent<Image>();
            image.raycastTarget = false;
            m_BarFills[index] = image;
        }

        /// <summary>把换弹状态对应的颜色写进程序化准星。</summary>
        private void ApplyBarColor()
        {
            if (m_BarFills == null)
            {
                return;
            }

            var color = m_Reloading ? m_ReloadColor : m_Color;
            for (var i = 0; i < m_BarFills.Length; i++)
            {
                if (m_BarFills[i] != null)
                {
                    m_BarFills[i].color = color;
                }
            }
        }
    }
}
