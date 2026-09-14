using System.Collections.Generic;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器移动部分的输入诊断：把"客户端上行输入有没有被覆盖丢弃"变成可读的数字。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>客户端按固定 60 Hz 步进，帧率低于 60 时一帧会补多步、
    /// 连发多条输入；而服务器每个固定步只消费一条待处理输入，**更新的输入会直接覆盖旧的**。
    /// 被覆盖的那几条在客户端都已经记过预测，于是"已处理序号"与"实际执行步数"错开，
    /// 表现为客户端位置稳定地领先服务器一步——超过对账容差后客户端每次快照都硬吸附一次，
    /// 玩家看到的就是"移动时一卡一卡、时不时短距离瞬移"。</para>
    ///
    /// <para><b>实测（2026-09-14，编辑器当客户端 + 无头服务器）：</b>客户端 ~60 FPS 时
    /// 该计数一秒都不增长；把同一客户端压到 20 FPS 后，每秒被覆盖 12~13 条。
    /// 这正是"只有房主一卡一卡、加进来的玩家正常"的来源：低帧率那一端才会在一帧里补多步。</para>
    ///
    /// <para>日志只在 Verbose 下打，且没有发生覆盖时一行都不写，正常游玩不会刷屏。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>上一次上报时各玩家的覆盖计数。</summary>
        private readonly Dictionary<int, int> m_InputDiagnosticsBaseline = new Dictionary<int, int>();

        /// <summary>下一次上报输入诊断的时刻。</summary>
        private float m_NextInputDiagnosticsTime;

        /// <summary>上报时复用的玩家编号缓冲。</summary>
        private readonly List<int> m_PlayerIdsBuffer = new List<int>(8);

        /// <summary>
        /// 每秒报一次"待处理输入被覆盖丢弃"的条数（Verbose；没有覆盖时不打）。
        /// </summary>
        private void ReportInputDiagnostics()
        {
            if (m_Session == null || !m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                return;
            }

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now < m_NextInputDiagnosticsTime)
            {
                return;
            }

            m_NextInputDiagnosticsTime = now + 1f;

            m_PlayerIdsBuffer.Clear();
            m_World.GetPlayerIds(m_PlayerIdsBuffer);
            for (var i = 0; i < m_PlayerIdsBuffer.Count; i++)
            {
                var playerId = m_PlayerIdsBuffer[i];
                if (!m_World.TryGetInputDiagnostics(playerId, out var coalesced))
                {
                    continue;
                }

                if (!m_InputDiagnosticsBaseline.TryGetValue(playerId, out var previous))
                {
                    previous = coalesced;
                }

                var delta = coalesced - previous;
                m_InputDiagnosticsBaseline[playerId] = coalesced;

                if (delta > 0)
                {
                    m_Session.Log.Verbose(
                        $"[服务器][诊断] 玩家 {playerId}：本秒被覆盖丢弃的未消费输入 {delta} 条"
                        + $"（累计 {coalesced} 条）。");
                }
            }
        }
    }
}
