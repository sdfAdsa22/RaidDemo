using System;
using System.IO;
using System.Linq;
using RaidDemo.Kernel.Updates;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M10 批次 4：资源层（Addressables）的构建与发布入口。
    /// </summary>
    /// <remarks>
    /// <para><b>资源层在整条更新链路里的位置：</b>本体由启动器在进程外替换；
    /// 资源层（catalog + bundle）与代码层（热更 DLL）由游戏内的热更流程下载。
    /// 因此这一步的产物要落进更新源清单的 <c>content</c> 段（第 3 章的协议）。</para>
    ///
    /// <para><b>构建输出路径写进 Addressables Profile 而不是散落在代码里：</b>
    /// <c>RemoteBuildPath</c> / <c>RemoteLoadPath</c> 是 Addressables 自己的配置，
    /// 菜单只负责把它们设成"构建产物进 Builds/、运行期相对 content/ 解析"，
    /// 这样产物布局与运行期寻址规则各自只有一处定义。</para>
    /// </remarks>
    public static class M10ContentBuildMenu
    {
        /// <summary>Addressables 构建产物目录（相对工程根；Builds/ 已被 .gitignore 忽略）。</summary>
        public const string ContentBuildRoot = "Builds/Addressables";

        /// <summary>运行期使用的相对加载路径前缀（由游戏内热更层按更新源重写）。</summary>
        public const string ContentLoadPath = "content/[BuildTarget]";

        /// <summary>
        /// 构建资源层（Addressables）。
        /// </summary>
        [MenuItem("RaidDemo/M10/⑤ 构建资源（Addressables）", priority = 105)]
        public static void BuildContent()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Debug.LogError("[M10 资源] Addressables 设置不可用，请先执行菜单 ④。");
                return;
            }

            ConfigureProfilePaths(settings);

            var totalEntries = settings.groups.Where(group => group != null).Sum(group => group.entries.Count);
            if (totalEntries == 0)
            {
                Debug.LogError("[M10 资源] 没有任何已登记的资产，请先执行菜单 ④ 配置分组。");
                return;
            }

            Debug.Log($"[M10 资源] 开始构建：{settings.groups.Count(group => group != null)} 个分组 / {totalEntries} 个条目");

            // 构建前清空输出目录：Addressables 的 bundle 名带内容哈希，不清空的话旧 bundle 会一直留在目录里，
            // 而清单是"扫描整个目录"生成的——结果就是每次改数值，玩家都要多下一份已经没人引用的旧 bundle。
            var staleDirectory = GetOutputDirectory();
            if (Directory.Exists(staleDirectory))
            {
                Directory.Delete(staleDirectory, true);
                Debug.Log($"[M10 资源] 已清空上次的构建输出：{staleDirectory}");
            }

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError("[M10 资源] 构建失败：" + result.Error);
                return;
            }

            var outputDirectory = GetOutputDirectory();
            var files = Directory.Exists(outputDirectory)
                ? new DirectoryInfo(outputDirectory).GetFiles()
                : Array.Empty<FileInfo>();

            var bundles = files.Where(file => file.Extension == ".bundle").ToList();
            var catalogs = files.Where(file => file.Name.StartsWith("catalog", StringComparison.OrdinalIgnoreCase)).ToList();
            var totalBytes = bundles.Sum(file => file.Length);

            Debug.Log(
                $"[M10 资源] 构建完成：{bundles.Count} 个 bundle / {totalBytes / (1024f * 1024f):F1} MB ｜ " +
                $"catalog {catalogs.Count} 个 ｜ 输出 {outputDirectory}");
            Debug.Log("[M10 资源] 下一步：执行菜单 ⑥ 生成含资源的更新清单。");
        }

        /// <summary>
        /// 生成"本体 + 资源"两层的更新清单。
        /// </summary>
        /// <remarks>
        /// 与菜单 ①（仅本体）的区别在于是否把资源层写进清单。
        /// 两者都调用同一个生成器，因此清单格式与哈希规则不会出现两套。
        /// </remarks>
        [MenuItem("RaidDemo/M10/⑥ 生成更新清单（本体 + 资源）", priority = 106)]
        public static void BuildManifestWithContent()
        {
            var version = PlayerSettings.bundleVersion;
            var bodyDirectory = Path.Combine("Builds/Client", version).Replace('\\', '/');
            var contentDirectory = GetOutputDirectory();

            if (!Directory.Exists(bodyDirectory))
            {
                Debug.LogError($"[M10 资源] 找不到本体产物：{bodyDirectory}。请先执行菜单 ①（或单独构建客户端）。");
                return;
            }

            if (!Directory.Exists(contentDirectory))
            {
                Debug.LogError($"[M10 资源] 找不到资源产物：{contentDirectory}。请先执行菜单 ⑤。");
                return;
            }

            RemoveStaleCatalogs(contentDirectory, version);

            var contentVersion = BuildContentVersion(contentDirectory);
            UpdateManifestBuilder.Generate(
                bodyDirectory,
                contentDirectory,
                codeDirectory: null,
                bodyVersion: version,
                contentVersion: contentVersion,
                codeVersion: null);

            Debug.Log($"[M10 资源] 资源层版本号：{contentVersion}（由 catalog 哈希派生，内容变了版本号才会变）");
        }

        /// <summary>
        /// 清掉资源产物目录里"不属于当前本体版本"的 catalog。
        /// </summary>
        /// <param name="contentDirectory">资源产物目录。</param>
        /// <param name="bodyVersion">当前本体版本（<c>PlayerSettings.bundleVersion</c>）。</param>
        /// <remarks>
        /// <para><b>为什么需要它（M10 演练踩到）：</b>Addressables 的 catalog 文件名带版本号
        /// （<c>catalog_0.10.0.bin</c>）。菜单 ⑤ 在构建前会清空输出目录，但<b>玩家构建（菜单 ①）
        /// 也会往同一个目录写一份 catalog</b>，而它不清空——于是"① → ⑥"这种跳过 ⑤ 的顺序会让
        /// 目录里同时躺着两个版本的 catalog，清单把它们全部收进去：客户端白下两个文件，
        /// 而 <see cref="BuildContentVersion"/> 取到哪一个还是不保证的。</para>
        ///
        /// <para>删的是构建产物目录里的过期文件，不是源资产；判断依据只有一条——
        /// 文件名里带不带当前版本号。</para>
        /// </remarks>
        private static void RemoveStaleCatalogs(string contentDirectory, string bodyVersion)
        {
            var marker = "_" + bodyVersion + ".";
            var files = new DirectoryInfo(contentDirectory).GetFiles("catalog_*");

            foreach (var file in files)
            {
                if (file.Name.Contains(marker))
                {
                    continue;
                }

                Debug.Log($"[M10 资源] 清理过期 catalog：{file.Name}（不属于本体 {bodyVersion}）");
                file.Delete();
            }
        }

        /// <summary>取资源产物目录（按当前构建目标分目录）。</summary>
        public static string GetOutputDirectory()
        {
            return Path.Combine(ContentBuildRoot, EditorUserBuildSettings.activeBuildTarget.ToString())
                .Replace('\\', '/');
        }

        /// <summary>
        /// 把 Addressables 的构建 / 加载路径写进当前 Profile。
        /// </summary>
        private static void ConfigureProfilePaths(AddressableAssetSettings settings)
        {
            var profileId = settings.activeProfileId;
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteBuildPath, ContentBuildRoot + "/[BuildTarget]");
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteLoadPath, ContentLoadPath);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.ProfileModified, null, true, true);
        }

        /// <summary>
        /// 由 catalog 内容哈希派生资源层版本号。
        /// </summary>
        /// <remarks>
        /// 用内容哈希而不是"构建次数"：同样的内容重复构建得到同样的版本号，
        /// 客户端就不会因为"重新打了一次包"而白下载一遍。
        /// </remarks>
        private static string BuildContentVersion(string contentDirectory)
        {
            var catalog = new DirectoryInfo(contentDirectory)
                .GetFiles()
                .FirstOrDefault(file => file.Name.StartsWith("catalog", StringComparison.OrdinalIgnoreCase) &&
                                        (file.Extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
                                         file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)));

            if (catalog == null)
            {
                return PlayerSettings.bundleVersion + ".c0";
            }

            var hash = UpdateFileHash.ComputeFileHash(catalog.FullName);
            return PlayerSettings.bundleVersion + ".c" + hash.Substring(0, 8);
        }
    }
}
