using RaidDemo.Combat;
using RaidDemo.Kernel;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的战斗部分：上报意图、接收结算结果。
    /// </summary>
    /// <remarks>
    /// <para><b>客户端在这条链路上的全部职责：</b>把"我扣着扳机""我按了换弹""我在朝哪看"上报给服务器；
    /// 把服务器回来的开火 / 伤害 / 摧毁 / 换弹事件塞进本地事件总线。</para>
    ///
    /// <para><b>为什么这样最省事：</b>本地事件总线一响，弹道、枪口火焰、命中反馈、伤害数字、
    /// 弹药 HUD 全部照旧工作——那些表现层代码本来就是订阅事件的，它们不关心事件来自本机还是网络。</para>
    ///
    /// <para><b>本批不做本地开火预测：</b>按下到出火光之间会多一个往返延迟。
    /// 按设计文档第 11 节的降级策略，先保证"结算正确"，预测等出现明确手感问题再加。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>本帧是否扣着扳机（待上报）。</summary>
        private bool m_NetworkTriggerHeld;

        /// <summary>是否有待上报的换弹请求（边沿触发）。</summary>
        private bool m_NetworkReloadRequested;

        private bool m_CombatChannelRegistered;
        private bool m_LastReportedTrigger;
        private bool m_TriggerPressLogged;

        /// <summary>
        /// 采集本帧的战斗意图，供下一步上行。
        /// </summary>
        /// <remarks>
        /// 换弹用**锁存**而不是当帧直传：输入采集发生在每帧，而输入上行发生在固定步里，
        /// 若只记当帧状态，恰好落在两个固定步之间的那次按键就会被丢掉。
        /// </remarks>
        private void CollectMultiplayerCombatIntent()
        {
            if (m_InputCollector == null)
            {
                return;
            }

            m_InputCollector.ReadCombatIntent(out var wantsToFire, out var wantsToReload);

            // 验收模式下一律扣着扳机：无头进程没有键盘，但要让"开火 → 命中 → 掉血"
            // 这条链路可以被自动验证。它走的是与真实输入完全相同的上报路径。
            m_NetworkTriggerHeld = wantsToFire || m_AutoWalk;

            if (m_NetworkTriggerHeld != m_LastReportedTrigger)
            {
                m_LastReportedTrigger = m_NetworkTriggerHeld;

                if (m_NetworkTriggerHeld && !m_TriggerPressLogged)
                {
                    // 第一次扣扳机记在 Info：联调时"我到底有没有把开火意图发出去"
                    // 是两个必须分开确认的环节之一，值得留一条总是可见的痕迹。
                    m_TriggerPressLogged = true;
                    m_Session?.Log.Info("[联机] 已扣下扳机，开始向服务器上报开火意图。");
                }

                if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
                {
                    m_Session.Log.Verbose($"[联机] 扳机状态：{(m_NetworkTriggerHeld ? "按下" : "松开")}");
                }
            }

            if (wantsToReload)
            {
                m_NetworkReloadRequested = true;
            }
        }

        /// <summary>订阅战斗事件通道；连接成功后调用一次。</summary>
        private void RegisterCombatChannel()
        {
            if (m_CombatChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.RegisterNamedMessageHandler(
                CombatNetworkChannel.EventMessageName,
                OnCombatEventReceived);
            m_CombatChannelRegistered = true;
        }

        /// <summary>处理服务器下发的战斗事件：还原成本地事件发布出去。</summary>
        private void OnCombatEventReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(CombatEventMessage);
            reader.ReadValueSafe(out message);

            switch (message.Kind)
            {
                case CombatEventMessage.KindFired:
                    m_EventBus.Publish(new WeaponFiredEvent(
                        message.SourceId,
                        message.Origin,
                        message.EndPoint,
                        message.DidHit,
                        message.TargetId,
                        message.NoiseRadius,
                        timestamp: 0d,
                        sequence: message.Sequence,
                        pelletIndex: message.PelletIndex));
                    break;

                case CombatEventMessage.KindDamaged:
                    m_EventBus.Publish(new DamageAppliedEvent(
                        message.SourceId,
                        message.TargetId,
                        message.Damage,
                        armorDamage: 0f,
                        isCritical: false,
                        penetrationFactor: 1f,
                        message.RemainingHealth,
                        message.WasKilled,
                        timestamp: 0d,
                        sequence: message.Sequence));
                    break;

                case CombatEventMessage.KindReload:
                    m_EventBus.Publish(new ReloadStateChangedEvent(
                        message.SourceId,
                        message.IsReloading,
                        message.MagazineAmmo));
                    break;

                case CombatEventMessage.KindDestroyed:
                    m_EventBus.Publish(new TargetDestroyedEvent(
                        message.TargetId,
                        message.SourceId));
                    break;
            }

            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose($"[联机] 收到战斗事件：种类 {message.Kind}（来源 {message.SourceId}）");
            }
        }
    }
}
