using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    public static class LobbyText
    {
        /// <summary>昵称最大长度（与 <c>LobbyLimits.MaxNicknameLength</c> 一致）。</summary>
        public const int MaxNicknameLength = 16;

        /// <summary>房间名最大长度（与 <c>LobbyLimits.MaxRoomNameLength</c> 一致）。</summary>
        public const int MaxRoomNameLength = 24;

        /// <summary>
        /// 会被写进协议字段的文本的 UTF-8 字节上限（与服务器侧共用同一条规则）。
        /// </summary>
        /// <remarks>
        /// 界面层是**预校验**：它必须和服务器用同一把尺子，否则玩家会遇到"输入框接受了、提交被拒绝"。
        /// 规则本体放在 <c>RaidDemo.Shared.LobbyTextBudget</c>，`UI` 与 `Bootstrap` 都引用它。
        /// </remarks>
        public const int ProtocolTextByteCapacity = RaidDemo.Shared.LobbyTextBudget.FixedString64ByteCapacity;

        /// <summary>房间密码位数（与 <c>LobbyLimits.RoomPasswordDigits</c> 一致）。</summary>
        public const int RoomPasswordDigits = 4;

        /// <summary>口令最少位数。</summary>
        public const int MinPassphraseDigits = 4;

        /// <summary>口令最多位数。</summary>
        public const int MaxPassphraseDigits = 6;

        /// <summary>地址输入框长度上限。</summary>
        public const int MaxAddressLength = 28;

        /// <summary>端口输入框长度上限。</summary>
        public const int MaxPortLength = 5;

        /// <summary>列表行展示文本的长度上限（超出用省略号）。</summary>
        public const int ListTextLength = 28;

        /// <summary>昵称是否合法：1~16 个字符、不超过协议字节容量，且不含分隔符与控制字符。</summary>
        /// <param name="text">昵称。</param>
        public static bool IsValidNickname(string text)
        {
            return IsValidText(text, 1, MaxNicknameLength, ProtocolTextByteCapacity);
        }

        /// <summary>房间名是否合法：1~24 个字符，且不超过协议字节容量（汉字按 3 字节算，约 20 个）。</summary>
        /// <param name="text">房间名。</param>
        public static bool IsValidRoomName(string text)
        {
            return IsValidText(text, 1, MaxRoomNameLength, ProtocolTextByteCapacity);
        }

        /// <summary>口令是否合法：4~6 位数字。</summary>
        /// <param name="text">口令。</param>
        public static bool IsValidPassphrase(string text)
        {
            return IsDigits(text, MinPassphraseDigits, MaxPassphraseDigits);
        }

        /// <summary>房间密码是否合法：留空（不设密码）或恰好 4 位数字。</summary>
        /// <param name="text">房间密码。</param>
        public static bool IsValidRoomPassword(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            return IsDigits(text, RoomPasswordDigits, RoomPasswordDigits);
        }

        /// <summary>把口令 / 密码变成圆点串，避免旁人在屏幕上读到。</summary>
        /// <param name="text">原文。</param>
        public static string Mask(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : new string('●', text.Length);
        }

        /// <summary>按长度截断，超出部分用省略号代替。</summary>
        /// <param name="text">原文。</param>
        /// <param name="maxLength">最大长度（含省略号）。</param>
        public static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0)
            {
                return string.Empty;
            }

            if (text.Length <= maxLength)
            {
                return text;
            }

            return maxLength == 1 ? "…" : text.Substring(0, maxLength - 1) + "…";
        }

        /// <summary>扫描结果列表的一行文本。</summary>
        /// <param name="room">房间。</param>
        public static string DescribeScanRow(in MultiplayerMenuRoom room)
        {
            var body = string.IsNullOrWhiteSpace(room.Description)
                ? room.Address + ":" + room.Port
                : room.Description;

            body = Truncate(body, ListTextLength);
            return room.Joinable ? body : body + "（不可加入）";
        }

        /// <summary>没有扫描结果时的占位文本。</summary>
        /// <param name="roomCount">结果数量。</param>
        public static string DescribeRoomList(int roomCount)
        {
            return roomCount <= 0 ? "未发现房间，可手输地址连接" : $"已发现 {roomCount} 个房间（不刷新，点「扫描」重来）";
        }

        /// <summary>房间界面顶部的房间状态文本。</summary>
        /// <param name="roomName">房间名。</param>
        /// <param name="hasPassword">是否设了密码。</param>
        /// <param name="isInRaid">是否已在战局中。</param>
        /// <param name="memberCount">成员数。</param>
        public static string DescribeRoomState(string roomName, bool hasPassword, bool isInRaid, int memberCount)
        {
            if (string.IsNullOrEmpty(roomName))
            {
                return "尚未加入任何房间";
            }

            var phase = isInRaid ? "战局中" : "等待中";
            var password = hasPassword ? " · 有密码" : string.Empty;
            return $"房间「{Truncate(roomName, MaxRoomNameLength)}」（{memberCount}/{4} · {phase}{password}）";
        }

        /// <summary>成员列表的一行文本。</summary>
        /// <param name="nickname">昵称。</param>
        /// <param name="isHost">是否房主。</param>
        /// <param name="isSelf">是否本机玩家。</param>
        public static string DescribeMember(string nickname, bool isHost, bool isSelf)
        {
            var name = string.IsNullOrEmpty(nickname) ? "（未登录）" : Truncate(nickname, MaxNicknameLength);
            var marks = string.Empty;
            if (isHost)
            {
                marks += " · 房主";
            }

            if (isSelf)
            {
                marks += " · 我";
            }

            return name + marks;
        }

        /// <summary>
        /// 默认昵称的递增序号。
        /// </summary>
        /// <remarks>
        /// <para>用进程内计数器而不是随机数：<b>连续两次取名字必须不同</b>——
        /// 否则同一台机器上开两个客户端会撞名（服务器会以"该昵称已在游戏中"拒绝第二个）。</para>
        ///
        /// <para>起始值掺入时间，避免每次启动都是 Player1001，看上去像固定的测试账号。</para>
        ///
        /// <para>刻意不用 <c>UnityEngine.Random</c>：那是战局里决定掉落与 AI 行为的同一条随机流，
        /// 界面顺手取一次就会让"同一局重复跑出不同结果"，破坏可复现性验证。</para>
        /// </remarks>
        private static int s_NicknameSequence =
            1000 + (int)(System.DateTime.UtcNow.Ticks % 8000);

        /// <summary>生成一个默认昵称：玩家不必先想名字，也保证同一进程内不重名。</summary>
        public static string SuggestNickname()
        {
            var number = System.Threading.Interlocked.Increment(ref s_NicknameSequence);

            // 只保留四位数区间，名字长度稳定在 "Player" + 4 位以内。
            var normalized = 1000 + ((number % 9000) + 9000) % 9000;
            return "Player" + normalized;
        }

        /// <summary>文本合法性：去空白后长度在范围内，且不含竖线与控制字符。</summary>
        /// <summary>文本合法性：字符数在范围内、UTF-8 字节数不超协议容量，且不含分隔符与控制字符。</summary>
        private static bool IsValidText(string text, int minLength, int maxLength, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (trimmed.Length < minLength || trimmed.Length > maxLength)
            {
                return false;
            }

            // 字符数与字节数是两把尺子（'A' 1 字节、汉字 3 字节），协议字段按字节算容量。
            if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > maxBytes)
            {
                return false;
            }

            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (c == '|' || char.IsControl(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>全部为 ASCII 数字且位数在范围内。</summary>
        private static bool IsDigits(string text, int minLength, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length < minLength || text.Length > maxLength)
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
