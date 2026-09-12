using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 把外部道具模型加工成项目层预制体（<c>Art/Props/</c>）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不让场景直接引用 External 里的模型：</b>这是 M7 素材技术标准里定的规则。
    /// 场景只引用项目层预制体，将来换素材（比如把 Kenney 集装箱换成别的包）只需要改预制体内部的引用，
    /// 场景与代码零改动；反过来，若场景里散落着几十个对 External 模型的直接引用，
    /// 换素材就必须逐个替换，且很容易漏掉几个。</para>
    ///
    /// <para><b>统一加工三件事：</b></para>
    /// <list type="number">
    /// <item><description>缩放：按包围盒高度等比缩放到设计尺寸，不同资源包的导出比例因此被抹平；</description></item>
    /// <item><description>落地与居中：把模型最低点对齐到预制体原点、水平中心对齐到原点，
    /// 场景里就能用「底面中心」直接摆位，不必为每个模型记一套偏移；</description></item>
    /// <item><description>材质：整包共用一个项目 URP 材质，保留原贴图，避免粉色材质与材质爆炸。</description></item>
    /// </list>
    ///
    /// <para><b>碰撞体选项：</b>集装箱、木箱这类实体需要碰撞体；围栏与装饰不给碰撞体，
    /// 因为它们的阻挡由场景里已有的灰盒代理承担——这样「只换外观、不改碰撞与导航」这条铁律
    /// 在结构上就无法被破坏。</para>
    /// </remarks>
    public static class M7PropPrefabBuilder
    {
        /// <summary>项目层预制体目录。</summary>
        public const string PropsFolder = "Assets/Game/Content/Art/Props";

        /// <summary>道具来源：决定用哪个加载器与哪份共享材质。</summary>
        private enum SourceKind
        {
            /// <summary>Kenney City Kit (Industrial)：集装箱与工业点缀。</summary>
            CityKit,

            /// <summary>Quaternius Toon Shooter：木箱、纸箱、围栏。</summary>
            ToonShooter
        }

        /// <summary>一个道具的加工配方。</summary>
        private readonly struct PropRecipe
        {
            public PropRecipe(
                SourceKind source,
                string sourceName,
                string prefabName,
                float targetHeight,
                bool addCollider,
                bool occluder = false)
            {
                Source = source;
                SourceName = sourceName;
                PrefabName = prefabName;
                TargetHeight = targetHeight;
                AddCollider = addCollider;
                Occluder = occluder;
            }

            public SourceKind Source { get; }

            public string SourceName { get; }

            public string PrefabName { get; }

            public float TargetHeight { get; }

            public bool AddCollider { get; }

            /// <summary>是否使用带透视孔的材质（会挡住相机的大件）。</summary>
            public bool Occluder { get; }
        }

        /// <summary>
        /// 全部道具配方。
        /// </summary>
        /// <remarks>
        /// 高度按 M7 技术标准 2.1 节：集装箱 2.59 米、小道具 0.5~1.4 米、围栏 2.2 米左右。
        /// 数值写在这里而不是散落在场景里，是为了让「场景道具到底多大」只有一个答案。
        /// </remarks>
        private static readonly PropRecipe[] s_Recipes =
        {
            // 集装箱三型：海运标准箱比例（长 6.06 × 宽 2.44 × 高 2.59），三种涂装做视觉变化
            new PropRecipe(SourceKind.CityKit, "shipping-container-a", "Prop_Container_A", 2.59f, true, occluder: true),
            new PropRecipe(SourceKind.CityKit, "shipping-container-b", "Prop_Container_B", 2.59f, true, occluder: true),
            new PropRecipe(SourceKind.CityKit, "shipping-container-c", "Prop_Container_C", 2.59f, true, occluder: true),

            // 工业点缀：储罐与水塔作为堆场地标，刻意做高，让玩家在俯视下也能定位
            new PropRecipe(SourceKind.CityKit, "detail-tank", "Prop_Tank", 1.6f, true, occluder: true),
            new PropRecipe(SourceKind.CityKit, "water-tower", "Prop_WaterTower", 7.5f, true, occluder: true),

            // 搜刮容器：木箱与纸箱（医疗箱用纸箱的浅色观感，武器架用木箱加长摆放）
            new PropRecipe(SourceKind.ToonShooter, "Crate", "Prop_Crate_Wood", 1.0f, true),
            new PropRecipe(SourceKind.ToonShooter, "CardboardBoxes_1", "Prop_Box_Cardboard_A", 0.6f, true),
            new PropRecipe(SourceKind.ToonShooter, "CardboardBoxes_2", "Prop_Box_Cardboard_B", 1.0f, true),
            new PropRecipe(SourceKind.ToonShooter, "Pallet", "Prop_Pallet", 0.19f, false),

            // 围栏与掩体：围栏不给碰撞体（阻挡由灰盒代理承担），沙袋掩体给碰撞体。
            // 目标高度大多等于模型原始高度：Quaternius 的道具本来就是按米建模的，
            // 强行缩放反而会让 1.05 米高的木栅栏变成一堵墙。
            new PropRecipe(SourceKind.ToonShooter, "Fence", "Prop_Fence_Wood", 1.05f, false),
            new PropRecipe(SourceKind.ToonShooter, "MetalFence", "Prop_Fence_Metal", 2.4f, false),
            new PropRecipe(SourceKind.ToonShooter, "SackTrench", "Prop_Barrier", 1.21f, true),

            // 远景工业建筑：只在地图外圈做天际线，不给碰撞体，也不参与导航烘焙
            new PropRecipe(SourceKind.ToonShooter, "Structure_2", "Prop_Warehouse_A", 7.79f, false, occluder: true),
            new PropRecipe(SourceKind.ToonShooter, "Structure_4", "Prop_Warehouse_B", 7.66f, false, occluder: true),
        };

        /// <summary>共享材质路径：一个资源包一个材质，满足「全场材质数量收敛」的要求。</summary>
        private static readonly Dictionary<SourceKind, string> s_MaterialPaths = new Dictionary<SourceKind, string>
        {
            { SourceKind.CityKit, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_KenneyIndustrial.mat" },
            { SourceKind.ToonShooter, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_ToonShooterProps.mat" },
        };

        /// <summary>菜单入口：重建全部道具预制体。</summary>
        [MenuItem("RaidDemo/M7/重建场景道具预制体")]
        public static void BuildFromMenu()
        {
            Debug.Log(BuildAll());
        }

        /// <summary>
        /// 重建全部道具预制体。
        /// </summary>
        /// <returns>过程摘要（可直接打印到控制台）。</returns>
        public static string BuildAll()
        {
            M7MaterialLibrary.EnsureFolder(PropsFolder);
            M7SceneAssetResolver.BeginBuild();

            var summary = new StringBuilder("[RaidDemo] M7 道具预制体：");
            foreach (var recipe in s_Recipes)
            {
                var path = BuildOne(recipe);
                summary.Append('\n').Append("  ").Append(path ?? $"{recipe.PrefabName}（跳过：缺少源模型）");
            }

            AssetDatabase.SaveAssets();
            return summary.ToString();
        }

        /// <summary>按预制体名载入项目层道具预制体（找不到返回 null）。</summary>
        public static GameObject LoadPrefab(string prefabName)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsFolder}/{prefabName}.prefab");
        }

        /// <summary>加工一个道具。</summary>
        private static string BuildOne(PropRecipe recipe)
        {
            var model = recipe.Source == SourceKind.CityKit
                ? M7SceneAssetResolver.LoadModel(CityKitLookupFolders, recipe.SourceName)
                : M7SceneAssetResolver.LoadToonShooterProp(recipe.SourceName);
            if (model == null)
            {
                return null;
            }

            var path = $"{PropsFolder}/{recipe.PrefabName}.prefab";
            var container = new GameObject(recipe.PrefabName);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "Model";
            instance.transform.SetParent(container.transform, worldPositionStays: false);

            // 解包后再改缩放：嵌套预制体实例上的覆盖可能在保存时被回退（M7-P-01 的教训）。
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            FitAndCenter(instance, recipe.TargetHeight);
            ApplyMaterial(recipe.Source, recipe.PrefabName, instance, recipe.Occluder);
            if (recipe.AddCollider)
            {
                AddBoxCollider(container, instance);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(container, path);
            Object.DestroyImmediate(container);
            return path + (prefab == null ? "（保存失败）" : string.Empty);
        }

        /// <summary>肯尼工业套件的候选目录（与解析器保持一致）。</summary>
        private static string[] CityKitLookupFolders =>
            new[]
            {
                "Assets/Game/Content/External/Kenney/CityKitIndustrial",
                "Assets/Game/Content/External/_Inbox/KenneyCityKitIndustrial"
            };

        /// <summary>等比缩放到目标高度，并把最低点、水平中心对齐到预制体原点。</summary>
        /// <remarks>
        /// <para><b>必须乘算而不是覆盖 localScale：</b>FBX 导入器会把「文件单位换算」写成模型根节点的缩放
        /// （Kenney 的模型尤其明显：一整个集装箱在模型空间里只有 0.35 个单位高）。
        /// 直接写 <c>localScale = 目标高 / 当前高</c> 会把这份换算丢掉，
        /// 于是模型在预制体里变成期望尺寸的 3.7 倍——渲染出来像一栋楼，
        /// 碰撞体也跟着变成 10×22 米，把整片区域堵死。</para>
        /// <para>乘上缩放系数后，导入器给的换算与我们的尺寸校正同时成立。</para>
        /// </remarks>
        private static void FitAndCenter(GameObject instance, float targetHeight)
        {
            if (!TryGetBounds(instance, out var bounds))
            {
                return;
            }

            var scale = bounds.size.y > 1e-4f ? targetHeight / bounds.size.y : 1f;
            instance.transform.localScale = Vector3.Scale(
                instance.transform.localScale,
                Vector3.one * scale);

            if (!TryGetBounds(instance, out bounds))
            {
                return;
            }

            instance.transform.position += new Vector3(
                -bounds.center.x,
                -bounds.min.y,
                -bounds.center.z);
        }

        /// <summary>给预制体根节点加一个贴合模型包围盒的碰撞体。</summary>
        private static void AddBoxCollider(GameObject container, GameObject instance)
        {
            if (!TryGetBounds(instance, out var bounds))
            {
                return;
            }

            var collider = container.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);
            collider.size = bounds.size;
        }

        /// <summary>
        /// 重赋项目 URP 材质，并按素材的两种形态选择共享策略。
        /// </summary>
        /// <remarks>
        /// <para><b>贴图型素材（Kenney 工业套件）：</b>整包共用一张色板贴图，颜色信息全在贴图里，
        /// 因此所有模型共享一个材质即可——材质数量少、合批机会多。</para>
        ///
        /// <para><b>纯色型素材（Quaternius 环境道具）：</b>每个模型一个纯色材质（木材是棕的、纸箱是牛皮纸色、
        /// 建筑是灰的），没有贴图。这类必须逐模型建材质：若共用一份，所有道具都会变成同一种颜色；
        /// 若沿用白色，就会得到一排惨白的模型——这是把「贴图优先」写成硬规则时最容易踩的坑。</para>
        /// </remarks>
        private static void ApplyMaterial(SourceKind source, string prefabName, GameObject instance, bool occluder)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }

            var texture = FindFirstTexture(renderers);
            var color = FindFirstColor(renderers);
            // 预制体名已经带 Prop_ 前缀，材质名里去掉它，避免出现 M_Prop_Prop_Barrier 这种叠词。
            var shortName = prefabName.StartsWith("Prop_") ? prefabName.Substring("Prop_".Length) : prefabName;
            var materialPath = texture != null
                ? s_MaterialPaths[source]
                : $"{BasinTerrainMaterialBuilder.MaterialsFolder}/M_Prop_{shortName}.mat";
            var baseColor = texture != null ? Color.white : color;

            // 每次都走创建/更新：贴图可能第一次生成时还没导入成功，
            // 若只在「材质不存在」时写入，之后就会一直保留没有贴图的版本。
            var material = M7MaterialLibrary.EnsureLitMaterial(materialPath, baseColor, texture, 0.08f, occluder);
            if (material == null)
            {
                return;
            }

            foreach (var renderer in renderers)
            {
                // 整份替换而不是只改第一个槽位：多子网格模型（例如山石是"岩石 + 草"）会保留其余槽位的原材质。
                var slots = renderer.sharedMaterials.Length;
                var materials = new Material[slots];
                for (var i = 0; i < slots; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>从模型自带的材质里取第一张基础贴图（图集型素材整包共用一张）。</summary>
        private static Texture2D FindFirstTexture(Renderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                var source = renderer.sharedMaterial;
                if (source != null && source.mainTexture is Texture2D texture)
                {
                    return texture;
                }
            }

            return null;
        }

        /// <summary>从模型自带的材质里取基础色；取不到时用白色（后续由调用方决定是否回退）。</summary>
        private static Color FindFirstColor(Renderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                var source = renderer.sharedMaterial;
                if (source == null)
                {
                    continue;
                }

                var color = source.HasProperty("_BaseColor")
                    ? source.GetColor("_BaseColor")
                    : source.color;
                if (color.maxColorComponent > 0.02f)
                {
                    return color;
                }
            }

            return Color.white;
        }

        /// <summary>
        /// 求一个对象（含子物体）全部渲染器的世界包围盒。
        /// </summary>
        /// <remarks>公开给场景生成器使用：摆放大块装饰（悬崖瓦片、远景山石）时，
        /// 需要先量出模型真实尺寸才能算出间距与缩放，写死数字会在换素材时全部失效。</remarks>
        public static bool TryMeasureBounds(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        /// <summary>内部别名，保持配方代码可读。</summary>
        private static bool TryGetBounds(GameObject instance, out Bounds bounds)
        {
            return TryMeasureBounds(instance, out bounds);
        }
    }
}
