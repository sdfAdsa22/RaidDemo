using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 批次 3：把外部枪械模型加工成项目层武器预制体。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么选 Quaternius Toon Shooter 的枪，而不是 Kenney Blaster Kit：</b>
    /// Blaster Kit 是科幻造型（圆润的塑料感），与"现代枪战"对不上；
    /// 而 Toon Shooter 的 AK 与 Pistol 正是敌人角色手里那把枪，同一个作者、同一套配色，
    /// 玩家与敌人站在一起时不会有"两种美术风格"的割裂。它已经随角色素材导入，零额外成本。</para>
    ///
    /// <para><b>加工做四件事：</b></para>
    /// <list type="number">
    /// <item><description><b>转向</b>：模型原始朝向是 +X，而项目的"前"是 +Z，
    /// 预制体里直接转正，运行时就不必再为每把枪记一个角度；</description></item>
    /// <item><description><b>缩放到设计长度</b>：步枪 1.05 米、手枪 0.36 米，
    /// 让两把枪与 1.7 米高的角色比例正确；</description></item>
    /// <item><description><b>替换材质</b>：按源材质名映射到项目共用的 4 个 URP 材质，
    /// 两把枪共用一套材质，材质数量因此不随武器数量增长；</description></item>
    /// <item><description><b>补枪口标记</b>：在枪管末端放一个名为 <c>Muzzle</c> 的空节点，
    /// 运行时取它作为弹道起点与枪口火焰位置。</description></item>
    /// </list>
    /// </remarks>
    public static class M7WeaponPrefabBuilder
    {
        /// <summary>武器预制体目录。</summary>
        public const string WeaponsFolder = "Assets/Game/Content/Art/Weapons";

        /// <summary>步枪预制体路径。</summary>
        public const string RiflePrefabPath = WeaponsFolder + "/Weapon_Rifle.prefab";

        /// <summary>手枪预制体路径。</summary>
        public const string PistolPrefabPath = WeaponsFolder + "/Weapon_Pistol.prefab";

        /// <summary>枪械模型来源：正式目录优先，暂存区兜底。</summary>
        private static readonly string[] GunFolders =
        {
            "Assets/Game/Content/External/Quaternius/ToonShooter/Guns/FBX",
            "Assets/Game/Content/External/_Inbox/QuaterniusToonShooter/Guns/FBX"
        };

        /// <summary>枪口标记的名字。运行时按这个名字找，改名要同步改 PlayerWeaponView。</summary>
        private const string MuzzleName = "Muzzle";

        /// <summary>一条武器加工配方。</summary>
        private readonly struct WeaponRecipe
        {
            public WeaponRecipe(string sourceName, string prefabPath, float lengthMeters)
            {
                SourceName = sourceName;
                PrefabPath = prefabPath;
                LengthMeters = lengthMeters;
            }

            /// <summary>源模型文件名（不含扩展名）。</summary>
            public string SourceName { get; }

            /// <summary>目标预制体路径。</summary>
            public string PrefabPath { get; }

            /// <summary>设计长度（米），沿枪身最长方向。</summary>
            public float LengthMeters { get; }
        }

        /// <summary>
        /// 两把枪的设计尺寸。
        /// </summary>
        /// <remarks>
        /// 步枪取 1.25 米：AK 实枪约 0.88 米，这里刻意放大约 40%。
        /// 理由是相机——13 米外、55 度视场下每米只有约 53 像素，而角色有 1.7 米高，
        /// 按真实比例做的枪在俯视角里会被身体与手臂挡住大半，玩家看不出自己拿了什么。
        /// 卡通风格本来就允许夸张比例，把枪做大是这一步最划算的取舍。
        /// 手枪取 0.46 米，同理。
        /// </remarks>
        private static readonly WeaponRecipe[] s_Recipes =
        {
            new WeaponRecipe("AK", RiflePrefabPath, 1.25f),
            new WeaponRecipe("Pistol", PistolPrefabPath, 0.46f),
        };

        /// <summary>源材质名到项目材质（路径、颜色）的映射表。</summary>
        private static readonly Dictionary<string, (string Path, Color Color)> s_MaterialMap =
            new Dictionary<string, (string, Color)>
            {
                { "grey2", (MaterialsFolder + "/M_Weapon_Metal.mat", new Color(0.41f, 0.41f, 0.41f)) },
                { "grey", (MaterialsFolder + "/M_Weapon_Grey.mat", new Color(0.56f, 0.57f, 0.63f)) },
                { "black", (MaterialsFolder + "/M_Weapon_Black.mat", new Color(0.17f, 0.17f, 0.17f)) },
                { "darkgrey", (MaterialsFolder + "/M_Weapon_DarkMetal.mat", new Color(0.29f, 0.29f, 0.29f)) },
                { "wood", (MaterialsFolder + "/M_Weapon_Wood.mat", new Color(0.65f, 0.49f, 0.27f)) },
            };

        /// <summary>项目材质目录（与地形、道具材质放在一起）。</summary>
        private const string MaterialsFolder = BasinTerrainMaterialBuilder.MaterialsFolder;

        /// <summary>菜单入口。</summary>
        [MenuItem("RaidDemo/M7/重建武器预制体")]
        public static void BuildFromMenu()
        {
            Debug.Log(BuildAll());
        }

        /// <summary>重建全部武器预制体。</summary>
        /// <returns>过程摘要。</returns>
        public static string BuildAll()
        {
            M7MaterialLibrary.EnsureFolder(WeaponsFolder);
            var summary = new StringBuilder("[RaidDemo] M7 武器预制体：");
            foreach (var recipe in s_Recipes)
            {
                summary.Append('\n').Append("  ").Append(BuildOne(recipe));
            }

            AssetDatabase.SaveAssets();
            return summary.ToString();
        }

        /// <summary>加工一把枪。</summary>
        private static string BuildOne(WeaponRecipe recipe)
        {
            var model = LoadGun(recipe.SourceName);
            if (model == null)
            {
                return $"{recipe.PrefabPath}（跳过：缺少源模型 {recipe.SourceName}）";
            }

            var container = new GameObject(System.IO.Path.GetFileNameWithoutExtension(recipe.PrefabPath));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, container.transform);

            // 解包后再动 Transform：嵌套预制体上的覆盖可能在保存时被回退（M7-P-01 的教训）。
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // 源模型朝向 +X，项目的"前"是 +Z：绕 Y 轴 -90 度即可对齐。
            instance.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            FitLength(instance, recipe.LengthMeters);
            AlignPivotToGrip(instance);
            ApplyMaterials(instance);
            CreateMuzzle(container, instance);

            var prefab = PrefabUtility.SaveAsPrefabAsset(container, recipe.PrefabPath);
            Object.DestroyImmediate(container);

            return prefab == null
                ? $"{recipe.PrefabPath}（保存失败）"
                : $"{recipe.PrefabPath}（长 {recipe.LengthMeters:F2} 米，材质已统一）";
        }

        /// <summary>载入枪械模型（正式目录优先）。</summary>
        private static GameObject LoadGun(string fileName)
        {
            foreach (var folder in GunFolders)
            {
                var path = $"{folder}/{fileName}.fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model != null)
                {
                    return model;
                }
            }

            return null;
        }

        /// <summary>
        /// 等比缩放到设计长度。
        /// </summary>
        /// <remarks>
        /// 与道具一样用**乘算**而不是覆盖 <c>localScale</c>：FBX 导入器把单位换算写成了根节点缩放，
        /// 覆盖会把这层换算丢掉，模型于是变成设计尺寸的若干倍（M7-P-10）。
        /// 旋转之后长边落在 Z 轴上，因此量的是包围盒的 z 尺寸。
        /// </remarks>
        private static void FitLength(GameObject instance, float lengthMeters)
        {
            if (!TryMeasureBounds(instance, out var bounds))
            {
                return;
            }

            var current = bounds.size.z;
            if (current > 1e-4f)
            {
                instance.transform.localScale = Vector3.Scale(
                    instance.transform.localScale,
                    Vector3.one * (lengthMeters / current));
            }
        }

        /// <summary>
        /// 把模型的高度中心与前后中心对齐到预制体原点。
        /// </summary>
        /// <remarks>
        /// <para>枪的枢轴取"握把所在的原点"而不是几何中心：源模型的枢轴本来就在握把附近，
        /// 保留它，运行时把挂点放在角色手边即可，枪身自然会从手往前伸出去。</para>
        /// <para>只把高度与横向居中对齐：垂直方向贴住持枪高度、水平方向不偏左右，
        /// 纵向（Z）保持源枢轴不动，避免枪身整体前移导致"枪飘在手前面"。</para>
        /// </remarks>
        private static void AlignPivotToGrip(GameObject instance)
        {
            if (!TryMeasureBounds(instance, out var bounds))
            {
                return;
            }

            instance.transform.position += new Vector3(
                -bounds.center.x,
                -bounds.center.y,
                0f);
        }

        /// <summary>在枪管末端创建一个名为 Muzzle 的空节点。</summary>
        private static void CreateMuzzle(GameObject container, GameObject instance)
        {
            if (!TryMeasureBounds(instance, out var bounds))
            {
                return;
            }

            // 包围盒是在世界空间量的，而容器此刻还在世界原点，因此最大值可直接当本地坐标用。
            var muzzle = new GameObject(MuzzleName);
            muzzle.transform.SetParent(container.transform, worldPositionStays: false);
            muzzle.transform.localPosition = new Vector3(0f, 0f, bounds.max.z - instance.transform.position.z);
            muzzle.transform.localRotation = Quaternion.identity;
        }

        /// <summary>按源材质名替换成项目共用材质。</summary>
        private static void ApplyMaterials(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var sources = renderer.sharedMaterials;
                var materials = new Material[sources.Length];
                for (var i = 0; i < sources.Length; i++)
                {
                    materials[i] = ResolveMaterial(sources[i]);
                }

                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>把源材质名映射到项目材质；映射表里没有的一律回落到金属灰。</summary>
        private static Material ResolveMaterial(Material source)
        {
            var key = source != null ? source.name.ToLowerInvariant() : string.Empty;
            if (!s_MaterialMap.TryGetValue(key, out var entry))
            {
                entry = s_MaterialMap["grey2"];
            }

            return M7MaterialLibrary.EnsureLitMaterial(entry.Path, entry.Color, null, 0.12f);
        }

        /// <summary>量取模型（含子物体）的世界包围盒。</summary>
        private static bool TryMeasureBounds(GameObject instance, out Bounds bounds)
        {
            return M7PropPrefabBuilder.TryMeasureBounds(instance, out bounds);
        }
    }
}
