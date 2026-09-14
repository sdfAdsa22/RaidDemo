using RaidDemo.Raid;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的战局结算部分：接收服务器裁定的结果并收尾本局。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么"什么时候结束"也由服务器说了算：</b>撤离读秒与阵亡判定依赖
    /// 权威位置与权威生命值。客户端自己在本地读秒，改一改就能"站着不动撤离成功"。
    /// 因此这里只做一件事：收到自己的结果就收尾。</para>
    ///
    /// <para><b>收尾走的是已有的本地路径：</b><see cref="RaidSession"/> 一结束就会广播
    /// <c>RaidEndedEvent</c>，结算界面、音效、流程状态全都照旧工作——
    /// 与战斗、背包一样，联机只换"事件从哪来"，不换"收到之后做什么"。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        private bool m_RaidOutcomeChannelRegistered;
        private bool m_LocalOutcomeApplied;

        /// <summary>服务器给我裁定的结果（未结算时为 null）。</summary>
        public RaidOutcomeMessage? LocalOutcome { get; private set; }

        /// <summary>订阅战局结果通道；连接成功后调用一次。</summary>
        private void RegisterRaidOutcomeChannel()
        {
            if (m_RaidOutcomeChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.RegisterNamedMessageHandler(
                ContainerNetworkChannel.OutcomeMessageName,
                OnRaidOutcomeReceived);
            m_RaidOutcomeChannelRegistered = true;
        }

        /// <summary>收到服务器裁定的战局结果。</summary>
        private void OnRaidOutcomeReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(RaidOutcomeMessage);
            reader.ReadValueSafe(out message);

            var outcome = (RaidOutcome)message.Outcome;
            Debug.Log(
                $"[联机] 战局结算：玩家 {message.PlayerId} {outcome}，"
                + $"带出价值 {message.CarriedValue}，击杀 {message.Kills}。");

            if (message.PlayerId != m_LocalPlayerId || m_LocalOutcomeApplied)
            {
                return;
            }

            m_LocalOutcomeApplied = true;
            LocalOutcome = message;

            // 走本地已有的收尾路径：会话结束 → 广播事件 → 结算界面。
            if (m_RaidSession == null || !m_RaidSession.IsActive)
            {
                return;
            }

            if (outcome == RaidOutcome.Extracted)
            {
                m_RaidSession.NotifyExtracted();
            }
            else
            {
                m_RaidSession.NotifyPlayerKilled();
            }
        }
    }
}
