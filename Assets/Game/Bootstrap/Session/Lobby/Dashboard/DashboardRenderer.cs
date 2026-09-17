using System.Text;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 状态页的渲染：把一份 <see cref="ServerStatusSnapshot"/> 变成 HTML 或 JSON。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么渲染是纯函数：</b>状态页的正确性里，大部分是"字段有没有画出来、
    /// 特殊字符有没有转义、日志有没有按新到旧排列"这类确定的事。抽成纯函数之后，
    /// 它们可以在编辑模式里逐条断言，而不是靠人打开浏览器看一眼——那既慢，又看不出回归。</para>
    ///
    /// <para><b>为什么不引前端框架：</b>这是一台无头服务器上的一个只读页面，
    /// 页面本身要能在没有网络、没有 CDN 的机器上打开。一行内联 CSS 就够，
    /// 多余的依赖只会让"运维在云主机上看一眼"这件事变复杂。</para>
    /// </remarks>
    public static partial class DashboardRenderer
    {
        /// <summary>JSON 路由的 Content-Type。</summary>
        public const string JsonContentType = "application/json; charset=utf-8";

        /// <summary>HTML 路由的 Content-Type。</summary>
        public const string HtmlContentType = "text/html; charset=utf-8";

        /// <summary>纯文本路由的 Content-Type。</summary>
        public const string TextContentType = "text/plain; charset=utf-8";

        /// <summary>
        /// 渲染整页 HTML。
        /// </summary>
        /// <param name="snapshot">状态快照。</param>
        /// <param name="canControl">
        /// 当前请求是否来自本机（本机访问免口令）。
        /// </param>
        /// <param name="adminEnabled">服务器是否配置了管理口令（决定远程访问能不能操作）。</param>
        /// <param name="notice">一键操作执行后的提示文本；没有时为 null。</param>
        /// <param name="refreshSeconds">自动刷新间隔（秒）；0 表示不自动刷新。</param>
        public static string BuildHtml(
            ServerStatusSnapshot snapshot,
            bool canControl,
            bool adminEnabled = false,
            string notice = null,
            float refreshSeconds = 2f)
        {
            var html = new StringBuilder(4096);
            html.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");

            html.Append("<title>RaidDemo 服务器状态</title>");
            html.Append("<style>");
            html.Append("body{font-family:system-ui,'Segoe UI',sans-serif;background:#12211c;color:#e8e4d5;margin:0;padding:24px}");
            html.Append("h1{font-size:22px;margin:0 0 4px}h2{font-size:15px;margin:24px 0 8px;color:#9fb8a8}");
            html.Append("table{border-collapse:collapse;width:100%;font-size:14px}");
            html.Append("th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #24382f}");
            html.Append("th{color:#9fb8a8;font-weight:600}");
            html.Append(".card{background:#183028;border:1px solid #24382f;border-radius:10px;padding:16px;margin-bottom:16px}");
            html.Append(".badge{display:inline-block;padding:2px 10px;border-radius:999px;font-size:13px}");
            html.Append(".empty{color:#7d8f85}.warn{color:#ffb347}.err{color:#ff7b72}");
            html.Append(".btn{display:inline-block;margin:0 10px 6px 0;padding:8px 14px;border-radius:8px;background:#2c4a3c;color:#e8e4d5;text-decoration:none;border:0;cursor:pointer;font-size:14px;font-family:inherit}");
            html.Append(".btn:hover{background:#39604d}");
            html.Append("input{padding:8px 10px;border-radius:8px;border:1px solid #2c4a3c;background:#12211c;color:#e8e4d5;font-size:14px;min-width:220px;font-family:inherit}");
            html.Append("code{color:#b8d8c4}");
            html.Append("</style></head><body>");

            html.Append("<h1>RaidDemo 专用服务器</h1>");
            html.Append(
                $"<div class=\"empty\">运行 {FormatDuration(snapshot.UptimeSeconds)}　｜　端口 {snapshot.Port}　｜　"
                + (snapshot.Listening ? "监听中" : "<span class=\"err\">未监听</span>") + "</div>");

            if (!string.IsNullOrEmpty(notice))
            {
                html.Append($"<div class=\"card warn\">{Escape(notice)}</div>");
            }

            AppendRoomCard(html, snapshot);
            AppendPlayersCard(html, snapshot, canControl || adminEnabled);
            AppendLogsCard(html, snapshot);
            AppendAddressesCard(html, snapshot);
            AppendActionsCard(html, canControl, adminEnabled);

            html.Append("<p class=\"empty\">管理操作需要来自本机的请求，或在页面口令框输入服务器配置的管理口令（adminToken）。JSON 版本：<code>/status.json</code></p>");
            AppendAdminScript(html, refreshSeconds);
            html.Append("</body></html>");
            return html.ToString();
        }

        /// <summary>渲染 JSON 版本（与页面同源，供脚本与监控使用）。</summary>
        /// <param name="snapshot">状态快照。</param>
        public static string BuildJson(ServerStatusSnapshot snapshot)
        {
            // JsonUtility 能直接序列化带 [Serializable] 的类与其中的 List：
            // 快照类型本来就是为"跨线程只读拷贝"设计的，这里顺手复用同一份契约。
            return JsonUtility.ToJson(snapshot, prettyPrint: true);
        }

        /// <summary>把秒数写成人看得懂的时长。</summary>
        /// <param name="seconds">秒数。</param>
        public static string FormatDuration(float seconds)
        {
            var total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            var hours = total / 3600;
            var minutes = (total % 3600) / 60;
            var secs = total % 60;
            return hours > 0
                ? $"{hours} 小时 {minutes} 分"
                : $"{minutes} 分 {secs} 秒";
        }

        /// <summary>房间卡片：阶段、房名、房主、人数。</summary>
        private static void AppendRoomCard(StringBuilder html, ServerStatusSnapshot snapshot)
        {
            html.Append("<div class=\"card\">");
            html.Append("<h2>房间</h2>");

            var color = snapshot.Phase == (byte)LobbyPhase.InRaid ? "#ffb347"
                : snapshot.Phase == (byte)LobbyPhase.Waiting ? "#7fd18a"
                : "#7d8f85";
            html.Append($"<div><span class=\"badge\" style=\"background:{color};color:#12211c\">");
            html.Append(Escape(snapshot.PhaseText));
            html.Append("</span>");

            if (!string.IsNullOrEmpty(snapshot.RoomName))
            {
                html.Append($"　房间名：<b>{Escape(snapshot.RoomName)}</b>");
            }

            if (snapshot.HasPassword)
            {
                html.Append("　<span class=\"warn\">有密码</span>");
            }

            html.Append("</div>");

            html.Append("<table><tbody>");
            AppendRow(html, "房主", string.IsNullOrEmpty(snapshot.HostNickname) ? "（无）" : snapshot.HostNickname);
            AppendRow(html, "在线人数（含未进房）", snapshot.ConnectedPlayerCount.ToString());

            // P4.5-b：服务器会在"共享安全屋 ⇄ 战局"之间切换托管的世界，
            // 运维最常问的两个问题正好是"现在在哪张图"与"有几个人在局里"。
            var world = string.IsNullOrEmpty(snapshot.WorldScene)
                ? snapshot.WorldText
                : $"{snapshot.WorldText}（{snapshot.WorldScene}）";
            AppendRow(html, "当前世界", world);
            AppendRow(html, "在局人数（权威世界）", snapshot.PlayersInWorld.ToString());

            if (snapshot.RaidElapsedSeconds > 0f)
            {
                AppendRow(html, "本局已进行", FormatDuration(snapshot.RaidElapsedSeconds));
            }

            // P-51：传输层自愈的累计次数。验收脚本对两次采样取差值即可确认"自愈确实发生过"；
            // 对运维则是一个简单的健康读数——正常对局它应该一直是 0。
            AppendRow(html, "传输层自愈（P-51）", snapshot.TransportRebuildCount == 0
                ? "0 次"
                : $"{snapshot.TransportRebuildCount} 次");

            AppendRow(html, "服务器存档目录", snapshot.SaveDirectory);
            html.Append("</tbody></table></div>");
        }

        /// <summary>在线玩家卡片。</summary>
        /// <param name="html">输出目标。</param>
        /// <param name="snapshot">状态快照。</param>
        /// <param name="operable">是否画出"踢出"按钮（本机访问或配置了口令）。</param>
        private static void AppendPlayersCard(StringBuilder html, ServerStatusSnapshot snapshot, bool operable)
        {
            html.Append("<div class=\"card\"><h2>在线玩家</h2>");

            if (snapshot.Members == null || snapshot.Members.Count == 0)
            {
                html.Append("<div class=\"empty\">还没有客户端连接。</div></div>");
                return;
            }

            html.Append("<table><thead><tr><th>连接</th><th>昵称</th><th>状态</th><th>在线</th>");
            if (operable)
            {
                html.Append("<th>操作</th>");
            }

            html.Append("</tr></thead><tbody>");
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                var member = snapshot.Members[i];
                html.Append("<tr>");
                html.Append($"<td>{member.ClientId}</td>");
                html.Append($"<td>{Escape(member.Nickname)}{(member.IsHost ? " ★" : string.Empty)}</td>");
                html.Append($"<td>{Escape(member.StateText)}</td>");
                html.Append($"<td class=\"empty\">{Escape(FormatDuration(member.OnlineSeconds))}</td>");
                if (operable)
                {
                    AppendKickCell(html, member);
                }

                html.Append("</tr>");
            }

            html.Append("</tbody></table></div>");
        }

        /// <summary>玩家行里的操作单元格。</summary>
        /// <param name="html">输出目标。</param>
        /// <param name="member">目标玩家。</param>
        /// <remarks>
        /// 只有房间成员能踢：不在房间里的连接没有"房间席位"可移除，
        /// 而断线重连机制会让直接断开看起来"踢不掉"，所以那个入口干脆不提供。
        /// 昵称通过 data 属性传给脚本、不进 JS 字面量：它是玩家输入，转义规则只需要管一处。
        /// </remarks>
        private static void AppendKickCell(StringBuilder html, ServerStatusMember member)
        {
            if (!member.InRoom)
            {
                html.Append("<td class=\"empty\">—</td>");
                return;
            }

            html.Append($"<td><button class=\"btn\" data-nick=\"{Escape(member.Nickname)}\"");
            html.Append($" onclick=\"act('/?action=kick-player&amp;client={member.ClientId}', this)\">踢出</button></td>");
        }

        /// <summary>最近日志卡片。</summary>
        private static void AppendLogsCard(StringBuilder html, ServerStatusSnapshot snapshot)
        {
            html.Append("<div class=\"card\"><h2>最近日志</h2>");

            if (snapshot.Logs == null || snapshot.Logs.Count == 0)
            {
                html.Append("<div class=\"empty\">没有日志（服务器可能以 <code>-logLevel off</code> 启动）。</div></div>");
                return;
            }

            html.Append("<table><thead><tr><th>时刻</th><th>级别</th><th>内容</th></tr></thead><tbody>");
            for (var i = 0; i < snapshot.Logs.Count; i++)
            {
                var entry = snapshot.Logs[i];
                html.Append("<tr>");
                html.Append($"<td class=\"empty\">{Escape(entry.Time)}</td>");
                html.Append($"<td>{Escape(entry.Level)}</td>");
                html.Append($"<td>{Escape(entry.Message)}</td>");
                html.Append("</tr>");
            }

            html.Append("</tbody></table></div>");
        }

        /// <summary>可达地址卡片。</summary>
        private static void AppendAddressesCard(StringBuilder html, ServerStatusSnapshot snapshot)
        {
            html.Append("<div class=\"card\"><h2>连接地址</h2>");

            if (snapshot.Addresses == null || snapshot.Addresses.Count == 0)
            {
                html.Append("<div class=\"empty\">未检测到可用地址。</div></div>");
                return;
            }

            for (var i = 0; i < snapshot.Addresses.Count; i++)
            {
                html.Append($"<div><code>{Escape(snapshot.Addresses[i])}</code></div>");
            }

            html.Append("</div>");
        }

        /// <summary>写一行"标签 / 值"。</summary>
        private static void AppendRow(StringBuilder html, string label, string value)
        {
            html.Append($"<tr><th>{Escape(label)}</th><td>{Escape(value)}</td></tr>");
        }

        /// <summary>
        /// HTML 转义。
        /// </summary>
        /// <remarks>
        /// 昵称与房间名都是玩家输入，日志里也可能带上它们。
        /// 不转义就等于把"玩家能在服务器页面上注入标签"当成一个特性留着。
        /// </remarks>
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var escaped = new StringBuilder(text.Length + 16);
            for (var i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '&': escaped.Append("&amp;"); break;
                    case '<': escaped.Append("&lt;"); break;
                    case '>': escaped.Append("&gt;"); break;
                    case '"': escaped.Append("&quot;"); break;
                    case '\'': escaped.Append("&#39;"); break;
                    default: escaped.Append(text[i]); break;
                }
            }

            return escaped.ToString();
        }
    }
}
