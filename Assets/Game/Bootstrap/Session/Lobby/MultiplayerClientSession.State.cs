using System.Text.RegularExpressions;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机会话的状态与提示：阶段切换、错误文本、进 / 出战局、地址解析。
    /// </summary>
    /// <remarks>
    /// <para>这些方法都只改状态并触发一次界面刷新事件。把它们集中在一处，
    /// 是为了保证"状态变了就一定通知界面"——漏一处就会出现界面停在旧状态的问题。</para>
    /// </remarks>
    public sealed partial class MultiplayerClientSession
    {
        /// <summary>阶段的中文名（提示文本用）。</summary>
        public static string DescribePhase(MultiplayerClientPhase phase)
        {
            switch (phase)
            {
                case MultiplayerClientPhase.Connecting:
                    return "连接中";
                case MultiplayerClientPhase.LoggingIn:
                    return "登录中";
                case MultiplayerClientPhase.InLobby:
                    return "已登录";
                case MultiplayerClientPhase.InRoom:
                    return "房间中";
                case MultiplayerClientPhase.InRaid:
                    return "战局中";
                default:
                    return "未连接";
            }
        }

        /// <summary>切换阶段并通知界面。</summary>
        private void SetPhase(MultiplayerClientPhase phase)
        {
            Phase = phase;
            RaiseChanged();
        }

        /// <summary>记录一条错误并通知界面。</summary>
        internal void SetError(string message)
        {
            LastError = message ?? string.Empty;
            StatusText = string.Empty;
            UnityEngine.Debug.LogWarning($"[联机] {LastError}");
            RaiseChanged();
        }

        /// <summary>清空错误（发起新操作时调用）。</summary>
        private void ClearError()
        {
            LastError = string.Empty;
        }

        /// <summary>通知界面刷新。</summary>
        internal void RaiseChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>设置状态说明文本（进行中的操作）。</summary>
        internal void SetStatus(string text)
        {
            StatusText = text ?? string.Empty;
            RaiseChanged();
        }

        /// <summary>收到开局通知：进入战局阶段并让装配层加载地图。</summary>
        internal void EnterRaid(string mapSceneName)
        {
            if (m_RaidStartSeen)
            {
                return;
            }

            m_RaidStartSeen = true;
            SetPhase(MultiplayerClientPhase.InRaid);
            StatusText = "战局进行中";
            UnityEngine.Debug.Log($"[联机] 服务器通知开局，准备加载地图：{mapSceneName}");
            RaidStarting?.Invoke(mapSceneName);
        }

        /// <summary>
        /// 收到"本局结束"通知：回到房间状态，并要求装配层加载安全屋。
        /// </summary>
        /// <param name="sceneName">要返回的场景名；空则用安全屋。</param>
        /// <remarks>
        /// 房间保留（只是回到等待阶段），因此玩家可以再开一局——这就是 P4.5-b 的"战后回屋循环"。
        /// </remarks>
        internal void ReturnFromRaid(string sceneName)
        {
            m_RaidStartSeen = false;
            SetPhase(MultiplayerClientPhase.InRoom);
            StatusText = "已回到安全屋";

            var target = string.IsNullOrEmpty(sceneName) ? GameScenes.SafeHouse : sceneName;
            UnityEngine.Debug.Log($"[联机] 服务器通知本局结束，返回场景：{target}");
            RaidEnding?.Invoke(target);
        }

        /// <summary>拆分"主机[:端口]"。</summary>
        /// <param name="raw">原始地址文本。</param>
        /// <param name="valid">地址是否合法。</param>
        private static (string Host, ushort Port) SplitAddress(string raw, out bool valid)
        {
            valid = false;

            var text = string.IsNullOrWhiteSpace(raw) ? "127.0.0.1" : raw.Trim();
            var host = text;
            ushort port = LaunchOptions.DefaultPort;

            var separator = text.LastIndexOf(':');
            if (separator > 0)
            {
                if (!ushort.TryParse(text.Substring(separator + 1), out var explicitPort) || explicitPort == 0)
                {
                    return (text, 0);
                }

                host = text.Substring(0, separator).Trim();
                port = explicitPort;
            }

            if (host.Length == 0 || !HostPattern.IsMatch(host))
            {
                return (text, 0);
            }

            valid = true;
            return (host, port);
        }

        /// <summary>
        /// 主机名允许的字符集：字母数字、点、横线、下划线，以及 IPv6 的冒号与方括号。
        /// </summary>
        /// <remarks>
        /// 显式校验而不是直接交给传输层：非法字符在传输层会变成一个难以理解的内部异常，
        /// 而这里可以立刻给玩家一句"地址格式不对"。
        /// </remarks>
        private static readonly Regex HostPattern = new Regex(
            @"^[A-Za-z0-9\.\-_\[\]:]+$",
            RegexOptions.Compiled);
    }
}
