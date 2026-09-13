using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 专用服务器构建入口（M9 · P0）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么服务器包与客户端包是同一个工程：</b>权威逻辑、AI、战局规则都只有一份源码，
    /// 服务器与客户端的差别只在启动参数（<c>-server</c>）与构建子目标。
    /// 分成两个工程会让「两边逻辑不一致」成为常态，而这是联机项目最贵的故障。</para>
    ///
    /// <para><b>子目标（Subtarget）是什么：</b>Unity 6 的独立平台构建支持
    /// <c>Player</c> 与 <c>Server</c> 两种子目标。选 <c>Server</c> 会剥离图形相关的初始化路径，
    /// 产物可以直接以 <c>-batchmode -nographics</c> 跑在 Linux 云主机上，也可以在本机开窗口运行
    /// （本机 / 局域网那套带 Dashboard 的形态）。</para>
    ///
    /// <para><b>构建后会恢复原子目标：</b>切换子目标是全局编辑器状态，
    /// 若不恢复，下一次「正常构建客户端」会莫名其妙地出一个服务器包。</para>
    /// </remarks>
    public static class ServerBuildMenu
    {
        /// <summary>服务器产物目录（相对工程根，随 .gitignore 忽略）。</summary>
        private const string OutputDirectory = "Builds/ServerWindows";

        /// <summary>可执行文件名。Linux 版由 P6 的构建脚本另起名字。</summary>
        private const string ExecutableName = "RaidDemoServer.exe";

        /// <summary>
        /// 构建 Windows 专用服务器。
        /// </summary>
        /// <remarks>
        /// 命令行调用示例（自动化验证用）：
        /// <c>unity command --project-path &lt;工程&gt; menu --path "RaidDemo/M9/构建专用服务器（Windows）"</c>
        /// </remarks>
        [MenuItem("RaidDemo/M9/构建专用服务器（Windows）")]
        public static void BuildWindowsServer()
        {
            var scenes = CollectEnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[服务器构建] 构建列表里没有已启用的场景，无法出包。");
                return;
            }

            Directory.CreateDirectory(OutputDirectory);

            var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            var previousTarget = EditorUserBuildSettings.activeBuildTarget;

            try
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(OutputDirectory, ExecutableName),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log(
                        $"[服务器构建] 成功：{summary.outputPath} ｜ " +
                        $"大小 {summary.totalSize / (1024f * 1024f):F1} MB ｜ 用时 {summary.totalTime.TotalSeconds:F0} 秒");
                    Debug.Log(
                        "[服务器构建] 启动示例：" +
                        $"{ExecutableName} -server -port 7777 -room 默认房间 -batchmode -nographics -logFile server.log");
                }
                else
                {
                    Debug.LogError($"[服务器构建] 失败：{summary.result}，错误 {summary.totalErrors} 条。");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[服务器构建] 异常：{exception.Message}");
            }
            finally
            {
                // 无论成功与否都要还原，否则后续构建会继承服务器的子目标。
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;

                if (EditorUserBuildSettings.activeBuildTarget != previousTarget)
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildPipeline.GetBuildTargetGroup(previousTarget),
                        previousTarget);
                }
            }
        }

        /// <summary>取出构建列表里已启用的场景路径。</summary>
        private static string[] CollectEnabledScenes()
        {
            var enabled = new System.Collections.Generic.List<string>();
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
