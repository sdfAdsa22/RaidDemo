using System.Security.Cryptography;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 账号库的密码学小工具。
    /// </summary>
    /// <remarks>
    /// <para>拆出来的原因：账号库主文件同时承担"账号表 / 落盘 / 两段式登录"三件事，
    /// 已经顶到工程规范的单文件 400 行上限（AR-07 改完 416 行）；这些纯函数没有状态，
    /// 独立成文件最省心。</para>
    /// </remarks>
    public sealed partial class ServerIdentityStore
    {
        /// <summary>
        /// 定长字符串比较。
        /// </summary>
        /// <remarks>
        /// 常规的 <c>==</c> 会在第一个不同的字符处返回，理论上能通过计时差逐字节试出哈希。
        /// 这条通道延迟很高、噪声很大，但修它的成本是几行代码，没有理由留着。
        /// </remarks>
        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }

        /// <summary>取密码学安全的随机字节。</summary>
        private static byte[] RandomBytes(int count)
        {
            var buffer = new byte[count];
            RandomNumberGenerator.Fill(buffer);
            return buffer;
        }

        /// <summary>字节 → 小写十六进制。</summary>
        private static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string Digits = "0123456789abcdef";

            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = Digits[bytes[i] >> 4];
                chars[(i * 2) + 1] = Digits[bytes[i] & 0x0F];
            }

            return new string(chars);
        }
    }
}
