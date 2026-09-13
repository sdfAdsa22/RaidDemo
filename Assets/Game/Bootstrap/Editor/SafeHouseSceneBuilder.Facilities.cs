using TMPro;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 安全屋生成器：设施、墙上说明牌与更衣镜的建造。
    /// </summary>
    /// <remarks>与主文件拆开是为了遵守单文件行数上限：主文件管「场景里放什么」，这里管「每个物件长什么样」。</remarks>
    public static partial class SafeHouseSceneBuilder
    {
        /// <summary>
        /// 写在墙上的操作说明牌。
        /// </summary>
        /// <remarks>
        /// <para><b>它现在是"会互动的物件"，不再只是一段贴在世界里的字：</b>
        /// 文字由 <c>RaidDemo.UI.SafeHouseSignBoard</c> 提供（内容常量只有一份），
        /// 该组件还负责"玩家走近显示提示、按 E 放大成一页说明"。</para>
        ///
        /// <para><b>为什么牌面、文字、组件三者挂在同一个宿主下：</b>牌面是被缩放到 9×3×0.15 的立方体，
        /// 文字若挂在它下面会继承那份非等比缩放，字会被拉得又大又扁（还可能看起来是镜像的）。
        /// 于是这里造一个**等比缩放的宿主**，牌面与文字都是它的子物体：牌面自己带非等比缩放没问题，
        /// 文字只继承宿主的等比缩放。</para>
        /// </remarks>
        private static void CreateSignBoard(GameObject player)
        {
            var host = new GameObject("SignBoard");
            host.transform.position = new Vector3(0f, 2.2f, 8.2f);

            var plate = CreateBox(
                "SignPlate",
                new Vector3(0f, 2.2f, 8.2f),
                new Vector3(9f, 3f, 0.15f),
                host.transform);
            SetColor(plate, new Color(0.12f, 0.13f, 0.16f));

            var canvasHost = new GameObject("SignText", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)canvasHost.transform;
            rect.SetParent(host.transform, worldPositionStays: false);
            rect.sizeDelta = new Vector2(900f, 300f);
            // 牌面厚 0.15（半厚 0.075），文字再往前挪 0.2 米，避免与牌面共面时闪面。
            rect.localPosition = new Vector3(0f, 0f, -0.2f);
            // 不旋转：世界空间画布的正面朝向 +Z（房间在 z 更小的一侧），
            // 从房间里看过去正好是正面。加 180 度反而会看到镜像的文字。
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.01f;

            var textHost = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textHost.transform;
            textRect.SetParent(rect, worldPositionStays: false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textHost.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = RaidDemo.UI.SafeHouseSignBoard.Instructions;
            text.fontSize = 28f;
            text.color = new Color(0.92f, 0.92f, 0.95f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            var sign = host.AddComponent<RaidDemo.UI.SafeHouseSignBoard>();
            var serialized = new SerializedObject(sign);
            serialized.FindProperty("m_Player").objectReferenceValue = player != null ? player.transform : null;
            serialized.FindProperty("m_WallText").objectReferenceValue = text;
            serialized.FindProperty("m_InteractRange").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>设施：仓库（西）、商人（中）、出口（东）沿北墙排开，更衣镜在西墙边。</summary>
        private static void CreateFacilities()
        {
            var root = new GameObject("Facilities").transform;

            CreateStashBox(root, new Vector3(-8f, 0f, 5f));
            CreateMerchantStall(root, new Vector3(-2f, 0f, 5f));
            CreateExitGate(root, new Vector3(5f, 0f, 5f));
            CreateWardrobe(root, new Vector3(-11.2f, 0f, -1.5f));
        }

        /// <summary>
        /// 更衣镜：站在它前面按 E 进入角色选择（M7 批次 4 的局外入口）。
        /// </summary>
        /// <remarks>
        /// <para>位置选在西墙边、离开三个设施与靶场：角色选择是一条**可选**支路，
        /// 找它的人会走过去，不找它的人不会被它挡住出击动线。</para>
        ///
        /// <para>交互复用标准 <c>SafeHouseInteractable</c> 的「最近设施 + 按 E」分发；
        /// <c>SafeHouseBootstrap</c> 收到 <c>Wardrobe</c> 类型后转调角色选择界面。
        /// 生成器只负责把它挂在一个一眼能认出来的物件上。</para>
        /// </remarks>
        private static void CreateWardrobe(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Wardrobe");
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.position = position;

            var frame = CreateBox(
                "Frame",
                position + new Vector3(0f, 1.1f, 0f),
                new Vector3(0.4f, 2.2f, 3f),
                host.transform);
            SetColor(frame, new Color(0.26f, 0.22f, 0.18f));

            // 镜面比镜框薄、朝房间一侧偏出 2 厘米：与镜框共面会闪面（Z-fighting），
            // 而"一面会闪的镜子"看起来像穿模，不像镜子。
            var mirror = CreateBox(
                "Mirror",
                position + new Vector3(0.21f, 1.15f, 0f),
                new Vector3(0.04f, 1.9f, 2.6f),
                host.transform);
            SetColor(mirror, new Color(0.62f, 0.72f, 0.76f));

            AddInteractable(
                host,
                RaidDemo.Presentation.SafeHouseInteractable.Kind.Wardrobe,
                "衣柜",
                position);
        }

        /// <summary>仓库箱：一个带交互标记的箱子。</summary>
        private static void CreateStashBox(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Stash");
            host.transform.SetParent(parent, worldPositionStays: false);
            // 先把宿主摆到设施位置，**再**创建子物体：子物体是按世界坐标造的，
            // 若之后再挪宿主，子物体会被跟着推一次——表现为「设施被推到房间外面」，
            // 而交互标记位置正确、提示照常出现，很难看出是哪一步错了。
            host.transform.position = position;

            var box = CreateBox(
                "StashBox",
                position + new Vector3(0f, 0.7f, 0f),
                new Vector3(2f, 1.4f, 1.2f),
                host.transform);
            SetColor(box, new Color(0.30f, 0.42f, 0.34f));

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Stash, "仓库", position);
        }

        /// <summary>商人摊位：一张柜台 + 一个「人」的占位块。</summary>
        private static void CreateMerchantStall(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Merchant");
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.position = position;

            var counter = CreateBox(
                "Counter",
                position + new Vector3(0f, 0.55f, 0f),
                new Vector3(2.4f, 1.1f, 1f),
                host.transform);
            SetColor(counter, new Color(0.52f, 0.42f, 0.28f));

            var keeper = CreateBox(
                "Keeper",
                position + new Vector3(0f, 1.7f, -0.8f),
                new Vector3(0.6f, 1.8f, 0.6f),
                host.transform);
            SetColor(keeper, new Color(0.85f, 0.72f, 0.2f));

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Merchant, "商人", position);
        }

        /// <summary>出口：一道门框，走近选地图。</summary>
        private static void CreateExitGate(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Exit");
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.position = position;

            var pad = CreateBox(
                "Pad",
                position + new Vector3(0f, 0.03f, 0f),
                new Vector3(3f, 0.06f, 3f),
                host.transform);
            SetColor(pad, new Color(0.18f, 0.62f, 0.38f));

            var left = CreateBox(
                "Post_L",
                position + new Vector3(-1.5f, 1.4f, 0f),
                new Vector3(0.25f, 2.8f, 0.25f),
                host.transform);
            var right = CreateBox(
                "Post_R",
                position + new Vector3(1.5f, 1.4f, 0f),
                new Vector3(0.25f, 2.8f, 0.25f),
                host.transform);
            SetColor(left, new Color(0.16f, 0.18f, 0.20f));
            SetColor(right, new Color(0.16f, 0.18f, 0.20f));

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Exit, "出口", position);
        }
    }
}
