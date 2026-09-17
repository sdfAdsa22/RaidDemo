using System;
using System.Collections.Generic;

namespace RaidDemo.Bootstrap
{
    /// <summary>状态页的一条 HTTP 响应。</summary>
    public readonly struct DashboardResponse
    {
        /// <summary>创建一条响应。</summary>
        /// <param name="statusCode">HTTP 状态码。</param>
        /// <param name="contentType">Content-Type。</param>
        /// <param name="body">正文。</param>
        /// <param name="actionTaken">本次请求执行的动作名；没有执行时为 null。</param>
        public DashboardResponse(int statusCode, string contentType, string body, string actionTaken = null)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Body = body ?? string.Empty;
            ActionTaken = actionTaken;
        }

        /// <summary>HTTP 状态码。</summary>
        public int StatusCode { get; }

        /// <summary>Content-Type。</summary>
        public string ContentType { get; }

        /// <summary>正文。</summary>
        public string Body { get; }

        /// <summary>本次请求执行的动作名（日志用）；没有时为 null。</summary>
        public string ActionTaken { get; }
    }

    /// <summary>
    /// 状态页的路由：把一行 HTTP 请求翻译成一份响应。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么自己写这一小段而不是引 HTTP 服务：</b>需要的只有几个只读路由与三个运维动作。
    /// 引入 <c>HttpListener</c> 反而要先在 Windows 上做 URL ACL（需要管理员），
    /// 而它在无头 Linux 上的行为又是另一套。这里用原始 TCP + 只解析请求行，
    /// 行为在两套系统上完全一致，也更容易在编辑模式里逐条断言。</para>
    ///
    /// <para><b>安全边界就一条：</b>写操作要么来自回环地址，要么带对管理口令。判断放在路由里（而不是界面上），
    /// 因为界面可以被绕过——curl 一句就能直接打过来。口令走 <c>X-Admin-Token</c> 请求头
    /// （页面里的 JS 负责带上它），也兼容查询串 <c>token=</c> 写法方便命令行排查。</para>
    /// </remarks>
    public static class DashboardRouter
    {
        /// <summary>解散房间（成员全部退出，回到空闲；不动服务器进程）。</summary>
        public const string StopRoomAction = "stop-room";

        /// <summary>停止服务器进程。</summary>
        public const string StopServerAction = "stop-server";

        /// <summary>踢出一名房间成员（参数 <c>client</c> = 玩家编号）。</summary>
        public const string KickPlayerAction = "kick-player";

        /// <summary>管理口令的请求头名（大小写不敏感）。</summary>
        public const string AdminTokenHeader = "X-Admin-Token";

        /// <summary>
        /// 处理一行请求。
        /// </summary>
        /// <param name="requestLine">形如 <c>GET /status.json HTTP/1.1</c>。</param>
        /// <param name="headers">请求头字段；没有时传 null。</param>
        /// <param name="remoteAddress">发起方的 IP（取自 socket，不信任请求头）。</param>
        /// <param name="snapshotProvider">取当前状态快照（必须由主线程执行）。</param>
        /// <param name="actionHandler">执行运维动作（动作名、参数）；返回 null 或空串表示成功。</param>
        /// <param name="adminToken">服务器配置的管理口令；空表示写操作只允许回环来源。</param>
        public static DashboardResponse Handle(
            string requestLine,
            IReadOnlyDictionary<string, string> headers,
            string remoteAddress,
            Func<ServerStatusSnapshot> snapshotProvider,
            Func<string, string, string> actionHandler,
            string adminToken = null)
        {
            if (!TryParseRequestLine(requestLine, out var method, out var path, out var query))
            {
                return new DashboardResponse(400, DashboardRenderer.TextContentType, "请求格式不正确。");
            }

            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return new DashboardResponse(405, DashboardRenderer.TextContentType, "只支持 GET。");
            }

            var action = ExtractAction(query);
            var isLocal = IsLoopback(remoteAddress);

            if (!string.IsNullOrEmpty(action))
            {
                return HandleAction(
                    action,
                    ExtractParameter(query, "client"),
                    ExtractToken(headers, query),
                    isLocal,
                    adminToken,
                    actionHandler);
            }

            switch (path)
            {
                case "/":
                case "/index.html":
                    return new DashboardResponse(
                        200,
                        DashboardRenderer.HtmlContentType,
                        DashboardRenderer.BuildHtml(
                            SafeSnapshot(snapshotProvider),
                            isLocal,
                            !string.IsNullOrEmpty(adminToken)));

                case "/status.json":
                    return new DashboardResponse(
                        200, DashboardRenderer.JsonContentType, DashboardRenderer.BuildJson(SafeSnapshot(snapshotProvider)));

                case "/healthz":
                    return new DashboardResponse(200, DashboardRenderer.TextContentType, "ok");

                case "/favicon.ico":
                    // 浏览器总会顺手要一次图标：回 204 而不是 404，日志里就不会每两秒多一行噪音。
                    return new DashboardResponse(204, DashboardRenderer.TextContentType, string.Empty);

                default:
                    return new DashboardResponse(404, DashboardRenderer.TextContentType, "没有这个页面。");
            }
        }

        /// <summary>
        /// 判断来源地址是否属于本机。
        /// </summary>
        /// <param name="address">IP 字符串。</param>
        /// <remarks>
        /// 回环整段（127.0.0.0/8）都算本机：Windows 上解析到的是 127.0.0.1，
        /// 而某些容器里会是 127.0.0.11 之类的地址，它们同样是"只有本机能来"的入口。
        /// </remarks>
        public static bool IsLoopback(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }

            if (string.Equals(address, "::1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(address, "0:0:0:0:0:0:0:1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return address.StartsWith("127.", StringComparison.Ordinal);
        }

        /// <summary>执行一个运维动作。</summary>
        /// <param name="action">动作名。</param>
        /// <param name="argument">动作参数（踢人时是玩家编号）。</param>
        /// <param name="requestToken">请求携带的管理口令；没有时为 null。</param>
        /// <param name="isLocal">请求是否来自回环地址。</param>
        /// <param name="adminToken">服务器配置的管理口令。</param>
        /// <param name="actionHandler">动作处理器。</param>
        private static DashboardResponse HandleAction(
            string action,
            string argument,
            string requestToken,
            bool isLocal,
            string adminToken,
            Func<string, string, string> actionHandler)
        {
            if (action != StopRoomAction && action != StopServerAction && action != KickPlayerAction)
            {
                return new DashboardResponse(400, DashboardRenderer.TextContentType, $"未知操作：{action}");
            }

            var tokenConfigured = !string.IsNullOrEmpty(adminToken);
            var tokenAccepted = tokenConfigured
                && !string.IsNullOrEmpty(requestToken)
                && string.Equals(requestToken, adminToken, StringComparison.Ordinal);

            if (!isLocal && !tokenAccepted)
            {
                // 回 403 而不是"假装成功"：运维看到明确拒绝，才知道要在页面上补口令（或走 SSH 隧道）。
                return new DashboardResponse(
                    403,
                    DashboardRenderer.TextContentType,
                    tokenConfigured
                        ? "写操作需要管理口令：请在页面顶部输入（或从服务器本机访问）。"
                        : "写操作只允许来自服务器本机（127.0.0.1）；服务器未配置管理口令（adminToken），无法远程操作。");
            }

            if (actionHandler == null)
            {
                return new DashboardResponse(503, DashboardRenderer.TextContentType, "服务器还没有准备好处理操作。");
            }

            var error = actionHandler(action, argument);
            var ok = string.IsNullOrEmpty(error);
            var text = ok
                ? DescribeActionSuccess(action)
                : $"操作失败：{error}";

            return new DashboardResponse(200, DashboardRenderer.TextContentType, text, action);
        }

        /// <summary>动作成功后的提示文案。</summary>
        private static string DescribeActionSuccess(string action)
        {
            switch (action)
            {
                case StopRoomAction:
                    return "房间已解散（成员全部回到已登录状态）。";
                case KickPlayerAction:
                    return "已踢出玩家（对方会看到提示，可立刻重新加入）。";
                default:
                    return "服务器正在停止，稍后本页将无法访问。";
            }
        }

        /// <summary>取管理口令：优先请求头，其次查询串（方便 curl 排查）。</summary>
        private static string ExtractToken(IReadOnlyDictionary<string, string> headers, string query)
        {
            if (headers != null
                && headers.TryGetValue(AdminTokenHeader, out var headerToken)
                && !string.IsNullOrEmpty(headerToken))
            {
                return headerToken;
            }

            return ExtractParameter(query, "token");
        }

        /// <summary>取快照；提供方缺失时回一份空快照而不是抛异常。</summary>
        private static ServerStatusSnapshot SafeSnapshot(Func<ServerStatusSnapshot> provider)
        {
            return provider != null ? provider() : new ServerStatusSnapshot();
        }

        /// <summary>解析 <c>GET /path?query HTTP/1.1</c>。</summary>
        private static bool TryParseRequestLine(string requestLine, out string method, out string path, out string query)
        {
            method = null;
            path = null;
            query = null;

            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return false;
            }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                return false;
            }

            method = parts[0];
            var target = parts[1];

            var mark = target.IndexOf('?');
            if (mark < 0)
            {
                path = target;
                query = string.Empty;
            }
            else
            {
                path = target.Substring(0, mark);
                query = target.Substring(mark + 1);
            }

            return path.StartsWith("/", StringComparison.Ordinal);
        }

        /// <summary>从查询串里取 <c>action</c> 参数。</summary>
        private static string ExtractAction(string query)
        {
            return ExtractParameter(query, "action");
        }

        /// <summary>从查询串里取一个参数值；没有时返回 null。</summary>
        /// <param name="query">查询串（<c>a=1&amp;b=2</c>）。</param>
        /// <param name="name">参数名。</param>
        private static string ExtractParameter(string query, string name)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            var prefix = name + "=";
            var segments = query.Split('&');
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return segment.Substring(prefix.Length);
                }
            }

            return null;
        }
    }
}
