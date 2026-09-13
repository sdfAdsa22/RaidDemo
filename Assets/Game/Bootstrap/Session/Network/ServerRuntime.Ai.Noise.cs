using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的 AI 噪音部分：把"谁发出了多大声"喂给 AI 调度器。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么噪音要单独一个文件：</b>它是"世界规则"而不是某个系统的职责——
    /// 同时涉及玩家的移动状态（脚步）与武器数据（枪声），却不属于其中任何一方。
    /// 客户端的噪音装配（<c>SceneBootstrap.Noise.cs</c>）也是同样的划分。</para>
    ///
    /// <para><b>两端必须用同一套规则：</b>服务器算出来的听觉判定就是权威判定，
    /// 若两边的半径或档位分叉，玩家会遇到"我在客户端明明听得见的距离开枪，服务器上的敌人却没反应"
    /// 这类无法解释的现象。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>脚步噪音的广播间隔（秒），与客户端保持一致。</summary>
        private const float FootstepNoiseInterval = 0.25f;

        private float m_FootstepNoiseTimer;
        private int m_AiEnemyShots;

        /// <summary>敌人累计开火次数。验收模式的状态上报会带上它（见 ServerRuntime.Ai.Report.cs）。</summary>
        public int EnemyShotCount
        {
            get { return m_AiEnemyShots; }
        }

        /// <summary>
        /// 把玩家的脚步噪音喂给 AI。
        /// </summary>
        /// <remarks>
        /// <para>噪音是持续状态而不是瞬时事件，因此按固定间隔采样广播，而不是每帧发布——
        /// 每帧广播会让噪音刷满事件总线，排查其它问题时看不到有用信息。</para>
        ///
        /// <para>档位用的是与客户端同一套规则（<see cref="MovementNoiseRules"/>）：
        /// 速度超过奔跑阈值就是奔跑声；超载时"走得慢也算大声"。
        /// 服务器目前不搬背包，因此超载恒为 false——装备搬运是 P5 的事。</para>
        /// </remarks>
        private void TickFootstepNoise(float deltaTime)
        {
            m_FootstepNoiseTimer -= deltaTime;
            if (m_FootstepNoiseTimer > 0f)
            {
                return;
            }

            m_FootstepNoiseTimer = FootstepNoiseInterval;

            foreach (var pair in m_PlayerBodies)
            {
                if (!m_World.TryGetSnapshot(pair.Key, out var snapshot) || !IsPlayerAlive(pair.Key))
                {
                    continue;
                }

                var tier = MovementNoiseRules.Classify(
                    snapshot.State.CurrentSpeed,
                    m_World.Profile.SprintSpeedThreshold,
                    isOverloaded: false);

                if (tier == NoiseTier.Silent)
                {
                    continue;
                }

                var combatantId = m_Combat != null ? m_Combat.GetCombatantId(pair.Key) : 0;
                m_AiDirector.ReportNoise(new NoiseEvent(
                    combatantId,
                    snapshot.State.Position,
                    tier,
                    m_AiDirector.Profile.HearingRadiusFor(tier)));
            }
        }

        /// <summary>
        /// 一发子弹 = 一次枪声噪音。
        /// </summary>
        /// <remarks>
        /// <para>与客户端同一规则：只认第一颗弹丸（霰弹一次开火会广播多条事件）、
        /// 半径取武器射程，因此"手枪比步枪安静"这条手感在联机里同样成立。</para>
        ///
        /// <para><b>敌人的枪声走同一条路：</b>AI 开火发布的是同一个 <see cref="WeaponFiredEvent"/>，
        /// 因此"打枪会招来更多敌人"对敌我双方都成立，不需要为 AI 单开一条规则。</para>
        /// </remarks>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (m_AiDirector == null || evt.PelletIndex != 0)
            {
                return;
            }

            m_AiDirector.ReportNoise(new NoiseEvent(
                evt.ShooterId,
                new Vector2F(evt.Origin.x, evt.Origin.z),
                NoiseTier.Gunshot,
                evt.NoiseRadiusMeters));

            // 敌人开火次数供验收模式的状态上报使用：它是"AI 真的打出来了"最直接的计数。
            if (IsEnemyCombatant(evt.ShooterId))
            {
                m_AiEnemyShots++;
            }
        }
    }
}
