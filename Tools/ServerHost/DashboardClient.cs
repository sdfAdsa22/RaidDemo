using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RaidDemo.ServerHost
{
    /// <summary>状态页 <c>/status.json</c> 里面板用得上的那几个读数。</summary>
    internal sealed class DashboardStatus
    {
        /// <summary>服务器是否正在监听。</summary>
        public bool Listening { get; set; }

        /// <summary>监听端口。</summary>
        public int Port { get; set; }

        /// <summary>房间阶段的中文名。</summary>
        public string PhaseText { get; set; }

        /// <summary>房间名；空闲时为空。</summary>
        public string RoomName { get; set; }

        /// <summary>当前托管的世界。</summary>
        public string WorldText { get; set; }

        /// <summary>在局人数（权威世界）。</summary>
        public int PlayersInWorld { get; set; }

        /// <summary>在线人数（含未进房）。</summary>
        public int ConnectedPlayerCount { get; set; }

        /// <summary>进程已运行秒数。</summary>
        public float UptimeSeconds { get; set; }

        /// <summary>拼一行给玩家看的摘要。</summary>
        /// <returns>形如 <c>空闲 ｜ 在线 0 ｜ 运行 12 秒</c> 的文本。</returns>
        public string ToSummary()
        {
            var room = string.IsNullOrWhiteSpace(RoomName) ? "未建房" : RoomName;
            return $"{PhaseText} ｜ {room} ｜ 在线 {ConnectedPlayerCount} ｜ 在局 {PlayersInWorld}"
                + $" ｜ 运行 {UptimeSeconds.ToString("0", CultureInfo.InvariantCulture)} 秒";
        }
    }

    /// <summary>
    /// 状态页的只读查询与停止动作。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么面板要连状态页而不是自己数：</b>服务器进程内的真实状态（房间阶段、在线人数、
    /// 托管的世界）只有它自己知道，而 <c>/status.json</c> 正是为外部观察准备的那份快照
    /// （它就是给浏览器与运维脚本看的）。面板自己再推断一遍，等于制造第二份真相。</para>
    ///
    /// <para><b>为什么停止走状态页：</b><c>?action=stop-server</c> 让服务器自己走正常的关闭流程
    /// （保存、通知客户端、释放端口）；直接杀进程只是"进程没了"，存档与在局玩家都要靠运气。
    /// 面板只在状态页不可用或超时的时候才退化为结束进程树。</para>
    ///
    /// <para><b>超时必须短：</b>状态页可能因为端口被占、服务器卡住而完全不响应。
    /// 玩家点了停止却看着界面发呆，比明确告诉他"状态页没响应、我已强杀"要糟得多。</para>
    /// </remarks>
    internal static class DashboardClient
    {
        /// <summary>停止服务器的动作名（与 DashboardRouter.StopServerAction 一致）。</summary>
        public const string StopServerAction = "stop-server";

        /// <summary>单次请求超时（毫秒）。</summary>
        private const int RequestTimeoutMilliseconds = 1500;

        /// <summary>复用的 HTTP 客户端。</summary>
        /// <remarks>
        /// 每次请求新建 <see cref="HttpClient"/> 会不断占用端口与套接字句柄，而面板是每两秒轮询一次的
        /// 长驻程序——这正好是官方文档里明确警告的用法。
        /// </remarks>
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        /// <summary>
        /// 读取状态页快照。
        /// </summary>
        /// <param name="baseUrl">状态页根地址。</param>
        /// <returns>快照；失败时为 null（调用方据此显示状态页不可用）。</returns>
        public static async Task<DashboardStatus> TryFetchStatusAsync(string baseUrl)
        {
            var json = await TryGetAsync(new Uri(new Uri(baseUrl), "status.json")).ConfigureAwait(true);
            if (json == null)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                return new DashboardStatus
                {
                    Listening = ReadBoolean(root, "Listening"),
                    Port = ReadInteger(root, "Port"),
                    PhaseText = ReadText(root, "PhaseText"),
                    RoomName = ReadText(root, "RoomName"),
                    WorldText = ReadText(root, "WorldText"),
                    PlayersInWorld = ReadInteger(root, "PlayersInWorld"),
                    ConnectedPlayerCount = ReadInteger(root, "ConnectedPlayerCount"),
                    UptimeSeconds = ReadNumber(root, "UptimeSeconds"),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// 请求服务器自己停下来。
        /// </summary>
        /// <param name="baseUrl">状态页根地址。</param>
        /// <returns>是否被接受（HTTP 200）。</returns>
        /// <remarks>
        /// 状态页只允许来自本机的写操作，所以从别的机器（哪怕是同一台面板的隧道）来会被 403 拒绝；
        /// 面板总是与服务器同机运行，命中这条边界只说明玩家在远程折腾。
        /// </remarks>
        public static async Task<bool> TryStopServerAsync(string baseUrl)
        {
            var url = new Uri(new Uri(baseUrl), "?action=" + StopServerAction);
            var body = await TryGetAsync(url).ConfigureAwait(true);
            return body != null;
        }

        /// <summary>发一个带超时的 GET；失败返回 null。</summary>
        private static async Task<string> TryGetAsync(Uri url)
        {
            using var cancellation = new CancellationTokenSource(RequestTimeoutMilliseconds);

            try
            {
                using var response = await Client
                    .GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellation.Token)
                    .ConfigureAwait(true);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellation.Token).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 连接被拒、超时、DNS 失败……对面板都是同一件事：状态页现在读不到。
                return null;
            }
        }

        /// <summary>按字段名（大小写不敏感）取属性——Unity 序列化出来的是 PascalCase。</summary>
        private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        /// <summary>读布尔字段。</summary>
        private static bool ReadBoolean(JsonElement root, string name)
        {
            return TryGetProperty(root, name, out var value) && value.ValueKind == JsonValueKind.True;
        }

        /// <summary>读整数字段。</summary>
        private static int ReadInteger(JsonElement root, string name)
        {
            return TryGetProperty(root, name, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;
        }

        /// <summary>读浮点字段。</summary>
        private static float ReadNumber(JsonElement root, string name)
        {
            return TryGetProperty(root, name, out var value) && value.TryGetSingle(out var parsed) ? parsed : 0f;
        }

        /// <summary>读文本字段。</summary>
        private static string ReadText(JsonElement root, string name)
        {
            if (!TryGetProperty(root, name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return value.GetString() ?? string.Empty;
        }
    }
}
