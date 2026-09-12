using System.Text;
using RaidDemo.Presentation;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 批次 3：把外部音效素材加工成工程自有资产，并生成音效目录。
    /// </summary>
    /// <remarks>
    /// <para><b>选音原则（为什么是这几个文件）：</b></para>
    /// <list type="bullet">
    /// <item><description><b>步枪</b>用 AK-47 的近距与中距两条单发录音。同一个型号、两种录音距离，
    /// 交替播放时不会听出重复感，音色又是同一把枪。</description></item>
    /// <item><description><b>手枪</b>用 1911 与 Walther PPQ 两条单发录音，两支枪音色差异明显，
    /// 正好补上手枪射速慢、重复次数少的短板。</description></item>
    /// <item><description><b>撞击、脚步、界面</b>全部取自 Kenney 的 CC0 音效包：体积小、风格统一，
    /// 且授权干净，可以随仓库分发。</description></item>
    /// </list>
    /// <para>连发与长点射的录音（例如 <c>C_27P</c> 的 long burst）刻意不用：
    /// 本项目的射速由战斗层按毫秒精确控制，音效必须"一发一份"，
    /// 用连发录音会在停火后还剩半秒枪声。</para>
    /// </remarks>
    public static class M7AudioAssetBuilder
    {
        /// <summary>音效资产根目录（随仓库提交）。</summary>
        public const string AudioRoot = "Assets/Game/Content/Audio";

        /// <summary>加工后的音效文件目录。</summary>
        public const string SfxRoot = AudioRoot + "/Sfx";

        /// <summary>音效目录资产路径。</summary>
        public const string CatalogPath = AudioRoot + "/AudioCatalog.asset";

        /// <summary>枪声库（暂存区，不入库）。</summary>
        private const string FirearmRoot =
            "Assets/Game/Content/External/_Inbox/FirearmSoundLibrary";

        /// <summary>Kenney 打击音效（暂存区，不入库）。</summary>
        private const string ImpactRoot =
            "Assets/Game/Content/External/_Inbox/KenneyImpactSounds/Audio";

        /// <summary>Kenney 界面音效（暂存区，不入库）。</summary>
        private const string InterfaceRoot =
            "Assets/Game/Content/External/_Inbox/KenneyInterfaceSounds/Audio";

        /// <summary>枪声保留时长（秒）：足够听出膛压与短尾音，又不会盖住下一发。</summary>
        private const float GunSeconds = 0.9f;

        /// <summary>枪声归一化峰值。给混音留 1 dB 余量，避免多声重叠时削顶。</summary>
        private const float GunPeak = 0.92f;

        /// <summary>机械动作音（换弹、拉栓、拾取）保留时长（秒）。</summary>
        private const float MechanismSeconds = 0.5f;

        /// <summary>机械动作音归一化峰值。</summary>
        private const float MechanismPeak = 0.72f;

        /// <summary>提示音（撤离、结算）保留时长（秒）。</summary>
        private const float CueSeconds = 1.2f;

        /// <summary>提示音归一化峰值。</summary>
        private const float CuePeak = 0.78f;

        /// <summary>菜单入口。</summary>
        [MenuItem("RaidDemo/M7/重建音效资产与音效目录")]
        public static void BuildFromMenu()
        {
            Debug.Log(BuildAll());
        }

        /// <summary>
        /// 重建全部音效资产与音效目录。
        /// </summary>
        /// <returns>过程摘要，可直接打印到控制台。</returns>
        public static string BuildAll()
        {
            M7MaterialLibrary.EnsureFolder(SfxRoot);

            var summary = new StringBuilder("[RaidDemo] M7 音效资产：");

            var rifle = BakeGroup(
                summary, "步枪枪声", GunSeconds, GunPeak, FirearmRoot,
                new[] { "AK-47/C_28P.wav", "AK-47/C_31P.wav" },
                "Weapons/Rifle_Shot");
            var pistol = BakeGroup(
                summary, "手枪枪声", GunSeconds, GunPeak, FirearmRoot,
                new[] { "1911/A_42P.wav", "Walther PPQ/X_39P.wav" },
                "Weapons/Pistol_Shot");

            var dryFire = BakeOne(summary, "空仓干响", MechanismSeconds, MechanismPeak,
                InterfaceRoot, "click_004.ogg", "Weapons/DryFire");
            var magazineOut = BakeOne(summary, "弹匣脱出", MechanismSeconds, MechanismPeak,
                ImpactRoot, "impactGeneric_light_001.ogg", "Weapons/MagazineOut");
            var magazineIn = BakeOne(summary, "弹匣推入", MechanismSeconds, MechanismPeak,
                ImpactRoot, "impactMetal_medium_000.ogg", "Weapons/MagazineIn");
            var bolt = BakeOne(summary, "拉栓上膛", MechanismSeconds, MechanismPeak,
                InterfaceRoot, "switch_002.ogg", "Weapons/BoltClose");
            var switchWeapon = BakeOne(summary, "切换武器", MechanismSeconds, MechanismPeak,
                InterfaceRoot, "switch_005.ogg", "Weapons/Switch");

            var flesh = BakeGroup(
                summary, "命中活体", MechanismSeconds, MechanismPeak, ImpactRoot,
                new[]
                {
                    "impactPunch_medium_000.ogg",
                    "impactPunch_medium_001.ogg",
                    "impactPunch_medium_002.ogg"
                },
                "Impacts/Flesh");
            var hard = BakeGroup(
                summary, "命中硬物", MechanismSeconds, MechanismPeak, ImpactRoot,
                new[]
                {
                    "impactMining_000.ogg",
                    "impactPlate_light_002.ogg",
                    "impactMetal_light_001.ogg"
                },
                "Impacts/Hard");

            var grass = BakeGroup(
                summary, "草地脚步", MechanismSeconds, MechanismPeak, ImpactRoot,
                new[]
                {
                    "footstep_grass_000.ogg",
                    "footstep_grass_001.ogg",
                    "footstep_grass_002.ogg",
                    "footstep_grass_003.ogg"
                },
                "Footsteps/Grass");
            var hardStep = BakeGroup(
                summary, "硬地脚步", MechanismSeconds, MechanismPeak, ImpactRoot,
                new[]
                {
                    "footstep_concrete_000.ogg",
                    "footstep_concrete_001.ogg",
                    "footstep_concrete_002.ogg"
                },
                "Footsteps/Hard");

            var lootOpen = BakeOne(summary, "容器开启", CueSeconds, CuePeak,
                InterfaceRoot, "open_001.ogg", "Loot/Open");
            var rummage = BakeGroup(
                summary, "翻找", MechanismSeconds, MechanismPeak, InterfaceRoot,
                new[] { "scratch_002.ogg", "scratch_004.ogg" },
                "Loot/Rummage");
            var pickup = BakeOne(summary, "拾取", MechanismSeconds, CuePeak,
                InterfaceRoot, "select_002.ogg", "Loot/Pickup");
            var extractionTick = BakeOne(summary, "撤离计次", MechanismSeconds, CuePeak,
                InterfaceRoot, "tick_001.ogg", "Raid/ExtractionTick");
            var success = BakeOne(summary, "撤离成功", CueSeconds, CuePeak,
                InterfaceRoot, "confirmation_002.ogg", "Raid/Success");
            var fail = BakeOne(summary, "战局失败", CueSeconds, CuePeak,
                InterfaceRoot, "error_004.ogg", "Raid/Fail");

            var catalog = LoadOrCreateCatalog();
            var serialized = new SerializedObject(catalog);
            SetArray(serialized, "m_RifleShots", rifle);
            SetArray(serialized, "m_PistolShots", pistol);
            SetArray(serialized, "m_ImpactFlesh", flesh);
            SetArray(serialized, "m_ImpactHard", hard);
            SetArray(serialized, "m_FootstepGrass", grass);
            SetArray(serialized, "m_FootstepHard", hardStep);
            SetArray(serialized, "m_LootRummage", rummage);
            SetClip(serialized, "m_DryFire", dryFire);
            SetClip(serialized, "m_MagazineOut", magazineOut);
            SetClip(serialized, "m_MagazineIn", magazineIn);
            SetClip(serialized, "m_BoltClose", bolt);
            SetClip(serialized, "m_WeaponSwitch", switchWeapon);
            SetClip(serialized, "m_LootOpen", lootOpen);
            SetClip(serialized, "m_LootPickup", pickup);
            SetClip(serialized, "m_ExtractionTick", extractionTick);
            SetClip(serialized, "m_RaidSuccess", success);
            SetClip(serialized, "m_RaidFail", fail);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            summary.Append("\n  ").Append(CatalogPath);
            return summary.ToString();
        }

        /// <summary>按文件名列表加工一组音效，返回按顺序排列的剪辑数组。</summary>
        private static AudioClip[] BakeGroup(
            StringBuilder summary,
            string label,
            float seconds,
            float peak,
            string sourceRoot,
            string[] fileNames,
            string targetPrefix)
        {
            var clips = new AudioClip[fileNames.Length];
            for (var i = 0; i < fileNames.Length; i++)
            {
                clips[i] = BakeOne(
                    summary,
                    label,
                    seconds,
                    peak,
                    sourceRoot,
                    fileNames[i],
                    $"{targetPrefix}_{i + 1:00}");
            }

            return clips;
        }

        /// <summary>加工单条音效并载入结果。</summary>
        private static AudioClip BakeOne(
            StringBuilder summary,
            string label,
            float seconds,
            float peak,
            string sourceRoot,
            string sourceName,
            string targetName)
        {
            var recipe = new M7AudioBaker.AudioRecipe(
                $"{sourceRoot}/{sourceName}",
                $"{SfxRoot}/{targetName}.wav",
                seconds,
                peak);

            M7AudioBaker.Bake(recipe, out var detail);
            summary.Append('\n').Append("  · ").Append(label).Append("：").Append(detail);

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(recipe.TargetPath);
            if (clip == null)
            {
                summary.Append(" → 未能载入，目录中留空");
            }

            return clip;
        }

        /// <summary>载入音效目录，不存在时创建。</summary>
        private static AudioCatalog LoadOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog != null)
            {
                return catalog;
            }

            catalog = ScriptableObject.CreateInstance<AudioCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        /// <summary>写入单个剪辑槽位（允许为 null，表示该槽位缺素材）。</summary>
        private static void SetClip(SerializedObject serialized, string propertyName, AudioClip clip)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = clip;
            }
        }

        /// <summary>写入数组型剪辑槽位。</summary>
        private static void SetArray(SerializedObject serialized, string propertyName, AudioClip[] clips)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            property.arraySize = clips.Length;
            for (var i = 0; i < clips.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }
        }
    }
}
