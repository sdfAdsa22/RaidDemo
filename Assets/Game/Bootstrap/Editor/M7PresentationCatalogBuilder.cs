using System.Text;
using RaidDemo.Presentation;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 批次 3 的一键加工入口：音效 → 武器模型 → 特效 → 表现层目录。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须有一个"按正确顺序执行"的入口：</b>表现层目录引用的是前三个构建器的产物，
    /// 顺序错了（例如先建目录再生成预制体）会得到一个引用为空的资产，而空引用在游戏里的表现是
    /// "没有枪声、没有枪口火焰"，排查时很容易怀疑到代码而不是资产。</para>
    /// <para>单独提供"只重建某一项"的菜单（各构建器自己的菜单项）仍然保留，
    /// 调一个特效参数时不必连音效一起重剪。</para>
    /// </remarks>
    public static class M7PresentationCatalogBuilder
    {
        /// <summary>表现层目录资产路径。</summary>
        public const string CatalogPath = "Assets/Game/Content/Presentation/PresentationCatalog.asset";

        /// <summary>目录资产所在文件夹。</summary>
        public const string CatalogFolder = "Assets/Game/Content/Presentation";

        /// <summary>准星素材目录（Kenney Crosshair Pack，CC0）。</summary>
        private const string CrosshairFolder = "Assets/Game/Content/External/Kenney/CrosshairPack";

        /// <summary>菜单入口。</summary>
        [MenuItem("RaidDemo/M7/重建表现层资产（音效 / 武器 / 特效）")]
        public static void BuildFromMenu()
        {
            Debug.Log(BuildAll());
        }

        /// <summary>
        /// 按依赖顺序重建全部表现层资产。
        /// </summary>
        /// <returns>过程摘要。</returns>
        public static string BuildAll()
        {
            var summary = new StringBuilder();
            summary.Append(M7AudioAssetBuilder.BuildAll()).Append('\n');
            summary.Append(M7WeaponPrefabBuilder.BuildAll()).Append('\n');
            summary.Append(M7VfxPrefabBuilder.BuildAll()).Append('\n');
            summary.Append(BuildCatalog());
            return summary.ToString();
        }

        /// <summary>只重建表现层目录（前三个构建器已经跑过时用）。</summary>
        [MenuItem("RaidDemo/M7/只重建表现层资产目录")]
        public static void BuildCatalogFromMenu()
        {
            Debug.Log(BuildCatalog());
        }

        /// <summary>创建或更新表现层目录。</summary>
        private static string BuildCatalog()
        {
            M7MaterialLibrary.EnsureFolder(CatalogFolder);
            EnsureCrosshairSprites();

            var catalog = AssetDatabase.LoadAssetAtPath<PresentationCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PresentationCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var serialized = new SerializedObject(catalog);
            SetReference(serialized, "m_Audio",
                AssetDatabase.LoadAssetAtPath<AudioCatalog>(M7AudioAssetBuilder.CatalogPath));
            SetWeapons(serialized);
            SetReference(serialized, "m_MuzzleFlashPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(M7VfxPrefabBuilder.VfxFolder + "/Vfx_MuzzleFlash.prefab"));
            SetReference(serialized, "m_ImpactSparkPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(M7VfxPrefabBuilder.VfxFolder + "/Vfx_ImpactSpark.prefab"));
            SetReference(serialized, "m_ImpactDustPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(M7VfxPrefabBuilder.VfxFolder + "/Vfx_ImpactDust.prefab"));
            SetReference(serialized, "m_ImpactFleshPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(M7VfxPrefabBuilder.VfxFolder + "/Vfx_ImpactFlesh.prefab"));
            SetReference(serialized, "m_CrosshairSprite",
                AssetDatabase.LoadAssetAtPath<Sprite>(CrosshairFolder + "/Crosshair_Normal.png"));
            SetReference(serialized, "m_CrosshairReloadSprite",
                AssetDatabase.LoadAssetAtPath<Sprite>(CrosshairFolder + "/Crosshair_Reload.png"));
            SetPlayerCharacters(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            return $"[RaidDemo] M7 表现层目录：{CatalogPath}";
        }

        /// <summary>
        /// 把准星 PNG 的导入类型设为 Sprite。
        /// </summary>
        /// <remarks>Unity 对普通 PNG 的默认导入类型是 Texture，而界面需要的是 Sprite；
        /// 不设这一步的话 <c>LoadAssetAtPath&lt;Sprite&gt;</c> 会返回 null，
        /// 表现为"准星又变回程序化的十字"——功能没坏，但素材等于没用上。</remarks>
        private static void EnsureCrosshairSprites()
        {
            if (!AssetDatabase.IsValidFolder(CrosshairFolder))
            {
                return;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CrosshairFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    continue;
                }

                if (importer.textureType == TextureImporterType.Sprite && !importer.mipmapEnabled)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        /// <summary>写入一个对象引用槽位。</summary>
        private static void SetReference(SerializedObject serialized, string propertyName, Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[RaidDemo] 表现层目录缺少字段 {propertyName}，请检查 PresentationCatalog 是否被改过。");
                return;
            }

            property.objectReferenceValue = value;
        }

        /// <summary>
        /// 把四把武器的模型与表现类别写进表现层目录。
        /// </summary>
        /// <remarks>
        /// <para>表里写物品 ID，而不是"第几把枪"：运行时按装备的物品 ID 精确查模型，
        /// 因此给某个敌人或某件未来武器换模型时，改这一张表即可。</para>
        /// <para>表现类别（Rifle / Pistol / SMG / Shotgun）决定用哪套枪声与枪口火焰距离，
        /// 与模型一一对应地写在同一行，避免"换了模型、忘了换枪声"。</para>
        /// </remarks>
        private static void SetWeapons(SerializedObject serialized)
        {
            var property = serialized.FindProperty("m_WeaponModels");
            if (property == null)
            {
                Debug.LogWarning("[RaidDemo] 表现层目录缺少 m_WeaponModels 字段，武器模型将全部回退为灰盒。");
                return;
            }

            var entries = new[]
            {
                ("weapon.pistol.pm", M7WeaponPrefabBuilder.PistolPrefabPath, WeaponPresentationKind.Pistol),
                ("weapon.rifle.ak74", M7WeaponPrefabBuilder.RiflePrefabPath, WeaponPresentationKind.Rifle),
                ("weapon.smg.uzi", M7WeaponPrefabBuilder.SmgPrefabPath, WeaponPresentationKind.SMG),
                ("weapon.shotgun.pump", M7WeaponPrefabBuilder.ShotgunPrefabPath, WeaponPresentationKind.Shotgun),
            };

            property.arraySize = entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("m_ItemId").stringValue = entries[i].Item1;
                element.FindPropertyRelative("m_Prefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(entries[i].Item2);
                element.FindPropertyRelative("m_Kind").enumValueIndex = (int)entries[i].Item3;
            }
        }

        /// <summary>
        /// 把 12 个玩家角色预制体写进表现层目录。
        /// </summary>
        /// <remarks>
        /// 角色预制体由 <see cref="PlayerCharacterBuilder"/> 生成；目录只保存 id、显示名与引用。
        /// 这样运行时不需要按名字猜资源路径，也不会把角色列表复制到多个场景。
        /// </remarks>
        private static void SetPlayerCharacters(SerializedObject serialized)
        {
            var property = serialized.FindProperty("m_PlayerCharacters");
            if (property == null)
            {
                Debug.LogWarning("[RaidDemo] 表现层目录缺少 m_PlayerCharacters 字段，角色选择列表会为空。");
                return;
            }

            var options = PlayerCharacterBuilder.CharacterOptions;
            property.arraySize = options.Count;
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var element = property.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("m_Id").stringValue = option.Id;
                element.FindPropertyRelative("m_DisplayName").stringValue = option.DisplayName;
                element.FindPropertyRelative("m_Prefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(option.PrefabPath);
            }
        }
    }
}
