using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 屏幕空间的瞄准准星。
    /// </summary>
    /// <remarks>
    /// <para>准星位置由外部每帧写入（通常来自鼠标射线与地面的交点），
    /// 本组件只负责把它画在屏幕上，不参与任何瞄准计算。
    /// 这样准星与角色朝向可以共用同一个世界坐标，从而天然对齐——
    /// 若准星与朝向各自独立计算，两者必然会出现视觉偏差。</para>
    ///
    /// <para>使用屏幕空间而非世界空间物体：世界空间的准星会被场景几何遮挡、
    /// 受透视缩放影响大小，且在高低差地形上会被墙体挡住。
    /// 屏幕空间绘制可以保证准星始终清晰可见、尺寸恒定。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AimCrosshair : MonoBehaviour
    {
        /// <summary>准星线条长度（像素）。</summary>
        [SerializeField] private float m_ArmLength = 10f;

        /// <summary>准星线条粗细（像素）。</summary>
        [SerializeField] private float m_Thickness = 2f;

        /// <summary>中心空隙半径（像素）。留出空隙便于看清瞄准点本身。</summary>
        [SerializeField] private float m_GapRadius = 4f;

        /// <summary>准星颜色。</summary>
        [SerializeField] private Color m_Color = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>准星外圈颜色，用于在浅色背景上保持可见性。</summary>
        [SerializeField] private Color m_OutlineColor = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>准星中心在屏幕上的位置（像素）。</summary>
        private Vector2 m_ScreenPosition;

        private bool m_Visible;
        private Texture2D m_Pixel;

        /// <summary>设置准星位置。传入的坐标以屏幕像素为单位，原点在左下角。</summary>
        public void SetScreenPosition(Vector2 screenPosition)
        {
            m_ScreenPosition = screenPosition;
            m_Visible = true;
        }

        /// <summary>隐藏准星。</summary>
        public void Hide()
        {
            m_Visible = false;
        }

        private void Awake()
        {
            // 用一个 1x1 白纹理配合 GUI 颜色来绘制所有线条，
            // 避免为每条线创建独立纹理。
            m_Pixel = new Texture2D(1, 1);
            m_Pixel.SetPixel(0, 0, Color.white);
            m_Pixel.Apply();
        }

        private void OnDestroy()
        {
            if (m_Pixel != null)
            {
                Destroy(m_Pixel);
            }
        }

        private void OnGUI()
        {
            if (!m_Visible || m_Pixel == null)
            {
                return;
            }

            // GUI 坐标以左上角为原点，而输入系统给出的屏幕坐标以左下角为原点，需要翻转 Y。
            var center = new Vector2(m_ScreenPosition.x, Screen.height - m_ScreenPosition.y);

            // 先画一圈深色描边，再画白色主体：这样在浅色与深色背景上都能看清。
            DrawCross(center, m_Thickness + 2f, m_OutlineColor);
            DrawCross(center, m_Thickness, m_Color);
        }

        private void DrawCross(Vector2 center, float thickness, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;

            var half = thickness * 0.5f;

            // 四条短线构成十字，中心留出空隙。
            GUI.DrawTexture(new Rect(center.x - m_GapRadius - m_ArmLength, center.y - half, m_ArmLength, thickness), m_Pixel);
            GUI.DrawTexture(new Rect(center.x + m_GapRadius, center.y - half, m_ArmLength, thickness), m_Pixel);
            GUI.DrawTexture(new Rect(center.x - half, center.y - m_GapRadius - m_ArmLength, thickness, m_ArmLength), m_Pixel);
            GUI.DrawTexture(new Rect(center.x - half, center.y + m_GapRadius, thickness, m_ArmLength), m_Pixel);

            GUI.color = previous;
        }
    }
}
