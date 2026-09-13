using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的「可交互内容」部分：撤离点与战利品容器。
    /// </summary>
    /// <remarks>
    /// <para>与分区布局分开，是因为这两类对象不是掩体而是玩法目标：
    /// 撤离点决定「什么时候能走」，容器决定「值不值得再留一会」。
    /// 调这两样东西的频率远高于调墙的位置，分开之后改起来不必翻整个布局文件。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>
        /// 创建三个撤离点：地面色块 + 四根立柱 + 一道横梁。
        /// </summary>
        /// <remarks>
        /// 立柱与横梁构成一个「大门」的轮廓，这在斜俯视下比单纯的地面色块更好认——
        /// 色块会被箱体或角色挡住，而两米多高的门框永远露在视野里。
        /// 立柱带碰撞体（它们是实体），地面色块不带碰撞体（见 CreateDecoration 的说明）。
        /// </remarks>
        private static void CreateExtractionZones()
        {
            var parent = CreateGroup("Extraction");

            for (var i = 0; i < s_ExtractionZones.Length; i++)
            {
                var (zoneId, displayName, center, groundY, radius) = s_ExtractionZones[i];
                var root = new GameObject($"Extraction_{zoneId:D2}_{displayName}");
                // 撤离点可能位于谷底、也可能位于塬面，因此必须使用布局表给出的地面高度；
                // 若写死为 0，坡顶的三个撤离点会整体沉到土墙里。
                var origin = new Vector3(center.x, groundY, center.y);
                root.transform.position = origin;
                root.transform.SetParent(parent, worldPositionStays: true);

                var marker = root.AddComponent<RaidDemo.Presentation.ExtractionZoneMarker>();
                var serialized = new UnityEditor.SerializedObject(marker);
                serialized.FindProperty("m_ZoneId").intValue = zoneId;
                serialized.FindProperty("m_DisplayName").stringValue = displayName;
                serialized.FindProperty("m_Radius").floatValue = radius;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // 子对象的坐标统一按世界坐标计算：CreateBox 与 CreateDecoration 都以世界坐标
                // 摆放对象，若这里传相对坐标，三个撤离点会全部叠到场景原点附近。
                // 地面色块：绿色代表「安全出口」，与橙色集装箱、灰色建筑形成明确区分。
                CreateDecoration(
                    "Pad",
                    PrimitiveType.Cylinder,
                    origin + new Vector3(0f, 0.02f, 0f),
                    new Vector3(radius * 2f, 0.02f, radius * 2f),
                    new Color(0.18f, 0.62f, 0.38f),
                    root.transform);

                var offset = radius * 0.72f;
                var cornerSigns = new (float X, float Z)[]
                {
                    (-offset, offset),
                    (offset, offset),
                    (-offset, -offset),
                    (offset, -offset),
                };

                for (var corner = 0; corner < cornerSigns.Length; corner++)
                {
                    var sign = cornerSigns[corner];
                    var post = CreateBox(
                        $"Post_{corner + 1}",
                        origin + new Vector3(sign.X, 1.3f, sign.Z),
                        new Vector3(0.22f, 2.6f, 0.22f),
                        root.transform);
                    SetMaterialColor(post, new Color(0.16f, 0.18f, 0.20f));
                }

                // 北侧横梁：把两根北柱连成一个门框
                var lintel = CreateBox(
                    "Lintel",
                    origin + new Vector3(0f, 2.6f, offset),
                    new Vector3(offset * 2f, 0.22f, 0.22f),
                    root.transform);
                SetMaterialColor(lintel, new Color(0.16f, 0.18f, 0.20f));
            }
        }

        /// <summary>创建全部战利品容器（实体箱体 + 场景标记）。</summary>
        /// <remarks>
        /// M7 批次 2 起，普通箱体换成 Toon Shooter 的木箱与纸箱预制体；
        /// 保险柜与开发期测试箱继续用灰盒方块（前者是金属箱，后者本来就只存在于安全屋）。
        /// 无论走哪条路径，都会在同一个对象上挂 <c>LootSpawnPoint</c>，
        /// 因此搜刮逻辑对「箱子长什么样」完全无感。
        /// </remarks>
        private static void CreateLootContainers()
        {
            var parent = CreateGroup("Loot");

            for (var i = 0; i < s_LootPlacements.Length; i++)
            {
                var (definitionId, position, groundY, yaw) = s_LootPlacements[i];
                var name = $"Loot_{i + 1:D2}_{definitionId}";
                var prefabName = ResolveLootPrefab(definitionId);
                var instance = prefabName == null
                    ? null
                    : InstantiateProp(prefabName, new Vector3(position.x, groundY, position.y), yaw, parent);

                GameObject container;
                if (instance != null)
                {
                    instance.name = name;
                    container = instance;
                }
                else
                {
                    var visual = ResolveLootVisual(definitionId);
                    container = CreateBox(
                        name,
                        new Vector3(position.x, groundY + (visual.Size.y * 0.5f), position.y),
                        visual.Size,
                        parent);
                    container.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    SetMaterialColor(container, visual.Color);
                }

                // 标记组件写的是「按哪个定义生成容器」，位置信息由它自己的 Transform 提供。
                var marker = container.AddComponent<RaidDemo.Presentation.LootSpawnPoint>();
                var serialized = new UnityEditor.SerializedObject(marker);
                serialized.FindProperty("m_ContainerDefinitionId").stringValue = definitionId;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// 按容器定义挑选外观预制体；返回 null 表示继续使用灰盒方块。
        /// </summary>
        /// <remarks>
        /// 映射写成「定义 ID → 预制体名」而不是反过来，是为了尊重数据层的主权：
        /// 容器定义是玩法数据（价值、掉落表、搜索时长），外观只是它的一个表现属性。
        /// 将来新增容器类型时，这张表加一行即可，不需要动数据资产。
        /// </remarks>
        private static string ResolveLootPrefab(string definitionId)
        {
            switch (definitionId)
            {
                case "crate.common":
                    return "Prop_Box_Cardboard_B";
                case "crate.ammo":
                    return "Prop_Crate_Wood";
                case "crate.weapon":
                    return "Prop_Crate_Wood";
                case "crate.medical":
                    return "Prop_Box_Cardboard_A";
                default:
                    // safe.rare（保险柜）保持灰盒外观。
                    return null;
            }
        }

        /// <summary>
        /// 按容器定义 ID 决定灰盒箱体的外观。
        /// </summary>
        /// <remarks>
        /// 尺寸与颜色按「箱子是干什么用的」区分：弹药箱矮而方、医疗箱偏白、
        /// 武器架细长、保险柜高而深。灰盒阶段没有美术资源，
        /// 体积与配色就是玩家判断「值不值得跑过去」的唯一信息。
        /// </remarks>
        private static LootVisual ResolveLootVisual(string definitionId)
        {
            switch (definitionId)
            {
                case "crate.ammo":
                    return new LootVisual(new Vector3(1.2f, 0.8f, 0.9f), new Color(0.30f, 0.38f, 0.24f));
                case "crate.medical":
                    return new LootVisual(new Vector3(1.2f, 0.9f, 0.9f), new Color(0.80f, 0.82f, 0.85f));
                case "crate.weapon":
                    return new LootVisual(new Vector3(2.2f, 1.2f, 0.8f), new Color(0.28f, 0.31f, 0.38f));
                case "safe.rare":
                    return new LootVisual(new Vector3(1.2f, 1.6f, 1.2f), new Color(0.22f, 0.22f, 0.25f));
                default:
                    return new LootVisual(new Vector3(1.4f, 1f, 1f), new Color(0.35f, 0.42f, 0.33f));
            }
        }

        /// <summary>灰盒箱体的外观（尺寸与颜色）。</summary>
        private readonly struct LootVisual
        {
            /// <summary>创建外观描述。</summary>
            public LootVisual(Vector3 size, Color color)
            {
                Size = size;
                Color = color;
            }

            /// <summary>箱体尺寸（米）。</summary>
            public Vector3 Size { get; }

            /// <summary>箱体颜色。</summary>
            public Color Color { get; }
        }
    }
}
