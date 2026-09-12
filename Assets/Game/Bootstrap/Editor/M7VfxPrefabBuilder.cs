using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 批次 3：用 Kenney 粒子贴图生成项目层特效预制体。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是"贴图 + 代码生成预制体"而不是直接下载粒子包里的示例场景：</b>
    /// 素材包给的是 80 张透明贴图，本身不带任何粒子系统；而四个特效（枪口火焰、火花、
    /// 尘土、血雾）的寿命、速度、数量、混合模式正是手感所在，必须按本项目的相机距离
    /// （13 米、俯角 62 度）自己定，照抄任何示例都不会合适。</para>
    /// <para>生成的预制体落在 <c>Art/Vfx/</c>，运行时由 <c>CombatVfxDirector</c> 池化播放。
    /// 把参数写在构建器里而不是让人手点 Inspector，是为了"重新生成"能回到同一种观感——
    /// 手上改过的参数会在下次重建时被覆盖，这是刻意的：特效参数的唯一来源就是本文件。</para>
    /// </remarks>
    public static class M7VfxPrefabBuilder
    {
        /// <summary>特效预制体目录。</summary>
        public const string VfxFolder = "Assets/Game/Content/Art/Vfx";

        /// <summary>粒子贴图目录（随仓库提交的正式目录）。</summary>
        private const string TextureFolder = "Assets/Game/Content/External/Kenney/ParticlePack/PNG";

        /// <summary>材质目录。</summary>
        private const string MaterialsFolder = BasinTerrainMaterialBuilder.MaterialsFolder;

        /// <summary>URP 粒子着色器。</summary>
        private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";

        /// <summary>一个粒子团的参数。</summary>
        private readonly struct Puff
        {
            public Puff(
                string name,
                string texture,
                Color color,
                bool additive,
                float duration,
                float lifeMin,
                float lifeMax,
                float speedMin,
                float speedMax,
                float sizeMin,
                float sizeMax,
                float gravity,
                float coneAngle,
                int count)
            {
                Name = name;
                Texture = texture;
                Color = color;
                Additive = additive;
                Duration = duration;
                LifeMin = lifeMin;
                LifeMax = lifeMax;
                SpeedMin = speedMin;
                SpeedMax = speedMax;
                SizeMin = sizeMin;
                SizeMax = sizeMax;
                Gravity = gravity;
                ConeAngle = coneAngle;
                Count = count;
            }

            public string Name { get; }

            public string Texture { get; }

            public Color Color { get; }

            public bool Additive { get; }

            public float Duration { get; }

            public float LifeMin { get; }

            public float LifeMax { get; }

            public float SpeedMin { get; }

            public float SpeedMax { get; }

            public float SizeMin { get; }

            public float SizeMax { get; }

            public float Gravity { get; }

            public float ConeAngle { get; }

            public int Count { get; }
        }

        /// <summary>
        /// 四个特效的参数。
        /// </summary>
        /// <remarks>
        /// 尺寸与速度都按"相机在 13 米外、俯角 62 度"来定：再小就看不见，
        /// 再大就会盖住角色。枪口火焰刻意只有 0.05 秒——它是瞬时光效，
        /// 停留超过 0.1 秒在斜俯视下会变成一团挂在枪口的橙色污渍。
        /// <para>尺寸的量化依据：13 米外的 55 度视场在 720 行画面里约 53 像素/米，
        /// 因此 0.1 米的粒子只有 5 像素——肉眼几乎看不见。所有尺寸都按"至少 15 像素"反推，
        /// 即最小不低于 0.3 米。</para>
        /// </remarks>
        private static readonly (string Prefab, Puff Root, Puff Child)[] s_Recipes =
        {
            (
                "Vfx_MuzzleFlash",
                new Puff("Flash", "muzzle_03", new Color(1f, 0.78f, 0.35f), true,
                    0.08f, 0.055f, 0.095f, 0.20f, 0.45f, 0.55f, 0.80f, 0f, 6f, 2),
                new Puff("Smoke", "smoke_01", new Color(0.75f, 0.75f, 0.75f, 0.45f), false,
                    0.12f, 0.30f, 0.60f, 0.35f, 0.70f, 0.20f, 0.35f, -0.05f, 25f, 3)
            ),
            (
                "Vfx_ImpactSpark",
                new Puff("Spark", "spark_02", new Color(1f, 0.85f, 0.40f), true,
                    0.06f, 0.15f, 0.35f, 3.0f, 6.0f, 0.14f, 0.26f, 1.8f, 32f, 12),
                default
            ),
            (
                "Vfx_ImpactDust",
                new Puff("Dust", "smoke_05", new Color(0.93f, 0.89f, 0.82f, 0.70f), false,
                    0.10f, 0.35f, 0.70f, 0.80f, 1.60f, 0.34f, 0.58f, -0.10f, 40f, 7),
                default
            ),
            (
                "Vfx_ImpactFlesh",
                new Puff("Flesh", "smoke_05", new Color(0.72f, 0.13f, 0.13f, 0.75f), false,
                    0.08f, 0.20f, 0.40f, 1.2f, 2.6f, 0.16f, 0.30f, 0.8f, 30f, 9),
                default
            ),
        };

        /// <summary>菜单入口。</summary>
        [MenuItem("RaidDemo/M7/重建特效预制体")]
        public static void BuildFromMenu()
        {
            Debug.Log(BuildAll());
        }

        /// <summary>重建全部特效预制体。</summary>
        /// <returns>过程摘要。</returns>
        public static string BuildAll()
        {
            M7MaterialLibrary.EnsureFolder(VfxFolder);
            ConfigureParticleTextureImporters();

            var summary = new StringBuilder("[RaidDemo] M7 特效预制体：");
            foreach (var (prefabName, root, child) in s_Recipes)
            {
                var path = $"{VfxFolder}/{prefabName}.prefab";
                var container = BuildContainer(prefabName, root, child);
                var prefab = PrefabUtility.SaveAsPrefabAsset(container, path);
                Object.DestroyImmediate(container);
                summary.Append('\n').Append("  ").Append(prefab == null ? path + "（保存失败）" : path);
            }

            AssetDatabase.SaveAssets();
            return summary.ToString();
        }

        /// <summary>按配方搭出预制体内容。</summary>
        private static GameObject BuildContainer(string prefabName, Puff root, Puff child)
        {
            var container = new GameObject(prefabName);
            var rootSystem = CreatePuff(root, container);
            if (child.Count > 0)
            {
                var childHost = new GameObject(child.Name);
                childHost.transform.SetParent(rootSystem.transform, worldPositionStays: false);
                CreatePuff(child, childHost);
            }

            return container;
        }

        /// <summary>
        /// 在一个已存在的节点上配置粒子团。
        /// </summary>
        /// <remarks>粒子系统挂在预制体根节点上，而不是根节点下再套一层空物体：
        /// 运行时对象池按"根节点上的 ParticleSystem"取组件，多一层会让它在预制体根上找不到系统。</remarks>
        private static ParticleSystem CreatePuff(Puff spec, GameObject host)
        {
            var system = host.AddComponent<ParticleSystem>();
            var main = system.main;
            main.duration = spec.Duration;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(spec.LifeMin, spec.LifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spec.SpeedMin, spec.SpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.SizeMin, spec.SizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = spec.Gravity;
            main.maxParticles = Mathf.Max(4, spec.Count * 2);

            // 世界空间模拟：特效挂在池对象上，池对象会被移动到命中点后复用；
            // 若用本地空间，已经喷出去的火花会跟着池对象一起瞬移。
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)spec.Count) });

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spec.ConeAngle;
            shape.radius = 0.02f;

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = EnsureParticleMaterial(spec);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.alignment = ParticleSystemRenderSpace.View;

            return system;
        }

        /// <summary>创建或更新一个粒子材质（一个特效贴图一份）。</summary>
        private static Material EnsureParticleMaterial(Puff spec)
        {
            var path = $"{MaterialsFolder}/M_Vfx_{spec.Name}.mat";
            var shader = Shader.Find(ParticleShaderName);
            if (shader == null)
            {
                return null;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{spec.Texture}.png");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", spec.Color);
            material.SetTexture("_BaseMap", texture);

            // URP 粒子着色器的透明与混合开关：透明表面 + 加法混合用于自发光类（火焰、火花），
            // 传统 Alpha 混合用于烟雾与血雾——烟雾用加法会变成一团发光的白雾。
            SetIfPresent(material, "_Surface", 1f);
            SetIfPresent(material, "_Blend", spec.Additive ? 2f : 0f);
            SetIfPresent(material, "_ZWrite", 0f);
            SetIfPresent(material, "_AlphaClip", 0f);
            material.renderQueue = 3000;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        /// <summary>
        /// 把粒子贴图设为"透明、不重复、不做多级渐远"。
        /// </summary>
        /// <remarks>
        /// 三件事都是粒子贴图的刚需：不开透明会把黑色背景画成方块；
        /// 重复采样会让火焰边缘出现一条硬接缝；多级渐远在缩小的火花上会糊成灰点。
        /// </remarks>
        private static void ConfigureParticleTextureImporters()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    continue;
                }

                if (importer.textureType == TextureImporterType.Default &&
                    importer.alphaIsTransparency &&
                    importer.wrapMode == TextureWrapMode.Clamp &&
                    !importer.mipmapEnabled)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }
    }
}
