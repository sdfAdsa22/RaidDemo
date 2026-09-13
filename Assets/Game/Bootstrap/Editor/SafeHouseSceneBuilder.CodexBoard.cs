using TMPro;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 安全屋生成器：图鉴展示板的建造（M8 批次 1）。
    /// </summary>
    /// <remarks>与主设施文件拆开是为了遵守单文件行数上限，也因为这个设施自带一小段文字逻辑。</remarks>
    public static partial class SafeHouseSceneBuilder
    {
        /// <summary>
        /// 图鉴展示板：两根立柱 + 木牌 + 三张"物品卡"装饰，牌面写着收集进度。
        /// </summary>
        /// <param name="parent">设施根节点。</param>
        /// <param name="position">牌面中心在地面上的投影位置。</param>
        /// <returns>进度组件；装配层把它写进 <c>SafeHouseBootstrap</c> 的序列化引用，运行时刷新。</returns>
        /// <remarks>
        /// <para>牌面朝南（房间在 z 更小的一侧），与墙上说明牌同一套朝向约定：
        /// 世界空间画布不旋转时正面朝向 +Z，从房间里看过去正好是正面。</para>
        /// <para>三张小卡片是纯装饰：用灰 / 绿 / 蓝三色对应"未点亮 / 精良 / 稀有"的观感，
        /// 让牌面在没有文字的时候也读得出"这是一块收集图鉴"。</para>
        /// </remarks>
        private static RaidDemo.UI.CodexBoardView CreateCodexBoard(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Codex");
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.position = position;

            var woodColor = new Color(0.42f, 0.31f, 0.21f);
            var boardColor = new Color(0.88f, 0.83f, 0.72f);

            var postLeft = CreateBox(
                "Post_L",
                position + new Vector3(-1.62f, 1.3f, 0f),
                new Vector3(0.16f, 2.6f, 0.16f),
                host.transform);
            SetColor(postLeft, woodColor);

            var postRight = CreateBox(
                "Post_R",
                position + new Vector3(1.62f, 1.3f, 0f),
                new Vector3(0.16f, 2.6f, 0.16f),
                host.transform);
            SetColor(postRight, woodColor);

            var board = CreateBox(
                "Board",
                position + new Vector3(0f, 1.85f, 0f),
                new Vector3(3.6f, 2.1f, 0.12f),
                host.transform);
            SetColor(board, boardColor);

            // 三张装饰卡片：贴在牌面南侧（朝向房间），避免与牌面共面闪面。
            var cardColors = new[]
            {
                new Color(0.72f, 0.70f, 0.65f),
                new Color(0.48f, 0.76f, 0.45f),
                new Color(0.47f, 0.65f, 0.85f),
            };
            for (var i = 0; i < cardColors.Length; i++)
            {
                var card = CreateBox(
                    $"Card_{i + 1}",
                    position + new Vector3((i - 1) * 0.62f, 1.12f, -0.09f),
                    new Vector3(0.45f, 0.45f, 0.03f),
                    host.transform);
                SetColor(card, cardColors[i]);
            }

            var progressView = CreateCodexBoardText(host.transform);
            AddInteractable(
                host,
                RaidDemo.Presentation.SafeHouseInteractable.Kind.CodexBoard,
                "图鉴展示板",
                position);
            return progressView;
        }

        /// <summary>
        /// 牌面文字：标题「收集图鉴」与一行收集进度。
        /// </summary>
        /// <returns>挂在进度标签上的显示组件，供运行时更新。</returns>
        /// <remarks>与墙上说明牌同一套做法：等比缩放的宿主 + 世界空间画布，
        /// 文字不继承任何非等比缩放。</remarks>
        private static RaidDemo.UI.CodexBoardView CreateCodexBoardText(Transform host)
        {
            var canvasHost = new GameObject("BoardText", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)canvasHost.transform;
            rect.SetParent(host, worldPositionStays: false);
            rect.sizeDelta = new Vector2(340f, 200f);
            // 牌面厚 0.12（南侧表面约 -0.06），文字再往南挪 0.14 米，避免与牌面共面时闪面。
            rect.localPosition = new Vector3(0f, 1.85f, -0.14f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.01f;

            var title = CreateBoardLabel(rect, "收集图鉴", new Vector2(0f, 52f), 40f, 0.18f);
            title.fontStyle = FontStyles.Bold;
            // 进度行贴在中线略上方：再往下会和牌面底部的三张装饰卡片叠在一起。
            var progress = CreateBoardLabel(rect, "已收集 0 / 0", new Vector2(0f, 6f), 26f, 0.36f);

            // 显示组件挂在进度标签自身：运行时只改这一行文字。
            var view = progress.gameObject.AddComponent<RaidDemo.UI.CodexBoardView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("m_ProgressText").objectReferenceValue = progress;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        /// <summary>世界空间画布里的一个居中标签。</summary>
        private static TextMeshProUGUI CreateBoardLabel(
            RectTransform canvasRect, string content, Vector2 offset, float fontSize, float inkValue)
        {
            var labelHost = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)labelHost.transform;
            rect.SetParent(canvasRect, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(320f, 64f);

            var text = labelHost.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = fontSize;
            text.color = new Color(inkValue, inkValue, inkValue);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
    }
}
