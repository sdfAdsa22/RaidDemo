using System.Collections.Generic;
using RaidDemo.AI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的 AI 验收辅助：把"AI 到底在做什么"写进日志。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>AI 在服务器上跑起来之后，客户端看不到任何过程——
    /// 只能看到结果。没有这几行日志的话，"AI 没动过"与"AI 动了但没被打中"从外部完全一样。</para>
    ///
    /// <para><b>两类痕迹，用途不同：</b></para>
    /// <list type="number">
    /// <item><description><b>状态变化</b>（进入交战）——证明"感知 → 状态机 → 追击"这条链路真的走通了，
    /// 而不是只跑了个空循环；</description></item>
    /// <item><description><b>周期性汇总</b>（存活数、交战数、位置与开火计数）——在 <c>-autowalk</c>
    /// 验收模式下每 5 秒一行，用来证明敌人确实在移动与射击（M9-P-14：要证明"这条分支被执行过"，
    /// 而不是"没报错"）。</description></item>
    /// </list>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>验收模式下 AI 状态上报的间隔（秒）。</summary>
        private const float AiReportInterval = 5f;

        private readonly HashSet<int> m_EngageReported = new HashSet<int>();
        private float m_NextAiReportTime;

        /// <summary>
        /// 每帧检查：第一次进入交战的敌人记一笔；验收模式下周期性汇总一次。
        /// </summary>
        private void ReportAiState()
        {
            ReportEngagements();

            if (!m_Options.AutoWalk || Time.unscaledTime < m_NextAiReportTime)
            {
                return;
            }

            m_NextAiReportTime = Time.unscaledTime + AiReportInterval;

            var agents = m_AiDirector.Agents;
            var engaging = 0;
            var investigating = 0;
            var alert = 0;

            for (var i = 0; i < agents.Count; i++)
            {
                switch (agents[i].CurrentState)
                {
                    case AiStateId.Engage:
                        engaging++;
                        break;
                    case AiStateId.Investigate:
                        investigating++;
                        break;
                    case AiStateId.Alert:
                        alert++;
                        break;
                }
            }

            // 只报一个敌人的坐标：日志要能一眼看出"位置在变"，而不是变成一张状态表。
            var first = agents.Count > 0 ? agents[0] : null;
            var sample = first != null
                ? $"{DescribeEntity(ResolveEntityId(first.CombatantId))} 在 " +
                  $"({first.Position.X:F1}, {first.Position.Y:F1})，状态 {first.CurrentState}"
                : "无敌人";

            m_Session?.Log.Info(
                $"[服务器] AI 状态：存活 {m_AiDirector.AliveCount}/{agents.Count}，" +
                $"交战 {engaging}／警惕 {alert}／调查 {investigating}；{sample}；" +
                $"敌人累计开火 {EnemyShotCount} 发。");
        }

        /// <summary>
        /// 第一次有敌人进入"交战"时记一笔。
        /// </summary>
        /// <remarks>
        /// 每个敌人只记一次（用战斗单位编号去重）：这是"AI 真的开始工作"的证据，
        /// 而不是每帧刷屏的状态表。
        /// </remarks>
        private void ReportEngagements()
        {
            var agents = m_AiDirector.Agents;

            for (var i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent.CurrentState != AiStateId.Engage || !m_EngageReported.Add(agent.CombatantId))
                {
                    continue;
                }

                m_Session?.Log.Info(
                    $"[服务器] {DescribeEntity(ResolveEntityId(agent.CombatantId))} 进入交战：" +
                    $"位置 ({agent.Position.X:F1}, {agent.Position.Y:F1})，" +
                    $"生命 {agent.Health:F0}/{agent.MaxHealth:F0}。");
            }
        }
    }
}
