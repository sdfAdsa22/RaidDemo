using RaidDemo.Combat;
using RaidDemo.Inventory;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的倒地与救援部分（M9 · P3.5）。
    /// </summary>
    /// <remarks>
    /// <para><b>客户端的职责只有三件：</b>把"我正在扶人"上报；把服务器给的倒地/被救起
    /// 画出来（血量、提示、能不能动）；在倒地期间**停住自己**——服务器已经不收他的输入了，
    /// 客户端若还继续预测移动，就会一直和快照对账、画面来回抽。</para>
    ///
    /// <para><b>扶起是"按住"而不是"按一下"：</b>被救者的倒计时不会因为有人按了一下就暂停，
    /// 施救者必须连续按住三秒且保持距离——这条规则在服务器的
    /// <see cref="PlayerLifeStateTracker"/> 里，客户端只负责如实上报按键状态。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        private bool m_LifeChannelRegistered;
        private bool m_NetworkReviveHeld;
        private bool m_LocalDowned;
        private float m_LocalBleedOutRemaining;

        /// <summary>本机是否处于倒地（失能）状态。</summary>
        public bool IsLocalPlayerDowned
        {
            get { return m_LocalDowned; }
        }

        /// <summary>订阅生命事件通道；连接成功后调用一次。</summary>
        private void RegisterLifeChannel()
        {
            if (m_LifeChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.RegisterNamedMessageHandler(
                ContainerNetworkChannel.LifeMessageName,
                OnLifeEventReceived);
            m_LifeChannelRegistered = true;
        }

        /// <summary>处理倒地 / 被救起 / 流血死亡。</summary>
        private void OnLifeEventReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(RaidLifeEventMessage);
            reader.ReadValueSafe(out message);

            var isLocal = message.PlayerId == m_LocalPlayerId;

            switch (message.Kind)
            {
                case RaidLifeEventMessage.KindDowned:
                    Debug.Log(
                        $"[联机] 玩家 {message.PlayerId} 倒地，剩余 {message.SecondsRemaining:F0} 秒。");

                    if (isLocal)
                    {
                        m_LocalDowned = true;
                        m_LocalBleedOutRemaining = message.SecondsRemaining;
                    }
                    else
                    {
                        NoteTeammateDowned(message.PlayerId, downed: true);
                        m_CombatHud?.ShowHint("队友倒地 · 靠近后按住 F 扶起", isWarning: true);
                    }
                    break;

                case RaidLifeEventMessage.KindRevived:
                    Debug.Log($"[联机] 玩家 {message.PlayerId} 已被救起。");

                    if (isLocal)
                    {
                        m_LocalDowned = false;
                        // 服务器把他恢复到固定生命值，界面跟着走。
                        ApplyLocalHealthFromServer(PlayerLifeStateTracker.RevivedHealth, true);
                    }
                    else
                    {
                        NoteTeammateDowned(message.PlayerId, downed: false);
                    }
                    break;

                case RaidLifeEventMessage.KindBledOut:
                    Debug.Log($"[联机] 玩家 {message.PlayerId} 倒地超时死亡。");

                    if (isLocal)
                    {
                        m_LocalDowned = false;
                    }
                    else
                    {
                        NoteTeammateDowned(message.PlayerId, downed: false);
                    }
                    break;
            }
        }

        /// <summary>
        /// 每帧采集救援按键，并把它与输入一起上行。
        /// </summary>
        /// <remarks>
        /// 倒地的人自己不能扶人（服务器的规则会拒绝），因此倒地期间直接上报 false。
        /// </remarks>
        private void CollectReviveIntent()
        {
            if (m_InputCollector == null || m_LocalDowned)
            {
                m_NetworkReviveHeld = false;
                return;
            }

            m_NetworkReviveHeld = m_InputCollector.ReadReviveHeld();
        }

        /// <summary>
        /// 倒地期间刷新 HUD 的倒计时（本机自己数秒，结束仍以服务器的生命事件为准）。
        /// </summary>
        /// <param name="deltaTime">帧间隔（秒）。</param>
        private void TickDownedHud(float deltaTime)
        {
            if (!m_LocalDowned)
            {
                return;
            }

            m_LocalBleedOutRemaining -= deltaTime;
            m_CombatHud?.SetDowned(m_LocalBleedOutRemaining);
        }

    }
}
