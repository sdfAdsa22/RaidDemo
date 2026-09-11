using System.Collections.Generic;
using RaidDemo.AI;
using RaidDemo.Shared;

namespace RaidDemo.Diagnostics
{
    /// <summary>
    /// 一帧的调试数据：把所有要显示的东西先收集好，再交给各个绘制者。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么先收集、再绘制：</b>如果不这样做，视野锥、标签、面板会各自去读一遍
    /// AI 的状态、各自做一次检测复核。一份数据读三遍的后果不是性能，而是**不一致**——
    /// 同一帧里扇形按旧位置画、标签按新位置写，看起来就像"某个 AI 卡住了"。</para>
    ///
    /// <para>本类只读，不持有任何逻辑对象的写入权限。</para>
    /// </remarks>
    public sealed class AiDebugSnapshot
    {
        /// <summary>一个 AI 在本帧的调试信息。</summary>
        public readonly struct Entry
        {
            /// <summary>创建一条调试条目。</summary>
            public Entry(
                AiAgent agent,
                AiDetectionResult detection,
                bool hasMemory,
                float memoryRemainingSeconds,
                float timeInState)
            {
                Agent = agent;
                Detection = detection;
                HasMemory = hasMemory;
                MemoryRemainingSeconds = memoryRemainingSeconds;
                TimeInState = timeInState;
            }

            /// <summary>逻辑层的 AI 单位。</summary>
            public AiAgent Agent { get; }

            /// <summary>检测复核结果（含失败原因）。</summary>
            public AiDetectionResult Detection { get; }

            /// <summary>是否还记得目标的位置。</summary>
            public bool HasMemory { get; }

            /// <summary>记忆还剩多少秒失效。没有记忆时为 0。</summary>
            public float MemoryRemainingSeconds { get; }

            /// <summary>在当前状态中已经停留的时长（秒）。</summary>
            public float TimeInState { get; }
        }

        private readonly List<Entry> m_Entries = new List<Entry>(8);

        /// <summary>存活的 AI 条目。</summary>
        public IReadOnlyList<Entry> Entries
        {
            get { return m_Entries; }
        }

        /// <summary>当前目标（玩家）快照。</summary>
        public AiTargetInfo Target { get; private set; }

        /// <summary>玩家当前的噪音档位。</summary>
        public NoiseTier PlayerNoiseTier { get; private set; }

        /// <summary>玩家当前档位对应的可听半径（米）。</summary>
        public float PlayerNoiseRadiusMeters { get; private set; }

        /// <summary>调度器的战局时钟（秒）。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>感知参数。面板与绘制都需要它。</summary>
        public AIPerceptionProfile Profile { get; private set; }

        /// <summary>存活 AI 数量。</summary>
        public int AliveAgentCount
        {
            get { return m_Entries.Count; }
        }

        /// <summary>玩家当前是否被 AI 视为可见（任一 AI 看得见即为真）。</summary>
        public bool PlayerSpotted
        {
            get
            {
                for (var i = 0; i < m_Entries.Count; i++)
                {
                    if (m_Entries[i].Detection.Sees)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 收集一帧的调试数据。
        /// </summary>
        /// <param name="context">世界状态入口。</param>
        public void Refresh(IAiDebugContext context)
        {
            m_Entries.Clear();
            if (context == null)
            {
                return;
            }

            var director = context.Director;
            if (director == null)
            {
                return;
            }

            Profile = director.Profile;
            Target = context.Target;
            PlayerNoiseTier = context.PlayerNoiseTier;
            PlayerNoiseRadiusMeters = Profile != null ? Profile.HearingRadiusFor(PlayerNoiseTier) : 0f;
            ElapsedSeconds = director.ElapsedSeconds;

            var agents = director.Agents;
            for (var i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];

                // 只收集存活单位：阵亡的 AI 不再行动，画它的视野锥只会让画面变乱。
                if (agent == null || !agent.IsAlive)
                {
                    continue;
                }

                var detection = AiDetectionDiagnostics.Evaluate(
                    context.Target,
                    agent.Position,
                    agent.Facing,
                    Profile,
                    context.Probe);

                var memory = agent.Context?.Memory;
                var hasMemory = memory != null && memory.HasMemory;
                var memoryRemaining = hasMemory && Profile != null
                    ? UnityEngine.Mathf.Max(0f, Profile.MemorySeconds - memory.TimeSinceLastKnown(ElapsedSeconds))
                    : 0f;

                m_Entries.Add(new Entry(agent, detection, hasMemory, memoryRemaining, agent.TimeInState));
            }
        }
    }
}
