using System;
using System.IO;
using UnityEngine;

namespace RaidDemo.HotUpdate
{
    /// <summary>
    /// 热更在本地磁盘上的目录约定。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么放在 <c>persistentDataPath</c> 而不是安装目录：</b>
    /// 安装目录可能被装在 Program Files 这类需要管理员权限的位置；
    /// <c>persistentDataPath</c> 是当前用户可写的固定位置，且不随游戏本体更新被覆盖——
    /// 这正是"下载下来的内容要活过本体更新"所需要的性质。</para>
    ///
    /// <para><b>目录结构</b>：</para>
    /// <code>
    /// &lt;persistentDataPath&gt;/content/
    ///     state.json            ← 本地记录（已下载的内容版本、catalog 文件名）
    ///     &lt;版本&gt;/               ← 按内容版本分目录（换版本时互不干扰，便于回退）
    ///         catalog_*.bin / *.hash / *.bundle
    /// </code>
    ///
    /// <para><b>为什么按版本分目录而不是就地覆盖：</b>下载到一半失败时，
    /// 旧版本的目录仍然完整可用——"更新失败必须能继续玩"这条要求就落在这里。</para>
    /// </remarks>
    public static class HotUpdatePaths
    {
        /// <summary>内容根目录名（位于 persistentDataPath 之下）。</summary>
        public const string ContentRootName = "content";

        /// <summary>本地状态文件名。</summary>
        public const string StateFileName = "state.json";

        /// <summary>取内容根目录。</summary>
        public static string ContentRoot
        {
            get { return Path.Combine(Application.persistentDataPath, ContentRootName); }
        }

        /// <summary>取本地状态文件路径。</summary>
        public static string StateFilePath
        {
            get { return Path.Combine(ContentRoot, StateFileName); }
        }

        /// <summary>取某个内容版本的目录。</summary>
        /// <param name="version">内容版本号。</param>
        public static string GetVersionDirectory(string version)
        {
            return Path.Combine(ContentRoot, Sanitize(version));
        }

        /// <summary>取某个内容版本目录下某个文件的完整路径。</summary>
        /// <param name="version">内容版本号。</param>
        /// <param name="fileName">文件名（清单里的相对路径最后一段）。</param>
        public static string GetVersionFilePath(string version, string fileName)
        {
            return Path.Combine(GetVersionDirectory(version), Sanitize(fileName));
        }

        /// <summary>确保目录存在并返回它。</summary>
        /// <param name="directory">目录路径。</param>
        public static string EnsureDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return directory;
        }

        /// <summary>
        /// 把版本号 / 文件名清洗成安全的单层名字。
        /// </summary>
        /// <remarks>
        /// 版本号来自远端清单，属于**不可信输入**：里面若含路径分隔符或 <c>..</c>，
        /// 直接拼路径就会写到内容目录之外。这里统一拒绝，只允许单层安全名字。
        /// </remarks>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (character == '/' || character == '\\' || Array.IndexOf(invalid, character) >= 0)
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(character);
                }
            }

            var result = builder.ToString().Replace("..", "_");
            return string.IsNullOrEmpty(result) ? "unknown" : result;
        }
    }
}
