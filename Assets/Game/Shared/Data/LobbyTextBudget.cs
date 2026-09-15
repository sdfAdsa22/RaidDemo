using System.Text;

namespace RaidDemo.Shared
{
    /// <summary>
    /// 大厅文本的"字节预算"：会被写进定长协议字段的那些字符串到底能装多少。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成一类：</b>同一条规则要被三处使用——协议层（<c>LobbyLimits</c> 的
    /// 服务器 / 客户端校验）、界面层（<c>LobbyText</c> 的输入预校验）、以及将来的局域网发现文本协议。
    /// 而 `UI` 不引用 `Bootstrap`，规则没法只写在一处再被另一处调用；
    /// 放在双方都引用的 <c>Shared</c> 里，才不会出现"界面允许输入、服务器拒绝"这种自相矛盾
    /// （`RD-AUD-053` 与 `RD-AUD-066` 的重复校验问题都由此而来）。</para>
    ///
    /// <para><b>为什么按字节而不是按字符：</b>协议字段是 <c>FixedString64Bytes</c>，
    /// 容量按 UTF-8 字节算（64 字节里要留 2 字节长度前缀与 1 字节结束符，可用 61 字节）。
    /// 'A' 占 1 字节，汉字通常占 3 字节——只按字符数校验时，24 个汉字的房间名约 72 字节，
    /// 写进协议会被**静默截断**：玩家看到名字少了几个字，日志里却什么都没有。</para>
    /// </remarks>
    public static class LobbyTextBudget
    {
        /// <summary>
        /// <c>FixedString64Bytes</c> 的可用字节数（64 减去长度前缀与结束符）。
        /// </summary>
        public const int FixedString64ByteCapacity = 61;

        /// <summary>
        /// 文本能否装进 <c>FixedString64Bytes</c>。
        /// </summary>
        /// <param name="text">待检查的文本。</param>
        /// <returns>UTF-8 字节数不超过 <see cref="FixedString64ByteCapacity"/> 时返回 true。</returns>
        public static bool FitsInFixedString64(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            return Encoding.UTF8.GetByteCount(text) <= FixedString64ByteCapacity;
        }
    }
}
