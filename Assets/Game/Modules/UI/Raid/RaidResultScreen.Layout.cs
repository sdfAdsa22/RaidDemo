using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 结算界面的版式（M7 批次 4 换皮）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么与逻辑拆成两个文件：</b>结算是"数据 + 版式"两头都长的界面——
    /// 一半是把 RaidResult 翻译成文字与颜色，一半是二十来个控件的坐标。
    /// 放在一起会顶着单文件行数上限，拆开之后两半都还能一眼读完。</para>
    ///
    /// <para><b>版式：一句话结论 + 一张清单。</b>标题条给出结局与存活时长，紧跟着是
    /// 「带入 / 带出（损失）」两个大数字，之后才是物品清单。清单每行是
    /// 「图标 + 名称 + 数量 + 单价 + 合计」，合计用深金色，让"哪几件值钱"一眼看得出来。</para>
    /// </remarks>
    public sealed partial class RaidResultScreen
    {
        /// <summary>面板尺寸（参考像素）。</summary>
        private static readonly Vector2 PanelSize = new Vector2(980f, 720f);

        /// <summary>内容区左右边距。</summary>
        private const float Padding = 44f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 84f;

        /// <summary>清单第一行的顶边（相对面板）。</summary>
        private const float FirstRowTop = 214f;

        /// <summary>清单行高与行内元素尺寸。</summary>
        private const float RowHeight = 30f;

        private const float RowIconSize = 22f;

        /// <summary>
        /// 物品清单的一行。
        /// </summary>
        /// <remarks>图标与稀有度小方块是**同一位置的两个控件**：有图标时显示图标、
        /// 没有时显示色块。这样"没有图标"不会表现为一行缺一块，而是退回一个同样大小的占位。</remarks>
        private sealed class ResultRow
        {
            public GameObject Root;
            public Image Icon;
            public Image Chip;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Count;
            public TextMeshProUGUI Unit;
            public TextMeshProUGUI Total;
        }

        /// <summary>构建整块界面。</summary>
        private void BuildLayout()
        {
            var canvas = UiFactory.CreateCanvas(transform, "RaidResultCanvas", 310);

            // 遮罩与面板一起显隐（同背包 / 商人 / 暂停）：单独的遮罩会在结算关闭后继续压暗世界。
            var screen = UiFactory.CreateRect(canvas, "Screen");
            UiFactory.Stretch(screen);
            m_Root = screen;
            UiFactory.CreateVeil(screen, "Veil");

            var panel = UiFactory.CreateCenteredPanel(screen, "Panel", PanelSize, UiSprites.Card);

            BuildTitleBar(panel);
            BuildTotals(panel);
            BuildList(panel);
            BuildFooter(panel);
        }

        /// <summary>标题条：结局标题 + 本局摘要。</summary>
        private void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(
                panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            m_TitleLabel = UiFactory.CreateLabel(
                panel,
                "战局结束",
                new Vector2(Padding, 18f),
                new Vector2(560f, 48f),
                34f,
                TextAlignmentOptions.Left,
                UiPalette.Ink);

            m_SummaryLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(PanelSize.x - Padding - 360f, 32f),
                new Vector2(360f, 26f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Right,
                UiPalette.InkSoft);
        }

        /// <summary>带入 / 带出（损失）两个大数字与它们之间的分隔线。</summary>
        private void BuildTotals(RectTransform panel)
        {
            m_BroughtLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Padding, 106f),
                new Vector2(390f, 44f),
                26f,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            m_ExtractedLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Padding + 426f, 106f),
                new Vector2(PanelSize.x - Padding - (Padding + 426f), 44f),
                26f,
                TextAlignmentOptions.Left,
                UiPalette.Ok);

            // 分隔线：一块纯色圆角条。用它而不是画一条 1px 线，
            // 是为了在 1920×1080 与更小的窗口下都保持同样的可见度。
            var divider = UiFactory.CreatePanel(
                panel, "Divider", new Vector2(PanelSize.x - (Padding * 2f), 3f), UiSprites.Block,
                new Vector2(Padding, 166f));
            divider.GetComponent<Image>().color = UiPalette.PaperLine;
        }

        /// <summary>清单表头与九行内容。</summary>
        private void BuildList(RectTransform panel)
        {
            var rowWidth = PanelSize.x - (Padding * 2f);
            m_ListHeaderLabel = UiFactory.CreateLabel(
                panel,
                "带出的物品",
                new Vector2(Padding, 182f),
                new Vector2(rowWidth, 26f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            for (var i = 0; i < MaxItemRows; i++)
            {
                m_ItemRows.Add(BuildRow(panel, rowWidth, FirstRowTop + (i * RowHeight)));
            }
        }

        /// <summary>构建一行清单。</summary>
        private static ResultRow BuildRow(RectTransform panel, float width, float top)
        {
            var root = UiFactory.CreateRect(panel, "ItemRow");
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(Padding, -top);
            root.sizeDelta = new Vector2(width, RowHeight);

            var iconTop = (RowHeight - RowIconSize) * 0.5f;
            var icon = UiFactory.CreateAnchored(
                root, "Icon", UiSprites.Block,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -iconTop),
                new Vector2(RowIconSize, RowIconSize));
            var iconImage = icon.GetComponent<Image>();
            // 22px 的小图比九宫格边界还小，继续用 Sliced 只会把四角挤在一起，
            // 因此小图标与小色块一律走 Simple。
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            var chip = UiFactory.CreateAnchored(
                root, "Chip", UiSprites.Block,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -iconTop),
                new Vector2(RowIconSize, RowIconSize));
            var chipImage = chip.GetComponent<Image>();
            chipImage.type = Image.Type.Simple;
            chipImage.raycastTarget = false;

            var row = new ResultRow
            {
                Root = root.gameObject,
                Icon = iconImage,
                Chip = chipImage,
                Name = UiFactory.CreateLabel(
                    root, string.Empty, new Vector2(34f, 4f), new Vector2(380f, RowHeight - 6f),
                    UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.Ink),
                Count = UiFactory.CreateLabel(
                    root, string.Empty, new Vector2(414f, 4f), new Vector2(76f, RowHeight - 6f),
                    UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft),
                Unit = UiFactory.CreateLabel(
                    root, string.Empty, new Vector2(502f, 4f), new Vector2(168f, RowHeight - 6f),
                    UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft),
                Total = UiFactory.CreateLabel(
                    root, string.Empty, new Vector2(682f, 4f), new Vector2(210f, RowHeight - 6f),
                    UiPalette.BodySize, TextAlignmentOptions.Right, UiPalette.Money),
            };

            root.gameObject.SetActive(false);
            return row;
        }

        /// <summary>清单脚注、任务摘要、去向说明与唯一按钮。</summary>
        private void BuildFooter(RectTransform panel)
        {
            var width = PanelSize.x - (Padding * 2f);

            m_ListFooterLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Padding, FirstRowTop + (MaxItemRows * RowHeight) + 6f),
                new Vector2(width, 24f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            m_QuestLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Padding, 522f),
                new Vector2(width, 26f),
                UiPalette.BodySize,
                TextAlignmentOptions.Left,
                UiPalette.Ok);

            m_NoteLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Padding, 554f),
                new Vector2(width, 48f),
                UiPalette.SmallSize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkSoft,
                wrap: true);

            // 结算后只有一条去处：回安全屋整理与再接任务。
            // 保留单一按钮而不是"再来一局 / 返回安全屋"两条路，避免玩家在结算界面
            // 直接跳过局外准备，也避免两个按钮在视觉上争夺主次。
            m_MenuButton = UiFactory.CreateButton(
                panel,
                "返回安全屋（Esc）",
                new Vector2((PanelSize.x - 340f) * 0.5f, 622f),
                new Vector2(340f, 62f),
                UiButtonKind.Primary);
        }
    }
}
