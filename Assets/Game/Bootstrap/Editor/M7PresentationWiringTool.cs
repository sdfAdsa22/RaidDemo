using System.Text;
using RaidDemo.Bootstrap;
using RaidDemo.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 把表现层资产目录接进两个场景的启动组件。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独做"接线工具"而不是重新生成场景：</b>重新生成会丢掉所有在编辑器里
    /// 手工调过的内容（灯光角度、道具微调位置），而这次改动的本质只是"给启动组件填一个引用"。
    /// 用工具直接改序列化字段，风险面小得多，而且可以反复执行——第二次跑不会产生任何差异。</para>
    /// <para>场景以**附加方式**打开、改完即保存并关闭，不打扰编辑器当前打开的场景。</para>
    /// </remarks>
    public static class M7PresentationWiringTool
    {
        /// <summary>需要接线的场景。</summary>
        private static readonly string[] ScenePaths =
        {
            "Assets/Game/Content/Scenes/GreyboxRaid.unity",
            "Assets/Game/Content/Scenes/SafeHouse.unity"
        };

        /// <summary>启动组件上的字段名。</summary>
        private const string CatalogField = "m_PresentationCatalog";

        /// <summary>菜单入口。</summary>
        [MenuItem("RaidDemo/M7/把表现层目录接入场景")]
        public static void ApplyFromMenu()
        {
            Debug.Log(ApplyToAllScenes());
        }

        /// <summary>
        /// 给全部场景的启动组件写入表现层目录引用。
        /// </summary>
        /// <returns>过程摘要。</returns>
        public static string ApplyToAllScenes()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PresentationCatalog>(
                M7PresentationCatalogBuilder.CatalogPath);
            if (catalog == null)
            {
                return $"[RaidDemo] 找不到表现层目录 {M7PresentationCatalogBuilder.CatalogPath}，" +
                       "请先执行「重建表现层资产（音效 / 武器 / 特效）」。";
            }

            // 附加打开场景时，若当前场景是"未保存的新场景"，Unity 会拒绝加载（M7-P-03 的教训）。
            // 与其抛出难懂的内部错误，不如提前说清要做什么。
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && active.isDirty)
            {
                return "[RaidDemo] 当前打开的是一个未保存的新场景，无法附加加载目标场景。" +
                       "请先保存或关闭它，再执行本工具。";
            }

            var summary = new StringBuilder("[RaidDemo] 表现层接线：");
            foreach (var path in ScenePaths)
            {
                summary.Append('\n').Append("  ").Append(WireScene(path, catalog));
            }

            AssetDatabase.SaveAssets();
            return summary.ToString();
        }

        /// <summary>处理一个场景。</summary>
        private static string WireScene(string scenePath, PresentationCatalog catalog)
        {
            var alreadyLoaded = SceneManager.GetSceneByPath(scenePath);
            var wasLoaded = alreadyLoaded.IsValid() && alreadyLoaded.isLoaded;
            var scene = wasLoaded
                ? alreadyLoaded
                : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            if (!scene.IsValid())
            {
                return $"{scenePath}（打开失败）";
            }

            var written = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                // 战局与安全屋各有一个启动组件，类型不同、字段名相同。
                written += WriteCatalog(root.GetComponentInChildren<SceneBootstrap>(true), catalog);
                written += WriteCatalog(root.GetComponentInChildren<SafeHouseBootstrap>(true), catalog);
            }

            if (written > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (!wasLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            return written > 0
                ? $"{scenePath}（已写入 {written} 处引用）"
                : $"{scenePath}（未找到启动组件）";
        }

        /// <summary>给一个启动组件写引用。</summary>
        private static int WriteCatalog(Component bootstrap, PresentationCatalog catalog)
        {
            if (bootstrap == null)
            {
                return 0;
            }

            var serialized = new SerializedObject(bootstrap);
            var property = serialized.FindProperty(CatalogField);
            if (property == null)
            {
                return 0;
            }

            if (property.objectReferenceValue == catalog)
            {
                return 0;
            }

            property.objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
            return 1;
        }
    }
}
