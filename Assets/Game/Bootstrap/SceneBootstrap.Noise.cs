using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的噪音装配与广播部分：脚步与枪声。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一个文件：</b>一是主文件接近行数上限，二是"什么声音会被敌人听到"
    /// 本身就是一件独立的事——它同时涉及玩家的移动状态与武器数据，却不属于其中任何一方。</para>
    ///
    /// <para>两类噪音的共同点是**都只在这里产生**：AI 侧只会收到一个带半径的
    /// <see cref="NoiseEvent"/>，既不需要知道那是脚步还是枪声，也不需要认识武器或背包。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>脚步噪音的广播间隔（秒）。</summary>
        private const float NoiseBroadcastInterval = 0.25f;

        private float m_NoiseTimer;

        /// <summary>枪声噪音的事件订阅。退出时需要释放。</summary>
        /// <remarks>
        /// 用完全限定名而不是 <c>using System;</c>：本类里已有 `Object.FindAnyObjectByType`，
        /// 引入 System 会让 Object 变成二义（UnityEngine.Object 与 object），
        /// 为一个字段而改动六处调用不值得。
        /// </remarks>
        private System.IDisposable m_WeaponNoiseSubscription;

        /// <summary>玩家当前的噪音档位。每帧计算，供噪音广播与开发者模式共用。</summary>
        private NoiseTier m_CurrentNoiseTier = NoiseTier.Silent;

        /// <summary>
        /// 订阅开火事件，把每一发子弹变成一次枪声噪音。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么订阅事件而不是写进武器控制器：</b>"开火会产生声音"是**世界规则**，
        /// 不是武器自身的职责。放在启动层之后，玩家与 AI 的开火自动都算数，
        /// 将来加消音器或加爆炸物也只需要改这一处。</para>
        /// <para><b>不区分是否命中：</b>打空的枪声一样会招人，这正是"什么时候该开枪"的代价所在。
        /// 如果只有命中的枪才出声，玩家就会用"先试射一发"来免费试探有没有人。</para>
        /// </remarks>
        private void SubscribeWeaponNoise()
        {
            m_WeaponNoiseSubscription = m_EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        /// <summary>一发子弹 = 一次枪声噪音。</summary>
        /// <remarks>
        /// 半径直接取事件里的值（由开火方的武器射程提供），因此**手枪比步枪安静**：
        /// 8 米的手枪只在近处暴露自己，12 米的步枪会招来更远的敌人。
        /// </remarks>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (m_AiDirector == null)
            {
                return;
            }

            var noise = new NoiseEvent(
                evt.ShooterId,
                new Vector2F(evt.Origin.x, evt.Origin.z),
                NoiseTier.Gunshot,
                evt.NoiseRadiusMeters);

            m_EventBus.Publish(noise);
            m_AiDirector.ReportNoise(noise);
        }

        /// <summary>
        /// 按固定间隔广播玩家的脚步噪音。
        /// </summary>
        /// <remarks>
        /// <para>噪音是持续状态而不是瞬时事件，因此这里按固定间隔采样广播，而不是每帧发布。
        /// 每帧广播会让事件总线被噪音刷满，排查其它事件时完全看不到有用信息。</para>
        /// <para>噪音档位由速度与负重状态共同决定（见 <see cref="MovementNoiseRules"/>）：
        /// 负重超载时即使走得慢，声音也比正常步行大——这是贪婪循环的反馈机制。
        /// 半径则由档位表给出，与枪声（按武器射程）是两条独立的来源。</para>
        /// </remarks>
        private void UpdatePlayerNoise(float deltaTime, bool inputBlocked)
        {
            // 档位每帧都算，但只在采样间隔到时才广播：
            // 开发者模式要显示"此刻有多吵"，而广播频率必须远低于帧率（理由见下）。
            var speed = m_MoveHandler != null ? m_MoveHandler.Simulator.State.CurrentSpeed : 0f;
            var overloaded = m_LastEncumbranceState == EncumbranceState.Overloaded;

            // 打开背包时不广播噪音，面板也应当显示静止——**显示必须与实际广播一致**，
            // 否则调试时会反复怀疑"明明在跑，为什么旁边的敌人没反应"。
            m_CurrentNoiseTier = inputBlocked
                ? NoiseTier.Silent
                : MovementNoiseRules.Classify(speed, m_MovementProfile.SprintSpeedThreshold, overloaded);

            m_NoiseTimer -= deltaTime;
            if (m_NoiseTimer > 0f)
            {
                return;
            }

            m_NoiseTimer = NoiseBroadcastInterval;

            if (m_CurrentNoiseTier == NoiseTier.Silent)
            {
                return;
            }

            var noise = new NoiseEvent(
                m_InputCollector != null ? m_InputCollector.PlayerId : 0,
                m_MoveHandler.Simulator.State.Position,
                m_CurrentNoiseTier,
                m_AiDirector.Profile.HearingRadiusFor(m_CurrentNoiseTier));

            m_EventBus.Publish(noise);
            m_AiDirector.ReportNoise(noise);
        }
    }
}
