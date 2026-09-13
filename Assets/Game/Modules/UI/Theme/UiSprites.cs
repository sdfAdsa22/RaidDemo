using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 界面用的九宫格贴图：运行时按配方绘制并缓存。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是"画出来"而不是导入图片：</b>这一套控件的形状规则只有三条
    /// （圆角半径、描边宽度、底部硬阴影高度），颜色全部来自 <see cref="UiPalette"/>。
    /// 如果做成 PNG，改一次配色就要重导一遍图，而且颜色会在图片与代码里各存一份。
    /// 画出来之后，"主色换成什么"仍然只有 <see cref="UiPalette"/> 一个答案。</para>
    ///
    /// <para><b>为什么是九宫格：</b>面板要能拉伸成任意尺寸而圆角与描边不变形，
    /// 这是 uGUI 的 Sliced 模式加边界（border）的标准做法。生成分辨率取参考尺寸的 2 倍，
    /// 再配合 <see cref="PixelsPerUnit"/> 让 1 个贴图像素正好等于 0.5 个参考像素——
    /// 于是描边 6 像素在界面上就是 3 像素，边缘仍然锐利。</para>
    ///
    /// <para>所有贴图在首次使用时创建并常驻（十来个 112×112 的 RGBA 贴图，合计约 0.5 MB），
    /// 不随场景重载重复生成。</para>
    /// </remarks>
    internal static class UiSprites
    {
        /// <summary>贴图边长（像素）。</summary>
        private const int Size = 112;

        /// <summary>九宫格边界宽度（像素）：必须大于圆角 + 描边（+ 阴影）。</summary>
        private const int Border = 32;

        /// <summary>生成分辨率与参考分辨率的比值。</summary>
        private const float Supersample = 2f;

        /// <summary>贴图的每单位像素数。</summary>
        /// <remarks>与画布参考分辨率（1920×1080、每单位 100 像素）配合，
        /// 使 1 个贴图像素 = 0.5 个参考像素。</remarks>
        private const float PixelsPerUnit = 100f * Supersample;

        private static Sprite s_Card;
        private static Sprite s_CardDim;
        private static Sprite s_Button;
        private static Sprite s_ButtonPrimary;
        private static Sprite s_ButtonPrimaryPressed;
        private static Sprite s_ButtonPressed;
        private static Sprite s_Chip;
        private static Sprite s_Plate;
        private static Sprite s_Cell;
        private static Sprite s_Slot;
        private static Sprite s_Track;
        private static Sprite s_Fill;
        private static Sprite s_Ring;
        private static Sprite s_RingWhite;
        private static Sprite s_Block;

        /// <summary>奶油色面板（不透明）。</summary>
        public static Sprite Card => s_Card ??= Build("Ui_Card", UiPalette.Paper, UiPalette.Outline, 3f, 0f);

        /// <summary>次级面板 / 标题条（比面板略深）。</summary>
        public static Sprite CardDim => s_CardDim ??= Build("Ui_CardDim", UiPalette.PaperDim, UiPalette.Outline, 3f, 0f);

        /// <summary>普通按钮（白底 + 墨描边 + 底部硬阴影）。</summary>
        public static Sprite Button => s_Button ??= Build("Ui_Button", UiPalette.ButtonFace, UiPalette.Outline, 3f, UiPalette.ButtonDepth);

        /// <summary>主行动按钮（青绿底）。</summary>
        public static Sprite ButtonPrimary => s_ButtonPrimary ??= Build("Ui_ButtonPrimary", UiPalette.Teal, UiPalette.Outline, 3f, UiPalette.ButtonDepth);

        /// <summary>按钮按下态：底色变深、阴影消失，形成"压下去"的错觉。</summary>
        public static Sprite ButtonPressed => s_ButtonPressed ??= Build("Ui_ButtonPressed", UiPalette.ButtonPressed, UiPalette.Outline, 3f, 0f);

        /// <summary>主按钮按下态。</summary>
        public static Sprite ButtonPrimaryPressed => s_ButtonPrimaryPressed ??= Build("Ui_ButtonPrimaryPressed", UiPalette.TealDark, UiPalette.Outline, 3f, 0f);

        /// <summary>压在世界上的白色小胶囊（HUD 徽标）。</summary>
        public static Sprite Chip => s_Chip ??= Build("Ui_Chip", UiPalette.ButtonFace, UiPalette.Outline, 3f, 0f);

        /// <summary>压在世界上的半透明深色底板（HUD 条、提示）。</summary>
        public static Sprite Plate => s_Plate ??= Build("Ui_Plate", UiPalette.HudPlate, UiPalette.Outline, 3f, 0f);

        /// <summary>物品格。</summary>
        public static Sprite Cell => s_Cell ??= Build("Ui_Cell", new Color(1f, 0.99f, 0.97f), UiPalette.PaperLine, 2f, 0f);

        /// <summary>空装备槽。</summary>
        public static Sprite Slot => s_Slot ??= Build("Ui_Slot", UiPalette.PaperDim, UiPalette.PaperLine, 2f, 0f);

        /// <summary>进度条底槽。</summary>
        public static Sprite Track => s_Track ??= Build("Ui_Track", UiPalette.ButtonFace, UiPalette.Outline, 3f, 0f);

        /// <summary>进度条填充（无描边，使用时按语义染色）。</summary>
        public static Sprite Fill => s_Fill ??= Build("Ui_Fill", Color.white, Color.white, 0f, 0f);

        /// <summary>选中环（中空，只画描边），叠在格子上表示"当前选中"。</summary>
        public static Sprite Ring => s_Ring ??= Build("Ui_Ring", new Color(0f, 0f, 0f, 0f), UiPalette.Teal, 3f, 0f);

        /// <summary>白色描边环：使用时按稀有度染色，就是物品格的外框。</summary>
        public static Sprite RingWhite => s_RingWhite ??= Build("Ui_RingWhite", new Color(0f, 0f, 0f, 0f), Color.white, 3f, 0f);

        /// <summary>纯白实心圆角块（无描边）：使用时按语义染色，充当地块与进度条填充。</summary>
        public static Sprite Block => s_Block ??= Build("Ui_Block", Color.white, Color.white, 0f, 0f);

        /// <summary>
        /// 画一张圆角矩形贴图并切成九宫格。
        /// </summary>
        /// <param name="name">贴图名（便于在 Profiler 里辨认）。</param>
        /// <param name="face">填充色（可为半透明）。</param>
        /// <param name="outline">描边色（alpha 为 0 时不画描边）。</param>
        /// <param name="outlineWidth">描边宽度（参考像素）。</param>
        /// <param name="shadow">底部硬阴影高度（参考像素），0 表示不画。</param>
        private static Sprite Build(string name, Color face, Color outline, float outlineWidth, float shadow)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var radius = UiPalette.CornerRadius * Supersample;
            var half = (Size * 0.5f) - 1f;
            var stroke = outlineWidth * Supersample;
            var depth = shadow * Supersample;
            var center = new Vector2(Size * 0.5f, Size * 0.5f);
            var pixels = new Color32[Size * Size];

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    // 必须先平移到以贴图中心为原点：距离场用 |x|、|y| 判断对称性，
                    // 直接拿左下角为原点的坐标算，画出来的会是"紧贴左下角的一小块圆角矩形"。
                    var point = new Vector2(x + 0.5f, y + 0.5f) - center;

                    // 形状本身：以贴图中心为基准的圆角矩形有符号距离场（SDF）。
                    var distance = RoundedBox(point, half, radius);
                    var inside = Coverage(distance);
                    var inner = Coverage(distance + stroke);

                    var color = new Color(0f, 0f, 0f, 0f);

                    // 底部硬阴影：同一个形状向上偏移一段距离后压在下面。
                    if (depth > 0f)
                    {
                        var shadowDistance = RoundedBox(point - new Vector2(0f, depth), half, radius);
                        var shadowAlpha = Coverage(shadowDistance) * 0.35f;
                        Blend(ref color, new Color(UiPalette.Outline.r, UiPalette.Outline.g, UiPalette.Outline.b, shadowAlpha));
                    }

                    Blend(ref color, WithAlpha(face, face.a * inner));
                    Blend(ref color, WithAlpha(outline, outline.a * (inside - inner)));
                    pixels[(y * Size) + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var border = new Vector4(Border, Border, Border, Border);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>圆角矩形的有符号距离：内部为负，边界为 0。</summary>
        private static float RoundedBox(Vector2 point, float half, float radius)
        {
            var qx = Mathf.Abs(point.x) - half + radius;
            var qy = Mathf.Abs(point.y) - half + radius;
            var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return Mathf.Min(Mathf.Max(qx, qy), 0f) + outside - radius;
        }

        /// <summary>把距离场换算成覆盖率，边界 1 像素内做抗锯齿。</summary>
        private static float Coverage(float distance)
        {
            return Mathf.Clamp01(0.5f - distance);
        }

        /// <summary>标准 source-over 合成，用于把多层形状叠成一张贴图。</summary>
        private static void Blend(ref Color destination, Color source)
        {
            if (source.a <= 0f)
            {
                return;
            }

            var outAlpha = source.a + (destination.a * (1f - source.a));
            if (outAlpha <= 0f)
            {
                destination = new Color(0f, 0f, 0f, 0f);
                return;
            }

            var r = ((source.r * source.a) + (destination.r * destination.a * (1f - source.a))) / outAlpha;
            var g = ((source.g * source.a) + (destination.g * destination.a * (1f - source.a))) / outAlpha;
            var b = ((source.b * source.a) + (destination.b * destination.a * (1f - source.a))) / outAlpha;
            destination = new Color(r, g, b, outAlpha);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }
    }
}
