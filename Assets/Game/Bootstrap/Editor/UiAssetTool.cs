using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 批次 4 的界面资产工具：TMP 基础资源导入与中文字体资产构建。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这两件事要单独做成菜单：</b>它们都会改动工程级资源
    /// （TMP 会往 <c>Assets/TextMesh Pro/</c> 写一整套着色器与设置，字体资产是一份二进制 SDF），
    /// 不应该在别人克隆仓库后第一次打开工程时被动发生。做成显式菜单，
    /// 谁需要谁执行，执行结果也进版本库。</para>
    ///
    /// <para><b>为什么不用系统字体：</b>TMP 需要读取字体文件本身（FreeType 光栅化），
    /// 而 <c>Font.CreateDynamicFontFromOSFont</c> 拿到的是引擎内部的动态字体句柄，
    /// 没有可读的字体数据——实测 <c>TMP_FontAsset.CreateFontAsset</c> 会返回 null 并打印
    /// "Unable to load font face"。因此必须把字体文件放进工程：
    /// 采用 Noto Sans SC（SIL OFL 1.1，允许随软件分发），放在
    /// <c>Content/External/Noto/NotoSansSC</c>。</para>
    /// </remarks>
    public static class UiAssetTool
    {
        /// <summary>中文字体文件（SIL OFL 1.1）。</summary>
        public const string SourceFontPath =
            "Assets/Game/Content/External/Noto/NotoSansSC/NotoSansSC-Regular.otf";

        /// <summary>生成的 TMP 字体资产目录。</summary>
        public const string FontAssetFolder = "Assets/Game/Content/Art/UI/Fonts";

        /// <summary>生成的 TMP 字体资产路径。</summary>
        public const string FontAssetPath = FontAssetFolder + "/NotoSansSC SDF.asset";

        /// <summary>字体图集边长（像素）。</summary>
        /// <remarks>动态字体资产会在运行时按需光栅化字形，图集不够时可以自动开新图集
        /// （见 <c>enableMultiAtlasSupport</c>），因此这里取 1024 起步即可。</remarks>
        private const int AtlasSize = 1024;

        /// <summary>字形采样尺寸。中文笔画多，取 90 能保证小字号下不糊。</summary>
        private const int SamplingPointSize = 90;

        /// <summary>字形内边距，避免相邻字形在图集里互相渗色。</summary>
        private const int AtlasPadding = 9;

        /// <summary>菜单入口：导入 TMP 基础资源。</summary>
        /// <remarks>官方入口是 <c>Window &gt; TextMeshPro &gt; Import TMP Essential Resources</c>，
        /// 但那是一个需要点按钮的窗口；这里直接调用同一个 API 的非交互版本。</remarks>
        [MenuItem("RaidDemo/UI/导入 TMP 基础资源")]
        public static void ImportTmpEssentials()
        {
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            Debug.Log("[RaidDemo] TMP 基础资源已导入：Assets/TextMesh Pro/");
        }

        /// <summary>菜单入口：重建中文字体资产。</summary>
        [MenuItem("RaidDemo/UI/重建中文字体资产")]
        public static void RebuildFontAssetFromMenu()
        {
            Debug.Log(BuildFontAsset());
        }

        /// <summary>
        /// 从工程内的字体文件生成 TMP 字体资产，并把它设为项目默认字体。
        /// </summary>
        /// <returns>过程摘要。</returns>
        public static string BuildFontAsset()
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
            {
                return $"[RaidDemo] 找不到字体文件：{SourceFontPath}。" +
                       "请确认 NotoSansSC-Regular.otf 已导入（SIL OFL 1.1，可随仓库分发）。";
            }

            M7MaterialLibrary.EnsureFolder(FontAssetFolder);

            // 动态模式：字形按需光栅化，不需要预先枚举用到的汉字。
            // 静态模式要先准备字符集，改一句文案就得重新生成，对还在写界面的阶段太脆。
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                source,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                return "[RaidDemo] TMP 字体资产创建失败：请检查字体文件是否包含可读字体数据。";
            }

            fontAsset.name = "NotoSansSC SDF";
            AssetDatabase.DeleteAsset(FontAssetPath);
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            // 图集与材质是字体资产的子资源，必须显式挂进去，否则重新打开工程后会丢。
            foreach (var texture in fontAsset.atlasTextures)
            {
                if (texture != null && !AssetDatabase.IsSubAsset(texture))
                {
                    AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }
            }

            if (fontAsset.material != null && !AssetDatabase.IsSubAsset(fontAsset.material))
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            AssetDatabase.SaveAssets();
            var applied = ApplyAsProjectDefault(fontAsset);
            return $"[RaidDemo] 中文字体资产已生成：{FontAssetPath}（{Directory.GetParent(FontAssetPath)?.Name}）" +
                   (applied ? "，并已设为项目默认字体。" : "，但设置默认字体失败（TMP Settings 缺失？）。");
        }

        /// <summary>
        /// 把字体资产设为 TMP 的项目默认字体。
        /// </summary>
        /// <remarks>
        /// <para>设为默认字体之后，运行时创建的 <c>TextMeshProUGUI</c> 不指定字体也能拿到中文，
        /// 界面代码因此不必持有字体引用——省掉一次"把资产引用接到场景里"的装配，
        /// 也就少了一处会漏接的地方。</para>
        /// <para>TMP 的设置资产由基础资源导入时创建；若它不存在，
        /// 说明上一步没做，这里返回 false 由调用方提示。</para>
        /// </remarks>
        private static bool ApplyAsProjectDefault(TMP_FontAsset fontAsset)
        {
            var settings = TMP_Settings.instance;
            if (settings == null)
            {
                return false;
            }

            var serialized = new SerializedObject(settings);
            var property = serialized.FindProperty("m_defaultFontAsset");
            if (property == null)
            {
                return false;
            }

            property.objectReferenceValue = fontAsset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
