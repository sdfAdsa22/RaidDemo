using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 界面配色与尺寸常量：M7 批次 4「卡通扁平」风格的唯一色彩来源。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么所有颜色都集中在这里：</b>这一批要改 9 块界面。如果颜色散在各文件里，
    /// "把主色从青绿换成别的"就变成一次全局搜索；集中之后，调整风格是改这一个文件。</para>
    ///
    /// <para><b>配色为什么是这几组：</b>世界是高饱和卡通（亮绿草地 + 橙黏土），
    /// 因此界面走"奶油纸面 + 深墨描边"——亮色面板压在高饱和场景上仍然清晰，
    /// 而高饱和只留给**状态与稀有度**（成功/警告/危险、白绿蓝紫金）。
    /// 主交互色取青绿，是因为它既不在稀有度的五个色位里，也区别于"危险红"与"超时橙"。</para>
    /// </remarks>
    public static class UiPalette
    {
        // ---- 纸张与墨色 ----

        /// <summary>面板底色（奶油纸）。</summary>
        public static readonly Color Paper = FromHex("#F6EFE3");

        /// <summary>次级面板底色（标题条、内嵌区域）。</summary>
        public static readonly Color PaperDim = FromHex("#ECE2D1");

        /// <summary>面板上的浅色分隔线。</summary>
        public static readonly Color PaperLine = FromHex("#D8CCB6");

        /// <summary>主文字（深墨）。</summary>
        public static readonly Color Ink = FromHex("#33302B");

        /// <summary>次级文字。</summary>
        public static readonly Color InkSoft = FromHex("#6F685D");

        /// <summary>禁用文字。</summary>
        public static readonly Color InkDisabled = FromHex("#A79F90");

        /// <summary>描边色：所有面板与按钮的 3px 厚描边都用它。</summary>
        public static readonly Color Outline = FromHex("#2E2A26");

        // ---- 交互与状态 ----

        /// <summary>按钮基础底色（白）。</summary>
        public static readonly Color ButtonFace = FromHex("#FFFFFF");

        /// <summary>按钮悬停底色。</summary>
        public static readonly Color ButtonHover = FromHex("#FFF6E4");

        /// <summary>按钮按下底色（同时去掉底部阴影）。</summary>
        public static readonly Color ButtonPressed = FromHex("#E7DCC8");

        /// <summary>主强调色（青绿）：主行动按钮、选中态。</summary>
        public static readonly Color Teal = FromHex("#2FA8A0");

        /// <summary>主强调色的深色变体（文字压在青绿底上时用的白字之外的第二选择）。</summary>
        public static readonly Color TealDark = FromHex("#1F7C76");

        /// <summary>高亮黄：预算提示、稀有度之外的重点标记。</summary>
        public static readonly Color Yellow = FromHex("#F5C542");

        /// <summary>成功（撤离成功、正向收益）。</summary>
        public static readonly Color Ok = FromHex("#3FA85C");

        /// <summary>警告（体力低、负重超限）。</summary>
        public static readonly Color Warn = FromHex("#D98A1F");

        /// <summary>危险（阵亡、亏损、禁用）。</summary>
        public static readonly Color Bad = FromHex("#D7443C");

        // ---- 遮罩与世界之上 ----

        /// <summary>
        /// 面板打开时的世界遮罩。
        /// </summary>
        /// <remarks>只压暗一半：奶油面板本身够亮，遮罩再深会变成"贴在屏幕上的一张纸"，
        /// 留一点世界在背后能提醒玩家自己还在局内。</remarks>
        public static readonly Color Veil = new Color(0.05f, 0.07f, 0.09f, 0.55f);

        /// <summary>
        /// 主菜单的独立底色。
        /// </summary>
        /// <remarks>主菜单是**不透明独立界面**（负责人 2026-09-13 决定）：它不叠在游戏场景上，
        /// 因此这里给的是实色深绿松，用来衬托奶油色面板与青绿按钮。</remarks>
        public static readonly Color MenuBackdrop = FromHex("#1C3A35");

        /// <summary>主菜单底色的深色分层，用于背景装饰块。</summary>
        public static readonly Color MenuBackdropDeep = FromHex("#152C29");

        /// <summary>HUD 元素压在场景上时的底色（半透明深色，保证白字可读）。</summary>
        public static readonly Color HudPlate = new Color(0.10f, 0.12f, 0.13f, 0.82f);

        // ---- 背包专用 ----

        /// <summary>空装备槽底色。</summary>
        public static readonly Color SlotEmpty = FromHex("#E7DCC8");

        /// <summary>装备槽 / 格子被鼠标悬停时的底色。</summary>
        public static readonly Color SlotHover = FromHex("#CDE9E5");

        /// <summary>拖拽落点预览：合法（绿）。</summary>
        public static readonly Color PreviewValid = FromHex("#8FDCA0");

        /// <summary>拖拽落点预览：非法（红）。</summary>
        public static readonly Color PreviewInvalid = FromHex("#F0A19B");

        /// <summary>
        /// 物品格的填充色：把稀有度色向白色稀释。
        /// </summary>
        /// <param name="rarity">物品稀有度。</param>
        /// <returns>浅到可以在上面写深色字的底色。</returns>
        /// <remarks>稀释比例 0.78 是"能一眼看出颜色、又不影响读字"的取值：
        /// 再深一点，深色文字就开始吃力；再浅一点，五个档位在纸面上分不出来。</remarks>
        public static Color ItemFill(RaidDemo.Data.RarityTier rarity)
        {
            var color = RaidDemo.Data.RarityPalette.GetColorOnPaper(rarity);
            return Color.Lerp(color, Color.white, 0.78f);
        }

        /// <summary>取某个稀有度在纸面上的描边色。</summary>
        public static Color ItemOutline(RaidDemo.Data.RarityTier rarity)
        {
            return RaidDemo.Data.RarityPalette.GetColorOnPaper(rarity);
        }

        // ---- 尺寸 ----

        /// <summary>描边宽度（参考像素）。</summary>
        public const float OutlineWidth = 3f;

        /// <summary>圆角半径（参考像素）。</summary>
        public const float CornerRadius = 12f;

        /// <summary>按钮底部硬阴影高度（参考像素）。</summary>
        public const float ButtonDepth = 4f;

        /// <summary>标题字号。</summary>
        public const float TitleSize = 46f;

        /// <summary>小标题字号。</summary>
        public const float SubtitleSize = 20f;

        /// <summary>正文字号。</summary>
        public const float BodySize = 17f;

        /// <summary>小字字号（提示、脚注）。</summary>
        public const float SmallSize = 14f;

        /// <summary>解析十六进制色值；解析失败返回洋红以便一眼看出配置错误。</summary>
        private static Color FromHex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;
        }
    }
}
