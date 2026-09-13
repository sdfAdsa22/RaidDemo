using RaidDemo.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 图鉴界面的布局部分：标题、页签、卡片网格与右侧详情栏的搭建。
    /// </summary>
    /// <remarks>
    /// <para>从核心文件拆出来的原因与其它界面一致：单文件 400 行上限。
    /// 这里只有"东西摆在哪、长什么样"，筛选与输入都在 Actions 部分。</para>
    ///
    /// <para><b>网格容量是 7 × 3 = 21 格</b>：当前物品表 13 件，批次 2 的扩展也在此之内。
    /// 若将来超过 21 件，需要在这里加翻页或滚动——超出部分的卡片会叠在面板底部，而不会报错。</para>
    /// </remarks>
    public sealed partial class CodexScreenController
    {
        // ---- 网格与卡片尺寸 ----

        private const float CardWidth = 140f;

        private const float CardHeight = 176f;

        private const float CardPitchX = 147f;

        private const float CardPitchY = 192f;

        private const int CardColumns = 7;

        private const int CardRows = 3;

        /// <summary>网格区域左上角（面板坐标）。</summary>
        private static readonly Vector2 GridOrigin = new Vector2(40f, 160f);

        /// <summary>详情栏左上角与尺寸。</summary>
        private static readonly Vector2 DetailOrigin = new Vector2(1100f, 160f);

        private static readonly Vector2 DetailSize = new Vector2(360f, 580f);

        /// <summary>未获得卡片的底色（深炭）与剪影颜色。</summary>
        private static readonly Color UnknownFill = new Color(0.16f, 0.16f, 0.18f);

        private static readonly Color UnknownSilhouette = new Color(0.45f, 0.45f, 0.50f);

        /// <summary>搭建整块界面。</summary>
        private void BuildLayout()
        {
            m_ScreenRoot = new GameObject("CodexCanvas");
            m_ScreenRoot.transform.SetParent(transform, worldPositionStays: false);
            var canvas = m_ScreenRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = m_ScreenRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(
                UiFactory.ReferenceWidth, UiFactory.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var canvasRoot = (RectTransform)m_ScreenRoot.transform;
            UiFactory.CreateVeil(canvasRoot, "Veil");

            var panel = UiFactory.CreateCenteredPanel(
                canvasRoot, "Panel", PanelSize, UiSprites.Card);

            BuildTitleBar(panel);
            BuildTabRow(panel);
            BuildCardGrid(panel);
            BuildDetailPane(panel);
        }

        /// <summary>标题条：界面名 + 收集进度 + 关闭按钮。</summary>
        private void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreateLabel(
                panel, "收集图鉴", new Vector2(40f, 18f), new Vector2(420f, 50f),
                34f, TextAlignmentOptions.Left, UiPalette.Ink);

            m_ProgressLabel = UiFactory.CreateLabel(
                panel, string.Empty, new Vector2(PanelSize.x - 420f, 26f), new Vector2(320f, 34f),
                UiPalette.SubtitleSize, TextAlignmentOptions.Right, UiPalette.Money);

            m_CloseButton = UiFactory.CreateButton(
                panel, "关闭",
                new Vector2(PanelSize.x - 180f, PanelSize.y - 92f),
                new Vector2(140f, 52f));
        }

        /// <summary>页签行：七个类别，按定义顺序排开。</summary>
        private void BuildTabRow(RectTransform panel)
        {
            var filters = new[]
            {
                CodexFilter.All, CodexFilter.Weapon, CodexFilter.Ammo, CodexFilter.Armor,
                CodexFilter.Medical, CodexFilter.Backpack, CodexFilter.Misc,
            };

            const float tabWidth = 128f;
            const float tabGap = 10f;
            const float tabTop = 96f;
            const float tabHeight = 44f;

            for (var i = 0; i < filters.Length; i++)
            {
                var button = UiFactory.CreateButton(
                    panel,
                    ResolveFilterName(filters[i]),
                    new Vector2(GridOrigin.x + (i * (tabWidth + tabGap)), tabTop),
                    new Vector2(tabWidth, tabHeight));
                m_Tabs.Add(new TabWidget { Filter = filters[i], Button = button });
            }
        }

        /// <summary>卡片网格：目录里每件物品一张卡，按固定行列排开。</summary>
        private void BuildCardGrid(RectTransform panel)
        {
            var items = m_Catalog != null ? m_Catalog.All : null;
            if (items == null)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var column = i % CardColumns;
                var row = i / CardColumns;
                if (row >= CardRows)
                {
                    // 超出网格容量的条目不再摆放：见类注释里的容量说明。
                    break;
                }

                var top = GridOrigin.y + (row * CardPitchY);
                var left = GridOrigin.x + (column * CardPitchX);
                m_Cards.Add(CreateCard(panel, items[i], left, top));
            }
        }

        /// <summary>创建一张物品卡：底、描边、图标、名称与稀有度色条。</summary>
        private static CardWidget CreateCard(
            RectTransform panel, ItemDefinition definition, float left, float top)
        {
            var rect = UiFactory.CreateRect(panel, "Card_" + definition.Id);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var backgroundRect = UiFactory.CreateRect(rect, "Fill");
            UiFactory.Stretch(backgroundRect);
            var background = backgroundRect.gameObject.AddComponent<Image>();
            background.sprite = UiSprites.Block;
            background.raycastTarget = false;

            var outlineRect = UiFactory.CreateRect(rect, "Outline");
            UiFactory.Stretch(outlineRect);
            var outline = outlineRect.gameObject.AddComponent<Image>();
            outline.sprite = UiSprites.RingWhite;
            outline.type = Image.Type.Sliced;
            outline.raycastTarget = false;

            var iconRect = UiFactory.CreateAnchored(
                rect, "Icon", UiSprites.Block,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(72f, 72f));
            var icon = iconRect.GetComponent<Image>();
            icon.type = Image.Type.Simple;
            icon.raycastTarget = false;

            var name = UiFactory.CreateLabel(
                rect, string.Empty, new Vector2(6f, 94f), new Vector2(CardWidth - 12f, 46f),
                18f, TextAlignmentOptions.Center, UiPalette.Ink, wrap: true);

            // 稀有度色条：未获得时也显示，作为"还差哪一件"的线索。
            var stripRect = UiFactory.CreateAnchored(
                rect, "Rarity", UiSprites.Block,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 12f), new Vector2(CardWidth - 40f, 8f));

            return new CardWidget
            {
                Definition = definition,
                Rect = rect,
                Background = background,
                Outline = outline,
                Icon = icon,
                RarityStrip = stripRect.GetComponent<Image>(),
                Name = name,
            };
        }

        /// <summary>重画一张卡片的颜色与文字。选中态与点亮态是两套独立的信号。</summary>
        private static void ApplyCardVisual(CardWidget card, bool selected)
        {
            var rarity = card.Definition.Rarity;
            var rarityColor = UiPalette.ItemOutline(rarity);

            if (card.RarityStrip != null)
            {
                card.RarityStrip.color = rarityColor;
            }

            if (card.Discovered)
            {
                card.Background.color = selected ? UiPalette.SlotHover : UiPalette.ItemFill(rarity);
                card.Icon.sprite = card.Definition.Icon != null ? card.Definition.Icon : UiSprites.Block;
                card.Icon.color = rarityColor;
                card.Name.text = card.Definition.DisplayName;
                card.Name.color = UiPalette.Ink;
            }
            else
            {
                // 选中态把炭底向白色提亮一点；直接乘系数会把 Alpha 也乘进去。
                card.Background.color = selected
                    ? Color.Lerp(UnknownFill, Color.white, 0.08f)
                    : UnknownFill;
                card.Icon.sprite = card.Definition.Icon != null ? card.Definition.Icon : UiSprites.Block;
                card.Icon.color = UnknownSilhouette;
                card.Name.text = UnknownName;
                card.Name.color = UiPalette.InkDisabled;
            }

            if (card.Outline != null)
            {
                card.Outline.color = selected
                    ? UiPalette.Teal
                    : card.Discovered ? rarityColor : new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.45f);
            }
        }

        /// <summary>右侧详情栏。</summary>
        private void BuildDetailPane(RectTransform panel)
        {
            var pane = UiFactory.CreatePanel(
                panel, "Detail", DetailSize, UiSprites.CardDim, DetailOrigin);

            var iconRect = UiFactory.CreateAnchored(
                pane, "Icon", UiSprites.Block,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(96f, 96f));
            m_DetailIcon = iconRect.GetComponent<Image>();
            m_DetailIcon.type = Image.Type.Simple;
            m_DetailIcon.raycastTarget = false;

            m_DetailName = UiFactory.CreateLabel(
                pane, string.Empty, new Vector2(20f, 124f), new Vector2(320f, 36f),
                26f, TextAlignmentOptions.Center, UiPalette.Ink);
            m_DetailMeta = UiFactory.CreateLabel(
                pane, string.Empty, new Vector2(20f, 162f), new Vector2(320f, 24f),
                UiPalette.BodySize, TextAlignmentOptions.Center, UiPalette.InkSoft);

            CreateDivider(pane, 196f);
            m_DetailBaseTitle = UiFactory.CreateLabel(
                pane, "基础信息", new Vector2(20f, 204f), new Vector2(320f, 22f),
                UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.Ink);
            m_DetailBase = UiFactory.CreateLabel(
                pane, string.Empty, new Vector2(20f, 230f), new Vector2(320f, 88f),
                UiPalette.BodySize, TextAlignmentOptions.TopLeft, UiPalette.InkSoft);

            m_DetailStatsDivider = CreateDivider(pane, 324f);
            m_DetailStatsTitle = UiFactory.CreateLabel(
                pane, "参数", new Vector2(20f, 332f), new Vector2(320f, 22f),
                UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.Ink);
            m_DetailStats = UiFactory.CreateLabel(
                pane, string.Empty, new Vector2(20f, 358f), new Vector2(320f, 108f),
                UiPalette.BodySize, TextAlignmentOptions.TopLeft, UiPalette.Ink, wrap: true);

            m_DetailDescriptionDivider = CreateDivider(pane, 470f);
            m_DetailDescriptionTitle = UiFactory.CreateLabel(
                pane, "说明", new Vector2(20f, 478f), new Vector2(320f, 22f),
                UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.Ink);
            m_DetailDescription = UiFactory.CreateLabel(
                pane, string.Empty, new Vector2(20f, 504f), new Vector2(320f, 68f),
                UiPalette.SmallSize, TextAlignmentOptions.TopLeft, UiPalette.InkSoft, wrap: true);
        }

        /// <summary>详情栏里的分隔线。返回图像，供"整节收起"时一起隐藏。</summary>
        private static Image CreateDivider(RectTransform pane, float top)
        {
            var rect = UiFactory.CreateAnchored(
                pane, "Divider", UiSprites.Block,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -top), new Vector2(320f, 2f));
            var image = rect.GetComponent<Image>();
            image.color = UiPalette.PaperLine;
            return image;
        }

        /// <summary>页签显示名。</summary>
        private static string ResolveFilterName(CodexFilter filter)
        {
            switch (filter)
            {
                case CodexFilter.Weapon:
                    return "武器";
                case CodexFilter.Ammo:
                    return "弹药";
                case CodexFilter.Armor:
                    return "护甲";
                case CodexFilter.Medical:
                    return "医疗";
                case CodexFilter.Backpack:
                    return "背包";
                case CodexFilter.Misc:
                    return "杂物";
                default:
                    return "全部";
            }
        }
    }
}
