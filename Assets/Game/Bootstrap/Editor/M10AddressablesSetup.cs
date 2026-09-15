using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M10 批次 4：Addressables 分组与资产入组的配置工具。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有这个工具而不是在界面里手点：</b>哪 38 个资产必须进 Addressables
    /// 是由"脚本挂在资产上"这个硬约束决定的（见 ADR-008），不是审美选择。
    /// 把它做成幂等的菜单，任何人（或任何一次重装/重建工程之后）都能一条命令恢复正确配置，
    /// 而不是靠记忆点一遍——漏一个资产的表现是"运行时脚本丢失"，排查成本极高。</para>
    ///
    /// <para><b>分组原则是"更新频率"而不是"资源类型"</b>（ADR-002 的教训条目）：
    /// 数据表要能随时调整数值，因此单独成组、体积小、下载快；场景与角色很少改；
    /// 美术与音效按需发布。分得太粗会让"改一个数值"变成"重下几百 MB"。</para>
    ///
    /// <para><b>幂等</b>：重复执行只会补齐缺失的分组与条目，不会重复添加、不会改变已有地址。
    /// </para>
    /// </remarks>
    public static class M10AddressablesSetup
    {
        /// <summary>数据表组名（物品定义、目录、表现目录、音频目录）。</summary>
        public const string DataGroupName = "group-data";

        /// <summary>场景组名（安全屋 + 战局）。</summary>
        public const string SceneGroupName = "group-scenes";

        /// <summary>角色预制体组名。</summary>
        public const string CharacterGroupName = "group-characters";

        /// <summary>美术组名（武器 / 道具 / 环境，按批次逐步纳入）。</summary>
        public const string ArtGroupName = "group-art";

        /// <summary>音效组名。</summary>
        public const string AudioGroupName = "group-audio";

        /// <summary>内容根目录。</summary>
        private const string ContentRoot = "Assets/Game/Content";

        /// <summary>
        /// 配置分组并把"挂在资产上的脚本"涉及的资产全部纳入（幂等）。
        /// </summary>
        [MenuItem("RaidDemo/M10/④ 配置 Addressables 分组（幂等）", priority = 103)]
        public static void ConfigureGroups()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Debug.LogError("[M10 资源] 无法创建 Addressables 设置，请确认 com.unity.addressables 已安装。");
                return;
            }

            settings.BuildRemoteCatalog = true;
            settings.MaxConcurrentWebRequests = 4;

            // 远端 catalog 的构建 / 加载路径必须显式指向 Profile 变量：
            // 否则 BuildRemoteCatalog 虽然开着，产物里也不会出现 catalog_*.json
            // （实测症状：bundle 都生成了，客户端却没有任何入口文件可用）。
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);

            var dataGroup = EnsureGroup(settings, DataGroupName);
            var sceneGroup = EnsureGroup(settings, SceneGroupName);
            var characterGroup = EnsureGroup(settings, CharacterGroupName);
            var artGroup = EnsureGroup(settings, ArtGroupName);
            var audioGroup = EnsureGroup(settings, AudioGroupName);

            var added = 0;

            // 数据表：物品/掉落/数值与目录资产。它们体量小、改得最频繁，单独成组最划算。
            added += MarkAssets(settings, dataGroup, new[] { ContentRoot + "/Items", ContentRoot + "/Presentation" }, "*.asset", "data");
            added += MarkAssets(settings, dataGroup, new[] { ContentRoot + "/Audio" }, "*.asset", "data");

            // 场景：安全屋与战局。它们带着热更脚本，必须进 Addressables（ADR-008）。
            added += MarkAssets(settings, sceneGroup, new[] { ContentRoot + "/Scenes" }, "*.unity", "scene");

            // 角色：12 个可选角色 + 3 个敌人，连同材质与 Animator Controller。
            added += MarkAssets(settings, characterGroup, new[] { ContentRoot + "/Art/Characters" }, "*.*", "character");

            // 音效：wav 本体归音效组（与数据表分开——换音效不该让数据表重新下载）。
            added += MarkAssets(settings, audioGroup, new[] { ContentRoot + "/Audio" }, "*.wav", "audio");

            // 美术组的资产按批次逐步纳入（枪械 / 道具 / 环境），这里先保证组存在。
            _ = artGroup;

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[M10 资源] 分组配置完成：本次新增 {added} 个条目。" +
                $"数据表 {CountEntries(dataGroup)} ｜ 场景 {CountEntries(sceneGroup)} ｜ 角色 {CountEntries(characterGroup)}");
            Debug.Log("[M10 资源] 提示：新增资产后请再次执行本菜单，确保它们也被纳入分组。");
        }

        /// <summary>
        /// 统计当前分组状态（排查"是不是漏了什么"时用）。
        /// </summary>
        [MenuItem("RaidDemo/M10/④b 打印分组状态", priority = 104)]
        public static void PrintStatus()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                Debug.LogWarning("[M10 资源] 尚未创建 Addressables 设置（先执行菜单 ④）。");
                return;
            }

            foreach (var group in settings.groups)
            {
                if (group == null)
                {
                    continue;
                }

                var schema = group.GetSchema<BundledAssetGroupSchema>();
                var remote = schema != null && schema.BuildPath.GetName(settings).Contains("Remote");
                var buildPath = schema?.BuildPath?.GetValue(settings) ?? "（未配置）";
                Debug.Log($"[M10 资源] {group.Name}：{CountEntries(group)} 个条目 ｜ {(remote ? "远程" : "本地")} ｜ {buildPath}");
            }
        }

        /// <summary>
        /// 确保分组存在，并把它配置成"远程打包"（构建产物进资源层，由更新源分发）。
        /// </summary>
        /// <param name="settings">Addressables 设置。</param>
        /// <param name="name">分组名。</param>
        /// <returns>分组对象。</returns>
        private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string name)
        {
            var group = settings.FindGroup(name);
            if (group == null)
            {
                group = settings.CreateGroup(
                    name,
                    setAsDefaultGroup: false,
                    readOnly: false,
                    postEvent: false,
                    schemasToCopy: null,
                    typeof(BundledAssetGroupSchema));
            }

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null)
            {
                schema = group.AddSchema<BundledAssetGroupSchema>();
            }

            // 远程打包：bundle 与 catalog 由更新源分发；运行时再由游戏内的热更流程决定用哪一份。
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;

            group.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaModified, null, false, true);
            return group;
        }

        /// <summary>
        /// 把一个目录下的全部资产纳入分组（幂等）。
        /// </summary>
        /// <param name="settings">Addressables 设置。</param>
        /// <param name="group">目标分组。</param>
        /// <param name="folders">目录列表（相对工程根）。</param>
        /// <param name="addressPrefix">地址前缀（例 <c>data</c> → <c>data/items/ak74</c>）。</param>
        /// <returns>新增条目数。</returns>
        private static int MarkAssets(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            IReadOnlyList<string> folders,
            string searchPattern,
            string addressPrefix)
        {
            var added = 0;

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var assetPath in Directory.GetFiles(folder, searchPattern, SearchOption.AllDirectories))
                {
                    var normalized = assetPath.Replace('\\', '/');
                    if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (AssetDatabase.LoadMainAssetAtPath(normalized) == null)
                    {
                        continue;
                    }

                    var guid = AssetDatabase.AssetPathToGUID(normalized);
                    if (string.IsNullOrEmpty(guid))
                    {
                        continue;
                    }

                    var entry = settings.CreateOrMoveEntry(guid, group, postEvent: false);
                    if (entry == null)
                    {
                        continue;
                    }

                    var address = BuildAddress(addressPrefix, folder, normalized);
                    if (entry.address != address)
                    {
                        entry.SetAddress(address, postEvent: false);
                    }

                    added++;
                }
            }

            return added;
        }

        /// <summary>
        /// 生成稳定可读的地址：<c>&lt;前缀&gt;/&lt;相对路径&gt;</c>（全小写、正斜杠）。
        /// </summary>
        /// <remarks>
        /// 地址一旦发布就不能随便改——它是代码里 <c>Addressables.LoadAssetAsync("地址")</c> 的键，
        /// 改了就必须同时改代码，等于一次强制发版。因此这里用"目录结构派生"的规则，
        /// 而不是人工起名：结构性改名必然伴随文件位置变化，容易被发现。
        /// </remarks>
        private static string BuildAddress(string prefix, string rootFolder, string assetPath)
        {
            var root = rootFolder.Replace('\\', '/').TrimEnd('/') + "/";
            var relative = assetPath.Replace('\\', '/');
            if (relative.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                relative = relative.Substring(root.Length);
            }

            var extension = Path.GetExtension(relative);
            if (!string.IsNullOrEmpty(extension))
            {
                relative = relative.Substring(0, relative.Length - extension.Length);
            }

            return (prefix + "/" + relative).ToLowerInvariant();
        }

        /// <summary>统计分组内的条目数。</summary>
        private static int CountEntries(AddressableAssetGroup group)
        {
            return group == null ? 0 : group.entries.Count(entry => entry != null);
        }
    }
}
