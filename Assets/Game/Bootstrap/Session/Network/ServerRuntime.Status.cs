using System.Collections.Generic;
using RaidDemo.Kernel;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的状态快照部分：把"现在服务器上是什么情况"整理成一份只读数据。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>状态页、局域网发现与日志是同一类需求——
    /// 外界想知道服务器内部状态，但不该拿到内部对象。快照是这条边界的唯一出口。</para>
    ///
    /// <para><b>线程约束：只能在主线程调用。</b>它读房间、连接表与日志缓冲，
    /// 这些数据只在主线程被改写。状态页的 HTTP 线程只消费返回值。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>状态页里最多展示多少条最近日志。</summary>
        private const int StatusLogLimit = 50;

        /// <summary>生成一份状态快照（主线程调用）。</summary>
        internal ServerStatusSnapshot BuildStatusSnapshot()
        {
            var now = Time.realtimeSinceStartup;

            var snapshot = new ServerStatusSnapshot
            {
                UptimeSeconds = now,
                Port = m_Options.Port,
                Listening = IsListening,
                SaveDirectory = m_Options.SaveDirectory,
                Phase = (byte)m_Room.Phase,
                PhaseText = DescribeLobbyPhase(m_Room.Phase),
                RoomName = m_Room.Exists ? m_Room.RoomName : "（空闲：等待第一个客户端创建）",
                HasPassword = m_Room.HasPassword,
                HostNickname = ResolveHostNickname(),
                ConnectedPlayerCount = m_LobbyClients.Count,
                RaidElapsedSeconds = m_RaidStartedAt >= 0f ? now - m_RaidStartedAt : 0f,
            };

            snapshot.Addresses.Add($"{ServerAddressReporter.LoopbackAddress}:{m_Options.Port}");
            var lan = ServerAddressReporter.EnumerateLanIPv4();
            for (var i = 0; i < lan.Count; i++)
            {
                snapshot.Addresses.Add($"{lan[i]}:{m_Options.Port}");
            }

            foreach (var pair in m_LobbyClients)
            {
                var client = pair.Value;
                var member = m_Room.Find(pair.Key);

                snapshot.Members.Add(new ServerStatusMember
                {
                    ClientId = pair.Key,
                    Nickname = string.IsNullOrEmpty(client.Nickname) ? "（未登录）" : client.Nickname,
                    IsHost = member != null && member.IsHost,
                    InRoom = member != null,
                    StateText = DescribeClientState(client, member),
                });
            }

            var recent = m_Session?.Log != null
                ? m_Session.Log.SnapshotRecent(StatusLogLimit)
                : new List<RecentLogEntry>();

            for (var i = 0; i < recent.Count; i++)
            {
                snapshot.Logs.Add(new ServerStatusLogEntry
                {
                    Time = $"{recent[i].TimeSeconds:F1}s",
                    Level = DescribeLogLevel(recent[i].Level),
                    Message = recent[i].Message,
                });
            }

            return snapshot;
        }

        /// <summary>房主昵称；空闲阶段为空字符串。</summary>
        private string ResolveHostNickname()
        {
            var hostId = m_Room.HostClientId;
            if (hostId < 0)
            {
                return string.Empty;
            }

            return m_LobbyClients.TryGetValue(hostId, out var host) && !string.IsNullOrEmpty(host.Nickname)
                ? host.Nickname
                : $"玩家 {hostId}";
        }

        /// <summary>房间阶段的中文名。</summary>
        private static string DescribeLobbyPhase(LobbyPhase phase)
        {
            switch (phase)
            {
                case LobbyPhase.Waiting:
                    return "等待中";
                case LobbyPhase.InRaid:
                    return "战局中";
                default:
                    return "空闲";
            }
        }

        /// <summary>一名在线玩家的中文状态描述。</summary>
        private static string DescribeClientState(LobbyClient client, LobbyMember member)
        {
            if (!client.LoggedIn)
            {
                return "未登录";
            }

            if (member == null)
            {
                return "已登录（未进房间）";
            }

            return member.IsHost ? "在房间（房主）" : "在房间";
        }

        /// <summary>日志级别的中文名。</summary>
        private static string DescribeLogLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose:
                    return "详细";
                case LogLevel.Warning:
                    return "警告";
                case LogLevel.Error:
                    return "错误";
                case LogLevel.Fatal:
                    return "致命";
                default:
                    return "信息";
            }
        }
    }
}
