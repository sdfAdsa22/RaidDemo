using System;
using System.IO;
using UnityEngine;

namespace RaidDemo.HotUpdate
{
    /// <summary>
    /// 本地热更状态：记住"我现在用的是哪个内容版本、catalog 在哪"。
    /// </summary>
    /// <remarks>
    /// <para>这份记录是**判断要不要更新**的依据，也是"离线可用"的关键：
    /// 打不开更新源时，游戏靠它找到上一次成功下载的 catalog 继续玩。</para>
    ///
    /// <para>用 JsonUtility 而不是自研格式：字段少、需要人工查看，
    /// 与工程里其它本地存档（`SaveFileStore`）保持同一套做法。</para>
    /// </remarks>
    [Serializable]
    public sealed class HotUpdateState
    {
        /// <summary>已下载生效的内容版本号（空 = 从未下载过，使用本体自带内容）。</summary>
        public string contentVersion = string.Empty;

        /// <summary>已下载的 catalog 文件名（相对该版本目录）。</summary>
        public string catalogFileName = string.Empty;

        /// <summary>最后一次成功更新的时间（ISO 8601，仅用于排障与界面显示）。</summary>
        public string updatedAt = string.Empty;

        /// <summary>最后一次失败原因（成功时清空）。</summary>
        public string lastFailure = string.Empty;

        /// <summary>读取本地状态；不存在或损坏时返回空状态。</summary>
        public static HotUpdateState Load()
        {
            var path = HotUpdatePaths.StateFilePath;
            if (!File.Exists(path))
            {
                return new HotUpdateState();
            }

            try
            {
                var state = JsonUtility.FromJson<HotUpdateState>(File.ReadAllText(path));
                return state ?? new HotUpdateState();
            }
            catch (Exception)
            {
                // 状态文件损坏不是灾难：当成"从未下载过"，游戏会退回本体自带内容。
                return new HotUpdateState();
            }
        }

        /// <summary>写入本地状态。</summary>
        public void Save()
        {
            try
            {
                HotUpdatePaths.EnsureDirectory(HotUpdatePaths.ContentRoot);
                File.WriteAllText(HotUpdatePaths.StateFilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[热更] 状态写入失败（不影响本次游戏）：{exception.Message}");
            }
        }

        /// <summary>取已下载 catalog 的完整路径；没有则返回空字符串。</summary>
        public string GetCatalogPath()
        {
            if (string.IsNullOrEmpty(contentVersion) || string.IsNullOrEmpty(catalogFileName))
            {
                return string.Empty;
            }

            var path = HotUpdatePaths.GetVersionFilePath(contentVersion, catalogFileName);
            return File.Exists(path) ? path : string.Empty;
        }
    }
}
