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

        /// <summary>设施：仓库（西）、商人（中）、出口（东）、图鉴板（东侧）沿北墙排开，更衣镜在西墙边。</summary>
        /// <returns>图鉴展示板的进度组件，供启动对象在运行时刷新。</returns>
        private static RaidDemo.UI.CodexBoardView CreateFacilities()
        {
            var root = new GameObject("Facilities").transform;

            CreateStashBox(root, new Vector3(-8f, 0f, 5f));
            CreateMerchantStall(root, new Vector3(-2f, 0f, 5f));
            CreateExitGate(root, new Vector3(5f, 0f, 5f));
            CreateWardrobe(root, new Vector3(-11.2f, 0f, -1.5f));
            return CreateCodexBoard(root, new Vector3(9.3f, 0f, 5.2f));
        }

        /// <summary>
        /// 实例化一个项目层道具预制体，作为安全屋设施的陈设。
        /// </summary>
        /// <returns>实例；预制体缺失时返回 null，由调用方回退到原来的灰盒几何。</returns>
        /// <remarks>
        /// 与战局生成器的 <c>InstantiateProp</c> 职责相同，但安全屋没有「谷底为 0」的坐标换算，
        /// 因此这里直接使用世界坐标。回退是硬要求：素材缺失时安全屋仍然必须完整可用，
        /// 这与战局地图的"缺素材也不缺地图"是同一条工程规则。
        /// </remarks>
        private static GameObject InstantiateFacilityProp(
            string prefabName,
            Vector3 position,
            float yawDegrees,
            Transform parent)
        {
            var prefab = M7PropPrefabBuilder.LoadPrefab(prefabName);
            if (prefab == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = prefabName;
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            return instance;
        }

        /// <summary>
        /// 仓库设施：货架 + 柜体 + 木箱 / 油桶 / 宝箱组成的仓储角（M8 开篇替换灰盒）。
        /// </summary>
        /// <remarks>
        /// 交互标记仍在原来的 <paramref name="position"/>：玩家走近最外侧的木箱就能按 E 开仓库。
        /// 货架与柜体贴着北墙摆、木箱组挡在交互点前方，碰撞体由道具预制体自带，
        /// 不再依赖旧灰盒方块——但仍保持"玩家不会穿过设施"这一条手感。
        /// </remarks>
        private static void CreateStashBox(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Stash");
            host.transform.SetParent(parent, worldPositionStays: false);
            // 先把宿主摆到设施位置，**再**创建子物体：子物体是按世界坐标造的，
            // 若之后再挪宿主，子物体会被跟着推一次——表现为「设施被推到房间外面」，
            // 而交互标记位置正确、提示照常出现，很难看出是哪一步错了。
            host.transform.position = position;

            var shelf = InstantiateFacilityProp("Prop_Shelf", position + new Vector3(-1.4f, 0f, 0.6f), 0f, host.transform);
            var cabinet = InstantiateFacilityProp("Prop_Cabinet", position + new Vector3(1.5f, 0f, 0.6f), 0f, host.transform);
            var crate = InstantiateFacilityProp("Prop_SurvivalBox", position + new Vector3(-0.2f, 0f, -1.0f), 20f, host.transform);
            var barrel = InstantiateFacilityProp("Prop_Barrel", position + new Vector3(-1.5f, 0f, -0.9f), 0f, host.transform);
            var chest = InstantiateFacilityProp("Prop_Chest", position + new Vector3(1.0f, 0f, -1.1f), 15f, host.transform);

            // 素材缺失回退：保留原来的绿色储物箱，保证设施仍可见、可交互。
            if (shelf == null && cabinet == null && crate == null && barrel == null && chest == null)
            {
                var box = CreateBox(
                    "StashBox",
                    position + new Vector3(0f, 0.7f, 0f),
                    new Vector3(2f, 1.4f, 1.2f),
                    host.transform);
                SetColor(box, new Color(0.30f, 0.42f, 0.34f));
            }

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Stash, "仓库", position);
        }

        /// <summary>
        /// 商人摊位：两张柜台 + 商人 NPC + 地毯与盆栽（M8 开篇替换灰盒）。
        /// </summary>
        /// <remarks>
        /// 商人 NPC 直接复用玩家角色预制体里的一个未使用角色（Kenney Mini Characters 同一套骨架与动画），
        /// 站在柜台后方、面向房间，默认播放 Idle。这样不需要单独维护 NPC 模型与动画控制器；
        /// 角色缺失时回退为原来的黄色占位块。
        /// </remarks>
        private static void CreateMerchantStall(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Merchant");
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.position = position;

            var counterLeft = InstantiateFacilityProp("Prop_Counter", position + new Vector3(-1.1f, 0f, 0.4f), 0f, host.transform);
            var counterRight = InstantiateFacilityProp("Prop_Counter", position + new Vector3(1.1f, 0f, 0.4f), 0f, host.transform);
            var rug = InstantiateFacilityProp("Prop_Rug", position + new Vector3(0f, 0.02f, -1.6f), 0f, host.transform);
            var sideTable = InstantiateFacilityProp("Prop_SideTable", position + new Vector3(-2.7f, 0f, 0.5f), 0f, host.transform);
            var plant = InstantiateFacilityProp("Prop_Plant", position + new Vector3(2.8f, 0f, 0.7f), 0f, host.transform);

            if (counterLeft == null && counterRight == null)
            {
                // 素材缺失回退：旧柜台。
                var counter = CreateBox(
                    "Counter",
                    position + new Vector3(0f, 0.55f, 0f),
                    new Vector3(2.4f, 1.1f, 1f),
                    host.transform);
                SetColor(counter, new Color(0.52f, 0.42f, 0.28f));
            }

            CreateMerchantKeeper(host.transform, position + new Vector3(0f, 0f, 1.7f));

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Merchant, "商人", position);
        }

        /// <summary>
        /// 商人 NPC：复用玩家角色预制体里的女队员 C，站在柜台后方面向房间。
        /// </summary>
        /// <remarks>
        /// 角色预制体只包含模型、Animator 与移动动画绑定，不含任何玩家逻辑，
        /// 因此作为静态 NPC 使用不会获得输入、移动或战斗行为；缺失时回退为黄色占位块。
        /// </remarks>
        private static void CreateMerchantKeeper(Transform parent, Vector3 position)
        {
            const string characterPath =
                "Assets/Game/Content/Art/Characters/Player/PlayerCharacter_female_c.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            if (prefab != null)
            {
                var keeper = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                keeper.name = "MerchantKeeper";
                keeper.transform.position = position;
                // 玩家从南侧（z 更小）走过来，NPC 面向 -Z 才能与玩家对视。
                keeper.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                return;
            }

            var fallback = CreateBox(
                "Keeper",
                position + new Vector3(0f, 0.9f, 0f),
                new Vector3(0.6f, 1.8f, 0.6f),
                parent);
            SetColor(fallback, new Color(0.85f, 0.72f, 0.2f));
        }

        /// <summary>
        /// 出口：绿色地垫 + 金属门框（M8 开篇替换两根灰色立柱）。
        /// </summary>
        /// <remarks>
        /// 门框不给碰撞体：玩家要能走进门洞并按 E 选地图；原来的立柱有碰撞体，
        /// 换成门框后"穿过门框"才是符合直觉的行为。
        /// </remarks>
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

            var doorway = InstantiateFacilityProp("Prop_MetalDoorway", position, 0f, host.transform);
            if (doorway == null)
            {
                // 素材缺失回退：旧立柱。
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
            }

            AddInteractable(host, RaidDemo.Presentation.SafeHouseInteractable.Kind.Exit, "出口", position);
        }

        /// <summary>
        /// 衣柜：柜体 + 镜面（M8 开篇替换深色方块镜框）。
        /// </summary>
        /// <remarks>
        /// 柜体朝东（面向房间），镜面比柜体前表面再偏出 2 厘米，避免共面闪面；
        /// 柜体缺失时保留原来的深色镜框。
        /// </remarks>
        private static void CreateWardrobe(Transform parent, Vector3 position)
        {
            var host = new GameObject("Facility_Wardrobe");
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.position = position;

            // 柜体旋转 90 度：模型正面朝向 +X，正好面向房间中央。
            var cabinet = InstantiateFacilityProp("Prop_Cabinet", position, 90f, host.transform);
            if (cabinet == null)
            {
                var frame = CreateBox(
                    "Frame",
                    position + new Vector3(0f, 1.1f, 0f),
                    new Vector3(0.4f, 2.2f, 3f),
                    host.transform);
                SetColor(frame, new Color(0.26f, 0.22f, 0.18f));
            }

            // 镜面比柜体前表面（约 +0.3 米）再偏出 2 厘米，避免与柜门共面闪面。
            var mirror = CreateBox(
                "Mirror",
                position + new Vector3(0.33f, 1.1f, 0f),
                new Vector3(0.04f, 1.4f, 0.9f),
                host.transform);
            SetColor(mirror, new Color(0.62f, 0.72f, 0.76f));

            AddInteractable(
                host,
                RaidDemo.Presentation.SafeHouseInteractable.Kind.Wardrobe,
                "衣柜",
                position);
        }
    }
}
