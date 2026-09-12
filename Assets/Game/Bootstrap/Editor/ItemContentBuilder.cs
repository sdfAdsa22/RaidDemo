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

        /// <summary>一条物品定义的数据。</summary>
        private readonly struct ItemSpec
        {
            public ItemSpec(
                string id,
                string displayName,
                ItemCategory category,
                RarityTier rarity,
                int width,
                int height,
                float weightKg,
                int baseValue,
                int maxStack,
                bool canRotate,
                int containerWidth = 0,
                int containerHeight = 0)
            {
                Id = id;
                DisplayName = displayName;
                Category = category;
                Rarity = rarity;
                Width = width;
                Height = height;
                WeightKg = weightKg;
                BaseValue = baseValue;
                MaxStack = maxStack;
                CanRotate = canRotate;
                ContainerWidth = containerWidth;
                ContainerHeight = containerHeight;
            }

            public string Id { get; }

            public string DisplayName { get; }

            public ItemCategory Category { get; }

            public RarityTier Rarity { get; }

            public int Width { get; }

            public int Height { get; }

            public float WeightKg { get; }

            public int BaseValue { get; }

            public int MaxStack { get; }

            public bool CanRotate { get; }

            public int ContainerWidth { get; }

            public int ContainerHeight { get; }

            /// <summary>是否是一个容器。</summary>
            public bool IsContainer
            {
                get { return ContainerWidth > 0 && ContainerHeight > 0; }
            }
        }

        /// <summary>
        /// 初始物品表。
        /// </summary>
        /// <remarks>
        /// <para>覆盖每个分类至少一件，目的是让背包系统的每条分支都能在灰盒里被走到：
        /// 可堆叠的弹药与材料、不可堆叠的装备、占多格并可旋转的长条武器、
        /// 带内部空间但不可堆叠的背包。</para>
        /// <para>重量刻意定得偏高，让灰盒里的物品全捡一遍就能进入重装乃至超重状态，
        /// 否则负重系统在演示时永远看不出效果。</para>
        /// </remarks>
        private static readonly ItemSpec[] s_Specs =
        {
            new ItemSpec("ammo.9x19.standard", "9x19 标准弹", ItemCategory.Ammo, RarityTier.Common,
                1, 1, 0.012f, 8, 120, canRotate: false),
            new ItemSpec("ammo.5.45.standard", "5.45 标准弹", ItemCategory.Ammo, RarityTier.Uncommon,
                1, 1, 0.014f, 25, 90, canRotate: false),
            new ItemSpec("medical.bandage.small", "小绷带", ItemCategory.Medical, RarityTier.Common,
                1, 1, 0.1f, 400, 5, canRotate: false),
            new ItemSpec("medical.kit.field", "野战医疗包", ItemCategory.Medical, RarityTier.Rare,
                1, 2, 0.6f, 9000, 1, canRotate: true),
            new ItemSpec("weapon.pistol.pm", "PM 手枪", ItemCategory.Weapon, RarityTier.Common,
                2, 1, 0.7f, 1800, 1, canRotate: true),
            new ItemSpec("weapon.rifle.ak74", "AK-74 步枪", ItemCategory.Weapon, RarityTier.Uncommon,
                3, 1, 4.2f, 7200, 1, canRotate: true),
            new ItemSpec("armor.helmet.steel", "钢盔", ItemCategory.Helmet, RarityTier.Uncommon,
                2, 2, 2.0f, 4500, 1, canRotate: false),
            new ItemSpec("armor.vest.plate", "防弹背心", ItemCategory.BodyArmor, RarityTier.Rare,
                2, 3, 8.5f, 15000, 1, canRotate: true),
            new ItemSpec("backpack.small", "小型背包", ItemCategory.Backpack, RarityTier.Common,
                3, 3, 1.0f, 1200, 1, canRotate: false, containerWidth: 4, containerHeight: 4),
            new ItemSpec("backpack.raider", "突击背包", ItemCategory.Backpack, RarityTier.Rare,
                4, 4, 3.0f, 12000, 1, canRotate: false, containerWidth: 6, containerHeight: 6),
            new ItemSpec("loot.bolt.copper", "铜螺栓", ItemCategory.Loot, RarityTier.Common,
                1, 1, 0.02f, 600, 20, canRotate: false),
            new ItemSpec("loot.canister.fuel", "燃料罐", ItemCategory.Loot, RarityTier.Rare,
                1, 2, 2.4f, 11000, 1, canRotate: true),
            new ItemSpec("loot.watch.gold", "金表", ItemCategory.Loot, RarityTier.Epic,
                1, 1, 0.05f, 32000, 1, canRotate: false),
        };

        /// <summary>
        /// 生成（或覆盖）全部物品资产与物品目录。
        /// </summary>
        [MenuItem("RaidDemo/生成初始物品资产", priority = 20)]
        public static void Build()
        {
            EnsureFolder();

            var definitions = new List<ItemDefinition>(s_Specs.Length);
            for (var i = 0; i < s_Specs.Length; i++)
            {
                var definition = CreateOrUpdate(s_Specs[i]);
                AttachCombatStats(definition);
                AttachMedicalBehavior(definition);
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
