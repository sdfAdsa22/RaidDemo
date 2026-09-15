using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M10 · 构建与清单生成的菜单入口。
    /// </summary>
    /// <remarks>
    /// <para><b>与 M9 服务器菜单的关系：</b>服务器菜单产出"专用服务器包"，本菜单产出"玩家客户端包 + 更新清单"。
    /// 两者共用同一份源码与同一套场景收集逻辑，差别只在构建子目标（<c>Server</c> / <c>Player</c>）
    /// 与产物整理方式。</para>
    ///
    /// <para><b>为什么"构建 + 生成清单"合成一个菜单：</b>清单必须与产物同源。
    /// 拆成两个菜单时，最容易发生的事就是改了代码、重新构建、却忘了重算清单——
    /// 于是启动器拿着旧哈希去比对，玩家永远更新不成功。合成一个动作之后，
    /// "出包"这件事在工程上只剩一种做法。</para>
    ///
    /// <para><b>命令行等价入口</b>（自动化与验收脚本用）：
    /// <c>unity command --project-path &lt;工程&gt; menu --path "RaidDemo/M10/① 构建客户端（Windows）并生成更新清单"</c>。</para>
    /// </remarks>
    public static class UpdateBuildMenu
    {
        /// <summary>客户端产物根目录（相对工程根）。</summary>
        private const string ClientOutputRoot = "Builds/Client";

        /// <summary>客户端可执行文件名。</summary>
        private const string ClientExecutableName = "RaidDemo.exe";

        /// <summary>
        /// 构建 Windows 客户端并生成本体层更新清单。
        /// </summary>
        [MenuItem("RaidDemo/M10/① 构建客户端（Windows）并生成更新清单", priority = 100)]
        public static void BuildClientAndManifest()
        {
            var version = PlayerSettings.bundleVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                Debug.LogError("[M10 构建] PlayerSettings 的 bundleVersion 为空，无法确定本体版本号。");
                return;
            }

            var outputDirectory = Path.Combine(ClientOutputRoot, version).Replace('\\', '/');
            if (!TryBuildClient(outputDirectory, Path.Combine(outputDirectory, ClientExecutableName)))
            {
                return;
            }

            // 本体层就绪后立刻生成清单；资源层与代码层在批次 4 / 5 接上后传目录即可。
            UpdateManifestBuilder.Generate(
                outputDirectory,
                contentDirectory: null,
                codeDirectory: null,
                bodyVersion: version,
                contentVersion: null,
                codeVersion: null);

            Debug.Log(
                $"[M10 构建] 完成。下一步：把 {UpdateManifestBuilder.GetVersionDirectory(version)} 整个目录" +
                "上传到更新源的 versions/<版本>/ 下，再用更新源面板发布。");
        }

        /// <summary>
        /// 只重新生成更新清单（不重新出包）。
        /// </summary>
        /// <remarks>
        /// 适用于"产物没变、只想确认清单"的场景（例如手工删掉了产物目录里的无用文件）。
        /// 注意：它不能替代重新出包——改了代码必须重新构建，否则清单描述的还是旧产物。
        /// </remarks>
        [MenuItem("RaidDemo/M10/② 重新生成更新清单（不构建）", priority = 101)]
        public static void RegenerateManifestOnly()
        {
            var version = PlayerSettings.bundleVersion;
            var outputDirectory = Path.Combine(ClientOutputRoot, version).Replace('\\', '/');

            if (!Directory.Exists(outputDirectory))
            {
                Debug.LogError($"[M10 构建] 客户端产物目录不存在：{outputDirectory}。请先执行菜单 ①。");
                return;
            }

            UpdateManifestBuilder.Generate(
                outputDirectory,
                contentDirectory: null,
                codeDirectory: null,
                bodyVersion: version,
                contentVersion: null,
                codeVersion: null);
        }

        /// <summary>在资源管理器里打开更新产物目录。</summary>
        [MenuItem("RaidDemo/M10/③ 打开更新产物目录", priority = 102)]
        public static void RevealUpdateOutput()
        {
            var version = PlayerSettings.bundleVersion;
            var directory = UpdateManifestBuilder.GetVersionDirectory(version);

            if (!Directory.Exists(directory))
            {
                Debug.LogWarning($"[M10 构建] 目录尚不存在（先执行菜单 ①）：{directory}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(directory),
                UseShellExecute = true,
            });
        }

        /// <summary>
        /// 执行一次客户端构建。
        /// </summary>
        /// <param name="outputDirectory">产物目录。</param>
        /// <param name="executablePath">可执行文件完整路径。</param>
        /// <returns>构建成功返回 <c>true</c>。</returns>
        private static bool TryBuildClient(string outputDirectory, string executablePath)
        {
            var scenes = CollectEnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[M10 构建] 构建列表里没有已启用的场景，无法出包。");
                return false;
            }

            Directory.CreateDirectory(outputDirectory);

            // M9 的服务器菜单会临时切到 Server 子目标；这里显式切回 Player，
            // 避免"先构建过服务器、再构建客户端"时产出错误形态的包。
            var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = executablePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[M10 构建] 客户端构建失败：{summary.result}，错误 {summary.totalErrors} 条。");
                    return false;
                }

                Debug.Log(
                    $"[M10 构建] 客户端构建成功：{summary.outputPath} ｜ " +
                    $"大小 {summary.totalSize / (1024f * 1024f):F1} MB ｜ 用时 {summary.totalTime.TotalSeconds:F0} 秒");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[M10 构建] 客户端构建异常：{exception.Message}");
                return false;
            }
            finally
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
            }
        }

        /// <summary>取出构建列表里已启用的场景路径。</summary>
        /// <remarks>与 M9 服务器菜单同一逻辑；场景集合即"本体包含哪些场景"的唯一来源。</remarks>
        private static string[] CollectEnabledScenes()
        {
            var enabled = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    enabled.Add(scene.path);
                }
            }

            return enabled.ToArray();
        }
    }
}
