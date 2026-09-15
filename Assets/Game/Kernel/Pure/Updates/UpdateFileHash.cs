using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RaidDemo.Kernel.Updates
{
    /// <summary>
    /// 文件哈希工具：更新链路上所有"这个文件对不对"的判断都落在它身上。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是 SHA-256：</b>它既用作完整性校验（下载损坏、复制截断），
    /// 也用作"内容是否变化"的比对依据；MD5 已不适合，SHA-1 也已不推荐。
    /// 对 GB 级文件逐个计算会明显拖慢出包，但本项目单文件都在几十 MB 以内，
    /// 全量哈希的代价可以接受——换来的是"清单里的哈希永远可信"。</para>
    ///
    /// <para><b>为什么放在纯逻辑层：</b>构建期（Unity 编辑器）与运行时（游戏内校验热更包）
    /// 需要的是同一份实现，两边都用这一份可以避免"构建算出来和运行算出来不一样"这类
    /// 只能靠对拍发现的低级问题。</para>
    /// </remarks>
    public static class UpdateFileHash
    {
        /// <summary>读取缓冲大小：128 KB。太小会放大系统调用次数，太大对哈希速度没有帮助。</summary>
        private const int BufferSize = 128 * 1024;

        /// <summary>
        /// 计算文件的 SHA-256（小写十六进制）。
        /// </summary>
        /// <param name="filePath">文件路径（绝对或相对均可）。</param>
        /// <returns>64 个字符的小写十六进制哈希。</returns>
        /// <exception cref="FileNotFoundException">文件不存在。</exception>
        public static string ComputeFileHash(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("计算哈希失败：文件不存在。", filePath);
            }

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            {
                return ComputeStreamHash(stream);
            }
        }

        /// <summary>
        /// 计算字节内容的 SHA-256（小写十六进制）。用于测试与内存中的小数据。
        /// </summary>
        /// <param name="content">待计算的字节数组。</param>
        /// <returns>64 个字符的小写十六进制哈希。</returns>
        public static string ComputeBytesHash(byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(content));
            }
        }

        /// <summary>
        /// 计算字符串内容的 SHA-256（按 UTF-8 编码，小写十六进制）。
        /// </summary>
        /// <param name="text">待计算的文本。</param>
        /// <returns>64 个字符的小写十六进制哈希。</returns>
        public static string ComputeTextHash(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return ComputeBytesHash(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// 校验文件哈希是否与期望值一致（大小写不敏感）。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <param name="expectedHash">清单里的期望哈希。</param>
        /// <returns>一致返回 <c>true</c>；文件不存在或哈希不同返回 <c>false</c>（本方法不抛异常）。</returns>
        public static bool Verify(string filePath, string expectedHash)
        {
            if (string.IsNullOrEmpty(expectedHash) || !File.Exists(filePath))
            {
                return false;
            }

            var actual = ComputeFileHash(filePath);
            return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>计算流的哈希。</summary>
        private static string ComputeStreamHash(Stream stream)
        {
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToHex(sha.Hash);
            }
        }

        /// <summary>把哈希字节转成小写十六进制字符串。</summary>
        private static string ToHex(byte[] hash)
        {
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
