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

        /// <summary>上一次上报时各玩家的积压条数（只在变化时打日志，避免每秒刷屏）。</summary>
        private readonly Dictionary<int, int> m_LastReportedPending = new Dictionary<int, int>();

        /// <summary>下一次上报输入诊断的时刻。</summary>
        private float m_NextInputDiagnosticsTime;

        /// <summary>上报时复用的玩家编号缓冲。</summary>
        private readonly List<int> m_PlayerIdsBuffer = new List<int>(8);

        /// <summary>
        /// 每秒报一次输入健康度：被丢弃的条数 + 当前积压条数（Verbose；正常时只打积压）。
        /// </summary>
        /// <remarks>
        /// 丢输入会让"已处理序号 ↔ 实际执行步数"错开，直接把两端位置错开一步；
        /// 积压条数则反映"客户端发得比服务器消费快多少"。两个数字一起看，
        /// 就能判断"移动抖动"是不是输入侧的锅，不必再去猜网络。
        /// </remarks>
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
                if (!m_World.TryGetInputDiagnostics(playerId, out var dropped, out var pending))
                {
                    continue;
                }

                if (!m_InputDiagnosticsBaseline.TryGetValue(playerId, out var previous))
                {
                    previous = dropped;
                }

                var delta = dropped - previous;
                m_InputDiagnosticsBaseline[playerId] = dropped;

                if (delta > 0)
                {
                    // 丢输入是异常：任何一条都不该丢，因此按 Warning 打，任何日志级别都看得见。
                    m_Session.Log.Warning(
                        $"[服务器] 玩家 {playerId} 本秒丢了 {delta} 条输入（累计 {dropped} 条，"
                        + $"当前积压 {pending} 条）——丢输入会让两端位置错开，请检查是否被灌包。");
                }
                else if (m_LastReportedPending.GetValueOrDefault(playerId, -1) != pending)
                {
                    // 积压条数是"输入健康度"的常规指标：只在变化时记一行 Verbose，便于对照时间线。
                    m_Session.Log.Verbose(
                        $"[服务器][诊断] 玩家 {playerId}：待处理输入积压 {pending} 条（丢弃累计 {dropped} 条）。");
                }

                m_LastReportedPending[playerId] = pending;
            }
        }
    }
}
