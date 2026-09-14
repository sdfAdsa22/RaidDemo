using System;
using System.Text;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 局域网发现协议的编解码：探测包与房间回包。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用文本而不是二进制：</b>这条通道只有几十字节、每秒几条，
    /// 性能不是问题；而文本让"抓到一个包却看不懂"变成可以直接读出来。
    /// 代价是必须处理分隔符注入——房间名里出现竖线会让字段错位，
    /// 因此两侧都过 <see cref="SanitizeField"/>。</para>
    ///
    /// <para><b>地址不放在包里：</b>回包的来源地址由 socket 给出，包里写地址可以被伪造
    /// （发一个"我在某地址"的包，别人就会去连一个不存在的地方）。</para>
    /// </remarks>
    public static class LanDiscoveryCodec
    {
        /// <summary>回包的字段分隔符。</summary>
        public const char Separator = '|';

        /// <summary>回包的字段数量（标记、房间名、端口、人数、上限、密码、阶段）。</summary>
        public const int ReplyFieldCount = 7;

        /// <summary>探测包的字节内容（UTF-8）。</summary>
        public static byte[] EncodeProbe()
        {
            return Encoding.UTF8.GetBytes(LanDiscoveryConstants.ProbeToken);
        }

        /// <summary>
        /// 编码一条房间回包。
        /// </summary>
        /// <param name="info">房间公告。</param>
        public static string EncodeReply(LanRoomInfo info)
        {
            if (info == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(64);
            builder.Append(LanDiscoveryConstants.ReplyToken).Append(Separator);
            builder.Append(SanitizeField(info.RoomName)).Append(Separator);
            builder.Append(info.GamePort).Append(Separator);
            builder.Append(info.PlayerCount).Append(Separator);
            builder.Append(info.MaxPlayers).Append(Separator);
            builder.Append(info.HasPassword ? '1' : '0').Append(Separator);
            builder.Append(info.Phase);
            return builder.ToString();
        }

        /// <summary>
        /// 解析一条房间回包。
        /// </summary>
        /// <param name="payload">回包文本。</param>
        /// <param name="fromAddress">回包来源地址（由调用方从 socket 取出）。</param>
        /// <param name="info">解析结果。</param>
        /// <returns>是合法回包时返回 true。</returns>
        /// <remarks>
        /// 任何不合法都返回 false 而不是抛异常：这条通道上什么包都可能收到
        /// （广播域里还有其它程序），解析失败必须是一条静默的"不是我们的包"。
        /// </remarks>
        public static bool TryParseReply(string payload, string fromAddress, out LanRoomInfo info)
        {
            info = null;

            if (string.IsNullOrEmpty(payload))
            {
                return false;
            }

            var parts = payload.Split(Separator);
            if (parts.Length != ReplyFieldCount)
            {
                return false;
            }

            if (!string.Equals(parts[0], LanDiscoveryConstants.ReplyToken, StringComparison.Ordinal))
            {
                return false;
            }

            if (!int.TryParse(parts[2], out var gamePort) || gamePort < 1 || gamePort > 65535)
            {
                return false;
            }

            if (!int.TryParse(parts[3], out var playerCount) || playerCount < 0)
            {
                return false;
            }

            if (!int.TryParse(parts[4], out var maxPlayers) || maxPlayers < 1 || maxPlayers > LobbyLimits.MaxPlayers)
            {
                return false;
            }

            if (parts[5] != "0" && parts[5] != "1")
            {
                return false;
            }

            if (!byte.TryParse(parts[6], out var phase) || phase > (byte)LobbyPhase.InRaid)
            {
                return false;
            }

            info = new LanRoomInfo
            {
                Address = fromAddress ?? string.Empty,
                RoomName = parts[1],
                GamePort = gamePort,
                PlayerCount = playerCount,
                MaxPlayers = maxPlayers,
                HasPassword = parts[5] == "1",
                Phase = phase,
            };

            return true;
        }

        /// <summary>
        /// 去掉会破坏协议或界面的字符（分隔符与控制字符）。
        /// </summary>
        /// <param name="text">原始文本。</param>
        /// <remarks>房间名是玩家可输入的：不过滤的话，一个带竖线的房间名就能让所有人的列表解析失败。</remarks>
        public static string SanitizeField(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == Separator || char.IsControl(c))
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
