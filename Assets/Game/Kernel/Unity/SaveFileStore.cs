using System;
using System.IO;
using UnityEngine;

namespace RaidDemo.Kernel
{
    /// <summary>
    /// 通用 JSON 存档文件服务。
    /// </summary>
    /// <remarks>
    /// <para><b>它只负责"把对象安全地写进文件、再从文件读回来"</b>，
    /// 不关心存档里有哪些字段。局外进度到 DTO 的映射由 Meta 模块负责。
    /// 这条边界让文件 IO 可以在测试里用临时目录单独验证。</para>
    ///
    /// <para>写入采用"临时文件 → 校验 → 替换 → 保留上一份备份"。
    /// 直接覆盖原文件时，一旦写到一半掉电，玩家会同时失去新存档和旧存档；
    /// 临时文件方案最坏情况也只是丢掉本次写入，旧存档仍然可读。</para>
    ///
    /// <para>路径来自 <c>Application.persistentDataPath</c>，代码中不含任何本机绝对路径。
    /// 克隆仓库到任意盘符、任意用户名下都能直接运行。</para>
    /// </remarks>
    public sealed class SaveFileStore
    {
        /// <summary>默认存档文件名。</summary>
        public const string DefaultFileName = "raid_demo_save.json";

        /// <summary>主存档路径。</summary>
        private readonly string m_FilePath;

        /// <summary>备份路径。主存档损坏时回退到它。</summary>
        private readonly string m_BackupPath;

        /// <summary>写入过程中的临时路径。</summary>
        private readonly string m_TempPath;

        /// <summary>
        /// 创建存档服务。
        /// </summary>
        /// <param name="directory">
        /// 存档目录。传 null 时使用 <c>Application.persistentDataPath</c>；
        /// 测试可以传入临时目录，避免碰玩家的真实存档。
        /// </param>
        /// <param name="fileName">文件名，默认 <see cref="DefaultFileName"/>。</param>
        public SaveFileStore(string directory = null, string fileName = DefaultFileName)
        {
            var root = string.IsNullOrEmpty(directory)
                ? Application.persistentDataPath
                : directory;
            var name = string.IsNullOrEmpty(fileName) ? DefaultFileName : fileName;

            m_FilePath = Path.Combine(root, name);
            m_BackupPath = m_FilePath + ".bak";
            m_TempPath = m_FilePath + ".tmp";
        }

        /// <summary>主存档文件路径。仅用于日志与测试断言。</summary>
        public string FilePath
        {
            get { return m_FilePath; }
        }

        /// <summary>主存档是否存在。</summary>
        public bool Exists
        {
            get { return File.Exists(m_FilePath); }
        }

        /// <summary>
        /// 读取存档。
        /// </summary>
        /// <typeparam name="T">存档 DTO 类型，必须是可被 JsonUtility 序列化的类。</typeparam>
        /// <param name="data">读出的数据；失败时为 null。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>读到有效数据返回 true。</returns>
        public bool TryLoad<T>(out T data, out string error)
            where T : class
        {
            data = null;
            if (!File.Exists(m_FilePath))
            {
                error = "存档不存在。";
                return false;
            }

            if (TryReadFile(m_FilePath, out data, out error))
            {
                return true;
            }

            // 主存档损坏时回退到上一次备份。
            if (File.Exists(m_BackupPath)
                && TryReadFile(m_BackupPath, out data, out var backupError))
            {
                error = null;
                Debug.LogWarning(
                    $"[RaidDemo] 主存档读取失败（{error}），已回退到上一份备份。"
                    + $"备份读取信息：{backupError ?? "无"}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 写入存档。
        /// </summary>
        /// <typeparam name="T">存档 DTO 类型。</typeparam>
        /// <param name="data">要写入的数据。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>写入成功返回 true。</returns>
        public bool Save<T>(T data, out string error)
            where T : class
        {
            error = null;
            if (data == null)
            {
                error = "存档数据为空，拒绝写入。";
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(m_FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonUtility.ToJson(data, prettyPrint: true);
                if (string.IsNullOrEmpty(json))
                {
                    error = "序列化结果为空。";
                    return false;
                }

                File.WriteAllText(m_TempPath, json);

                // 立刻读回并反序列化一次：能写不等于能读，校验失败就不能覆盖旧存档。
                if (!TryReadFile(m_TempPath, out T _, out var verifyError))
                {
                    error = "临时存档校验失败：" + verifyError;
                    TryDelete(m_TempPath);
                    return false;
                }

                if (File.Exists(m_FilePath))
                {
                    File.Copy(m_FilePath, m_BackupPath, overwrite: true);
                }

                File.Copy(m_TempPath, m_FilePath, overwrite: true);
                TryDelete(m_TempPath);
                return true;
            }
            catch (Exception exception)
            {
                error = $"写入存档失败：{exception.GetType().Name} — {exception.Message}";
                TryDelete(m_TempPath);
                return false;
            }
        }

        /// <summary>删除主存档、备份与临时文件。</summary>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>全部删除成功返回 true。</returns>
        public bool Delete(out string error)
        {
            error = null;
            try
            {
                TryDelete(m_TempPath);
                TryDelete(m_BackupPath);
                if (File.Exists(m_FilePath))
                {
                    File.Delete(m_FilePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"删除存档失败：{exception.GetType().Name} — {exception.Message}";
                return false;
            }
        }

        private static bool TryReadFile<T>(string path, out T data, out string error)
            where T : class
        {
            data = null;
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "文件不存在。";
                    return false;
                }

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "文件内容为空。";
                    return false;
                }

                data = JsonUtility.FromJson<T>(json);
                if (data == null)
                {
                    error = "反序列化结果为空。";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"{exception.GetType().Name} — {exception.Message}";
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 清理失败不应影响主流程；残留的临时文件会在下次写入时被覆盖。
            }
        }
    }
}
