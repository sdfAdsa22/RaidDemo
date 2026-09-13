using RaidDemo.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 口径小徽标：一块按口径着色的圆角小标签，写着 5.45 / 9x19 / 12 号。
    /// </summary>
    /// <remarks>
    /// <para>它出现在物品格右上角、商人货架行与 HUD 需要提示口径的地方。
    /// 颜色由 <see cref="CaliberPalette"/> 统一给出，因此"橙色 = 步枪弹"在整块界面上只学一次。</para>
    /// <para>做成静态工厂而不是 MonoBehaviour：它没有状态、不处理输入，
    /// 每次刷新（容器重画）都要重建几十个，用组件反而多一层销毁与注册成本。</para>
    /// </remarks>
    internal static class CaliberBadge
    {
        /// <summary>徽标默认尺寸（像素）。</summary>
        public static readonly Vector2 DefaultSize = new Vector2(42f, 16f);

        /// <summary>徽标字号：比正文小一档，保证它是"标签"而不是"标题"。</summary>
        private const float FontSize = 11f;

        /// <summary>
        /// 按任意锚点创建一个口径徽标。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="caliberId">口径标识；为空时不创建，返回 null。</param>
        /// <param name="anchor">锚点。</param>
        /// <param name="pivot">轴心。</param>
        /// <param name="offset">相对锚点的偏移（屏幕坐标方向）。</param>
        /// <param name="size">尺寸。</param>
        public static RectTransform CreateAnchored(
            RectTransform parent,
            string caliberId,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 offset,
            Vector2 size)
        {
            if (parent == null || string.IsNullOrEmpty(caliberId))
            {
                return null;
            }

            var rect = UiFactory.CreateAnchored(
                parent, "CaliberBadge", UiSprites.Block, anchor, pivot, offset, size);
            rect.GetComponent<Image>().color = CaliberPalette.GetColor(caliberId);

            var label = UiFactory.CreateLabel(
                rect,
                CaliberPalette.GetDisplayName(caliberId),
                Vector2.zero,
                size,
                FontSize,
                TextAlignmentOptions.Center,
                Color.white);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = Vector2.zero;
            return rect;
        }
    }
}
