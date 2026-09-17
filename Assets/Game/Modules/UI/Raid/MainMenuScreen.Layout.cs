using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 主菜单的版式构建（方案 B：左品牌、右菜单）。
    /// </summary>
    /// <remarks>
    /// <para>与输入轮询分开放：这份回答「界面怎么长」，主文件回答「界面怎么响应」。
    /// 拆分的直接触发点是行数上限测试（单文件 ≤400 行）。</para>
    ///
    /// <para><b>为什么不引入任何图片资源：</b>装饰（大圆、斜纹、山峦、准星）全部由
    /// <see cref="UiSprites"/> 的运行时贴图与旋转方块拼成。这样配色仍然只有
    /// <see cref="UiPalette"/> 一个来源，之后调整风格不需要重导资源。</para>
    /// </remarks>
    public sealed partial class MainMenuScreen
    {
        // ---- 尺寸 ----

        /// <summary>左栏（品牌区）宽度；右栏自动占满剩余宽度。</summary>
        private const float BrandColumnWidth = 700f;

        /// <summary>左栏内容距卡片边缘的距离。</summary>
        private const float CardPadding = 56f;

        /// <summary>右栏深色面板相对卡片边缘的内缩：留出卡片自己的墨色描边。</summary>
        private const float MenuInset = 8f;

        /// <summary>竖排按钮的统一尺寸。</summary>
        /// <remarks>宽 520 要容得下二次确认时的「再次点击确认清空进度」，同时与右栏宽度成比例。</remarks>
        private static readonly Vector2 ButtonSize = new Vector2(520f, 68f);

        /// <summary>按钮列的行距（按钮高 68 + 间隙 16）。</summary>
        private const float ButtonStep = 84f;

        /// <summary>第一个按钮距卡片顶边的距离。</summary>
        private const float ButtonColumnTop = 88f;

        /// <summary>左栏信息胶囊的高度与纵向位置。</summary>
        private const float ChipHeight = 38f;
        private const float ChipTop = 618f;

        /// <summary>按钮列左边缘：在右栏深色面板里水平居中。</summary>
        private static float ButtonColumnX =>
            BrandColumnWidth + MenuInset +
            ((PanelSize.x - BrandColumnWidth - (MenuInset * 2f) - ButtonSize.x) * 0.5f);

        /// <summary>右栏深色面板的宽度。</summary>
        private static float MenuColumnWidth => PanelSize.x - BrandColumnWidth - (MenuInset * 2f);

        // ---- 背景装饰 ----

        /// <summary>背景装饰层：两个深色大圆、一块斜纹、底部山峦与一枚准星。</summary>
        /// <remarks>所有装饰都不参与输入，也不随窗口比例挤进卡片区域——它们锚在屏幕四角，
        /// 窗口拉宽时向外滑走，卡片始终是视觉中心。</remarks>
        private static void BuildBackdropDecor(RectTransform root)
        {
            // 左上、右下的大圆：位置刻意压在屏幕外一半，只留一段弧线当色块焦点。
            CreateDecorSprite(
                root, "DecorCircleTopLeft", UiSprites.Disc, new Vector2(0f, 1f), new Vector2(140f, -60f),
                new Vector2(640f, 640f), UiPalette.MenuBackdropDeep, 0f, false);
            CreateDecorSprite(
                root, "DecorCircleBottomRight", UiSprites.Disc, new Vector2(1f, 0f), new Vector2(-70f, -50f),
                new Vector2(420f, 420f), WithAlpha(UiPalette.MenuBackdropDeep, 0.85f), 0f, false);

            // 右上斜纹块：45° 平铺纹理旋转 -8° 即可，比摆一排斜条省十来个节点。
            CreateDecorSprite(
                root, "DecorStripes", UiSprites.StripeTile, new Vector2(1f, 1f), new Vector2(-150f, -100f),
                new Vector2(420f, 340f), WithAlpha(UiPalette.MenuBackdropDeep, 0.55f), -8f, true);

            // 底部山峦：四个旋转 45° 的圆角方块（菱形）只露出上半截，拼出低多边形的山脊剪影。
            CreateDecorSprite(root, "DecorHill1", UiSprites.Block, new Vector2(0f, 0f), new Vector2(240f, -100f),
                new Vector2(300f, 300f), UiPalette.MenuBackdropHill, 45f, false);
            CreateDecorSprite(root, "DecorHill2", UiSprites.Block, new Vector2(0f, 0f), new Vector2(700f, -170f),
                new Vector2(430f, 430f), UiPalette.MenuBackdropHill, 45f, false);
            CreateDecorSprite(root, "DecorHill3", UiSprites.Block, new Vector2(0f, 0f), new Vector2(1250f, -120f),
                new Vector2(320f, 320f), UiPalette.MenuBackdropHill, 45f, false);
            CreateDecorSprite(root, "DecorHill4", UiSprites.Block, new Vector2(0f, 0f), new Vector2(1700f, -90f),
                new Vector2(260f, 260f), UiPalette.MenuBackdropHill, 45f, false);

            // 右上角准星：环 + 十字，呼应射击题材；透明度压得很低，只当背景纹理。
            var crosshair = WithAlpha(UiPalette.MenuCrosshair, 0.55f);
            CreateDecorSprite(root, "DecorCrosshairRing", UiSprites.RingWhite, new Vector2(0f, 1f), new Vector2(1345f, -165f),
                new Vector2(96f, 96f), crosshair, 0f, false);
            CreateDecorSprite(root, "DecorCrosshairH", UiSprites.Block, new Vector2(0f, 1f), new Vector2(1345f, -165f),
                new Vector2(96f, 4f), crosshair, 0f, false);
            CreateDecorSprite(root, "DecorCrosshairV", UiSprites.Block, new Vector2(0f, 1f), new Vector2(1345f, -165f),
                new Vector2(4f, 96f), crosshair, 0f, false);
        }

        /// <summary>创建一块背景装饰贴图（支持旋转与平铺）。</summary>
        /// <param name="anchor">锚点：装饰贴屏幕四角。</param>
        /// <param name="offset">相对锚点的偏移（屏幕坐标方向，Y 向上为正）。</param>
        /// <param name="rotation">绕 Z 轴的旋转角度，0 表示不旋转。</param>
        /// <param name="tiled">true 时用 <see cref="Image.Type.Tiled"/> 平铺（斜纹用）。</param>
        private static void CreateDecorSprite(
            RectTransform parent,
            string name,
            Sprite sprite,
            Vector2 anchor,
            Vector2 offset,
            Vector2 size,
            Color color,
            float rotation,
            bool tiled)
        {
            var rect = UiFactory.CreateRect(parent, name);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            if (!Mathf.Approximately(rotation, 0f))
            {
                rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            }

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = tiled ? Image.Type.Tiled : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
        }

        // ---- 左栏：品牌 ----

        /// <summary>左栏：大标题、导语、强退提示、信息胶囊与版本行。</summary>
        private void BuildBrandColumn(RectTransform panel)
        {
            // 双色大标题走 TMP 富文本：标签字符（<color=#…>）全部是 ASCII，
            // 而字体资产的字符集本来就包含全部 ASCII，不会因此触发"缺字"的资产测试。
            var title = "RAID <color=#" + ColorUtility.ToHtmlStringRGB(UiPalette.TealDark) + ">DEMO</color>";
            UiFactory.CreateLabel(
                panel,
                title,
                new Vector2(CardPadding, 64f),
                new Vector2(620f, 104f),
                UiPalette.HeroSize,
                TextAlignmentOptions.Left,
                UiPalette.Ink);

            // 标题下的青色短横：用 Track（白底墨描边）染色，得到一条带描边的色条。
            var underscore = UiFactory.CreatePanel(
                panel, "TitleUnderscore", new Vector2(220f, 14f), UiSprites.Track, new Vector2(CardPadding, 184f));
            underscore.GetComponent<Image>().color = UiPalette.Teal;

            UiFactory.CreateLabel(
                panel,
                "3D 斜俯视搜打撤 · 卡通低多边形",
                new Vector2(CardPadding, 214f),
                new Vector2(584f, 34f),
                UiPalette.SubtitleSize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            UiFactory.CreateLabel(
                panel,
                "在一块不大的安全屋里整理仓库、试枪，从出口选地图出击，带着战利品撤离。",
                new Vector2(CardPadding, 264f),
                new Vector2(584f, 60f),
                UiPalette.BodySize,
                TextAlignmentOptions.TopLeft,
                UiPalette.Ink,
                wrap: true);

            UiFactory.CreateLabel(
                panel,
                "操作说明写在安全屋的墙上；出击前的准备也都在那里完成。\n"
                + "联机是 2~4 人合作：所有人在同一台服务器上创建 / 加入同一个房间，由房主开局。",
                new Vector2(CardPadding, 332f),
                new Vector2(584f, 64f),
                UiPalette.BodySize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkSoft,
                wrap: true);

            m_NoticeLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(CardPadding, 420f),
                new Vector2(584f, 56f),
                UiPalette.BodySize,
                TextAlignmentOptions.TopLeft,
                UiPalette.Warn,
                wrap: true);
            m_NoticeLabel.gameObject.SetActive(false);

            BuildChip(panel, "单人 · 一局 8 分钟", CardPadding, 214f, UiPalette.Ok);
            BuildChip(panel, "联机 2~4 人合作", CardPadding + 228f, 196f, UiPalette.Yellow);

            UiFactory.CreateLabel(
                panel,
                "Windows 桌面版 · v" + Application.version,
                new Vector2(CardPadding, 676f),
                new Vector2(500f, 26f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.InkDisabled);
        }

        /// <summary>左栏底部的信息胶囊：白底墨描边 + 一个小圆点 + 一行小字。</summary>
        /// <param name="dotColor">圆点颜色：普通信息用成功绿，需要留意的用高亮黄。</param>
        private static void BuildChip(RectTransform panel, string text, float x, float width, Color dotColor)
        {
            var chip = UiFactory.CreatePanel(
                panel, "Chip", new Vector2(width, ChipHeight), UiSprites.Chip, new Vector2(x, ChipTop));

            var dot = UiFactory.CreateRect(chip, "Dot");
            dot.anchorMin = new Vector2(0f, 0.5f);
            dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.anchoredPosition = new Vector2(20f, 0f);
            dot.sizeDelta = new Vector2(12f, 12f);
            var dotImage = dot.gameObject.AddComponent<Image>();
            dotImage.sprite = UiSprites.Disc;
            dotImage.type = Image.Type.Simple;
            dotImage.color = dotColor;
            dotImage.raycastTarget = false;

            UiFactory.CreateLabel(
                chip,
                text,
                new Vector2(36f, 0f),
                new Vector2(width - 46f, ChipHeight),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.Ink);
        }

        // ---- 右栏：菜单 ----

        /// <summary>右栏：深色底板、两栏分隔线、竖排按钮与底部操作提示。</summary>
        private void BuildMenuColumn(RectTransform panel)
        {
            // 深色底板：相对卡片内缩 8 像素，卡片自己的墨色描边仍然可见，形成"内嵌面板"的层次。
            var plate = UiFactory.CreatePanel(
                panel,
                "MenuPlate",
                new Vector2(MenuColumnWidth, PanelSize.y - (MenuInset * 2f)),
                UiSprites.Block,
                new Vector2(BrandColumnWidth + MenuInset, MenuInset));
            plate.GetComponent<Image>().color = UiPalette.MenuBackdrop;

            // 两栏之间的墨色分隔线：与卡片描边同色，把"品牌"与"菜单"干净地切开。
            var divider = UiFactory.CreatePanel(
                panel,
                "ColumnDivider",
                new Vector2(4f, PanelSize.y - (MenuInset * 2f)),
                UiSprites.Block,
                new Vector2(BrandColumnWidth - 2f, MenuInset));
            divider.GetComponent<Image>().color = UiPalette.Outline;

            // 四个按钮先按固定槽位创建，再交给 LayoutButtonColumn() 按可见性重排：
            // 「没有存档」「没有联机入口」时下面的按钮会自动上移补位，而不是在列表里留一个洞。
            m_ContinueButton = UiFactory.CreateButton(
                panel, "开始游戏（Enter）", new Vector2(ButtonColumnX, ButtonColumnTop), ButtonSize, UiButtonKind.Primary);
            m_NewGameButton = UiFactory.CreateButton(
                panel, "新游戏", new Vector2(ButtonColumnX, ButtonColumnTop + ButtonStep), ButtonSize);
            m_MultiplayerButton = UiFactory.CreateButton(
                panel, "联机", new Vector2(ButtonColumnX, ButtonColumnTop + (ButtonStep * 2f)), ButtonSize);
            m_QuitButton = UiFactory.CreateButton(
                panel, "退出游戏", new Vector2(ButtonColumnX, ButtonColumnTop + (ButtonStep * 3f)), ButtonSize);
            m_MultiplayerButton.Rect.gameObject.SetActive(m_OnMultiplayer != null);
            m_QuitButton.Rect.gameObject.SetActive(m_OnQuit != null);
            LayoutButtonColumn();

            UiFactory.CreateLabel(
                panel,
                "WASD 移动 · Shift 奔跑 · 左键射击 · R 换弹 · Tab 背包 · F1 调试",
                new Vector2(ButtonColumnX, PanelSize.y - MenuInset - 60f),
                new Vector2(ButtonSize.x, 28f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.OnDarkSoft);
        }

        /// <summary>
        /// 按当前可见按钮重排按钮列：隐藏的按钮不占槽位，其余按钮保持固定间距依序下移。
        /// </summary>
        private void LayoutButtonColumn()
        {
            var index = 0;
            PlaceButton(m_ContinueButton, ref index);
            PlaceButton(m_NewGameButton, ref index);
            PlaceButton(m_MultiplayerButton, ref index);
            PlaceButton(m_QuitButton, ref index);
        }

        /// <summary>把单个按钮放到第 <paramref name="index"/> 个槽位；隐藏的按钮跳过且不占用槽位。</summary>
        /// <remarks>按钮列以左上角为锚点、Y 向下为正，因此写入的 Y 是负值。</remarks>
        private void PlaceButton(UiButton button, ref int index)
        {
            if (button == null || !button.Rect.gameObject.activeSelf)
            {
                return;
            }

            button.Rect.anchoredPosition = new Vector2(ButtonColumnX, -(ButtonColumnTop + (ButtonStep * index)));
            index++;
        }

        /// <summary>保持 RGB、替换透明度：装饰层与深色底上的文字要压 alpha。</summary>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }
}
