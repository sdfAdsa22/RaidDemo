using System.Collections.Generic;
using RaidDemo.Data;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 初始物品资产的生成器。
    /// </summary>
    /// <remarks>
    /// <para>用代码生成资产而不是手工在 Inspector 里填，理由有三：</para>
    /// <list type="number">
    /// <item><description>物品表是一份可以被评审、被 diff 的数据。手工填的资产在代码评审里是二进制块，看不出改了什么。</description></item>
    /// <item><description>数值调整只需改这张表再重新生成，不会漏掉某个资产。</description></item>
    /// <item><description>这份表本身就是设计文档的一部分，与 <c>Docs/Modules/02_Inventory.md</c> 一一对应。</description></item>
    /// </list>
    /// <para><b>重新生成会覆盖同名资产</b>，因此不要在生成出来的资产上手工改数值——
    /// 要改数值请改这张表。</para>
    /// </remarks>
    public static partial class ItemContentBuilder
    {
        /// <summary>物品资产的存放目录（仓库相对路径）。</summary>
        private const string ItemFolder = "Assets/Game/Content/Items";

        /// <summary>物品目录资产的路径。</summary>
        private const string CatalogPath = ItemFolder + "/ItemCatalog.asset";

        /// <summary>正式采用的 Kenney Game Icons 图标目录（CC0）。</summary>
        private const string IconFolder =
            "Assets/Game/Content/External/Kenney/GameIcons";

        /// <summary>
        /// 生成（或覆盖）全部物品资产与物品目录。
        /// </summary>
        [MenuItem("RaidDemo/生成初始物品资产", priority = 20)]
        public static void Build()
        {
            EnsureFolder();
            EnsureIconImports();

            var definitions = new List<ItemDefinition>(s_Specs.Length);
            for (var i = 0; i < s_Specs.Length; i++)
            {
                var definition = CreateOrUpdate(s_Specs[i]);
                AttachCombatStats(definition);
                AttachMedicalBehavior(definition);
                AttachCategoryIcon(definition);
                definitions.Add(definition);
            }

            var catalog = LoadOrCreate<ItemCatalog>(CatalogPath);
            var serialized = new SerializedObject(catalog);
            var list = serialized.FindProperty("m_Items");
            list.arraySize = definitions.Count;
            for (var i = 0; i < definitions.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ReportProblems(catalog);
            Debug.Log($"[RaidDemo] 已生成 {definitions.Count} 个物品定义与物品目录：{CatalogPath}");
        }

        /// <summary>
        /// 校验当前物品目录并输出问题列表。
        /// </summary>
        [MenuItem("RaidDemo/校验物品目录", priority = 21)]
        public static void ValidateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[RaidDemo] 找不到物品目录：{CatalogPath}。请先执行「生成初始物品资产」。");
                return;
            }

            ReportProblems(catalog);
        }

        /// <summary>
        /// 给医疗类物品挂上医疗行为。
        /// </summary>
        /// <remarks>
        /// <para>行为做成**独立资产文件**而不是物品资产的子资产：子资产虽然更干净，
        /// 但要靠 <c>AddObjectToAsset</c> 维护，重新生成时容易出现重复挂载，
        /// 而这里只有两件物品，多两个文件的代价远小于出错后的排查成本。</para>
        ///
        /// <para>非医疗物品不挂任何行为，<c>Behavior</c> 保持为 null——
        /// 逻辑层据此判断「这件东西能不能用」。</para>
        /// </remarks>
        private static void AttachMedicalBehavior(ItemDefinition definition)
        {
            var healAmount = 0;
            var duration = 0f;
            switch (definition.Id)
            {
                case "medical.bandage.small":
                    healAmount = 25;
                    duration = 1.5f;
                    break;
                case "medical.kit.field":
                    healAmount = 70;
                    duration = 3f;
                    break;
                default:
                    return;
            }

            var path = $"{ItemFolder}/Behavior_{definition.Id}.asset";
            var behavior = LoadOrCreate<MedicalBehavior>(path);
            behavior.Configure(healAmount, duration);
            EditorUtility.SetDirty(behavior);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("m_Behavior").objectReferenceValue = behavior;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        /// <summary>
        /// 按分类给物品写一张共用图标。
        /// </summary>
        /// <remarks>
        /// <para>五类图标是刻意的粗粒度：武器/弹药/医疗/贵重/任务。逐件配图需要 13 张以上的
        /// 独立素材，而当前 Kenney Game Icons 里并没有对应的枪械、弹药、药品写实图标；
        /// 先用语义接近的符号保证“不同类别一眼能分开”，以后再逐件替换不会影响规则层。</para>
        /// <para>装备类（武器、头盔、护甲、背包）统一用武器图标，表示“可穿戴装备”；
        /// 钥匙属于任务道具，用任务图标。找不到 PNG 时留空，UI 会退回文字显示。</para>
        /// </remarks>
        private static void AttachCategoryIcon(ItemDefinition definition)
        {
            var fileName = ResolveIconFileName(definition.Category);
            var icon = fileName != null
                ? AssetDatabase.LoadAssetAtPath<Sprite>($"{IconFolder}/{fileName}")
                : null;

            var serialized = new SerializedObject(definition);
            var property = serialized.FindProperty("m_Icon");
            property.objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        /// <summary>
        /// 把五张共用图标导入成 Sprite。
        /// </summary>
        /// <remarks>
        /// Kenney 的 PNG 默认会按普通贴图导入，直接 LoadAssetAtPath&lt;Sprite&gt; 会返回 null。
        /// 这里在构建物品资产之前统一改一次导入设置；Point 过滤保证小尺寸图标不被磨糊，
        /// 不压缩则避免 64×64 线稿被 DXT 压出噪点。
        /// </remarks>
        private static void EnsureIconImports()
        {
            var fileNames = new[]
            {
                "icon_weapon.png",
                "icon_ammo.png",
                "icon_medical.png",
                "icon_valuable.png",
                "icon_quest.png",
            };

            for (var i = 0; i < fileNames.Length; i++)
            {
                var path = $"{IconFolder}/{fileNames[i]}";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType == TextureImporterType.Sprite)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }

        /// <summary>分类到图标文件名的映射。</summary>
        private static string ResolveIconFileName(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Weapon:
                case ItemCategory.Helmet:
                case ItemCategory.BodyArmor:
                case ItemCategory.Backpack:
                    return "icon_weapon.png";
                case ItemCategory.Ammo:
                    return "icon_ammo.png";
                case ItemCategory.Medical:
                    return "icon_medical.png";
                case ItemCategory.Loot:
                    return "icon_valuable.png";
                case ItemCategory.Key:
                    return "icon_quest.png";
                default:
                    return null;
            }
        }

        /// <summary>按数据表创建或更新一条物品定义。</summary>
        private static ItemDefinition CreateOrUpdate(ItemSpec spec)
        {
            var path = $"{ItemFolder}/{spec.Id}.asset";
            var definition = LoadOrCreate<ItemDefinition>(path);
            var serialized = new SerializedObject(definition);

            serialized.FindProperty("m_Id").stringValue = spec.Id;
            serialized.FindProperty("m_DisplayName").stringValue = spec.DisplayName;
            serialized.FindProperty("m_Category").enumValueIndex = (int)spec.Category;
            serialized.FindProperty("m_Rarity").enumValueIndex = (int)spec.Rarity;
            serialized.FindProperty("m_Width").intValue = spec.Width;
            serialized.FindProperty("m_Height").intValue = spec.Height;
            serialized.FindProperty("m_WeightKg").floatValue = spec.WeightKg;
            serialized.FindProperty("m_BaseValue").intValue = spec.BaseValue;
            serialized.FindProperty("m_MaxStack").intValue = spec.MaxStack;
            serialized.FindProperty("m_CanRotate").boolValue = spec.CanRotate;
            serialized.FindProperty("m_IsContainer").boolValue = spec.IsContainer;
            serialized.FindProperty("m_Description").stringValue = spec.Description ?? string.Empty;

            if (spec.IsContainer)
            {
                serialized.FindProperty("m_ContainerWidth").intValue = spec.ContainerWidth;
                serialized.FindProperty("m_ContainerHeight").intValue = spec.ContainerHeight;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>加载资产，不存在时创建。</summary>
        private static T LoadOrCreate<T>(string path)
            where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>确保存放目录存在。</summary>
        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(ItemFolder))
            {
                AssetDatabase.CreateFolder("Assets/Game/Content", "Items");
            }
        }

        /// <summary>输出校验结果。</summary>
        private static void ReportProblems(ItemCatalog catalog)
        {
            var problems = catalog.Validate();
            if (problems.Count == 0)
            {
                Debug.Log($"[RaidDemo] 物品目录校验通过，共 {catalog.All.Count} 项。");
                return;
            }

            for (var i = 0; i < problems.Count; i++)
            {
                Debug.LogWarning($"[RaidDemo] 物品目录问题：{problems[i]}");
            }
        }
    }
}
