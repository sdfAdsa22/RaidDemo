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

            // 瞄准方向与瞄准点每帧同步（纯表现）：武器模型的朝向由 AimDegrees 驱动，
            // 不更新的话枪会一直指着出生方向——联机分支以前只在单机里做这件事。
            SyncAimToWeapon();

            m_InputCollector.ReadCombatIntent(out var wantsToFire, out var wantsToReload);

            // 验收模式下的自动开火：只有在"确实瞄着敌人"时才扣扳机
            //（m_AutoWalkHasEnemyTarget 由 UpdateAutoWalkInput 每帧写入）。
            // 无头进程没有键盘，但要让"开火 → 命中 → 掉血"这条链路可以被自动验证；
            // 它走的是与真实输入完全相同的上报路径。
            //
            // 为什么不再"一律扣着扳机"（U-85 的教训）：没有敌人时机器人的瞄准方向会落到
            // 最近的队友身上，于是它一路朝队友扫射——旧规则下真把房主打倒过。
            // 玩家之间已经免伤（CombatRules），但朝队友与空地扫射仍然是浪费，
            // 也会让真实队友以为被攻击。
            //
            // 另一个例外是 -rescueonly：那个模式的语义是"只救人、不主动交火"。
            // 第一版没有排除它，验收里的"乙"几秒就把"甲"打死，后续所有阶段全部失真
            // （2026-09-14 联机基础问题排查里踩过）。
            var rescueOnly = ClientMode.IsActive
                && ClientMode.Options != null
                && ClientMode.Options.RescueOnly;
            m_NetworkTriggerHeld = wantsToFire || (m_AutoWalk && !rescueOnly && m_AutoWalkHasEnemyTarget);

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
                // 换弹是边沿事件：交给移动链路锁存，由真正发出去的那个固定步清掉
                // （见 MultiplayerMovementLink.RequestReload 的说明）。
                m_MovementLink?.RequestReload();
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

        /// <summary>退订战斗事件通道（战局场景销毁时调用，见 <c>DetachFromServer</c>）。</summary>
        private void UnregisterCombatChannel()
        {
            if (!m_CombatChannelRegistered || m_NetworkClient == null
                || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.UnregisterNamedMessageHandler(
                CombatNetworkChannel.EventMessageName);
            m_CombatChannelRegistered = false;
        }

        /// <summary>处理服务器下发的战斗事件：还原成本地事件发布出去。</summary>
        private void OnCombatEventReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(CombatEventMessage);
            reader.ReadValueSafe(out message);

            switch (message.Kind)
            {
                case CombatEventMessage.KindFired:
                    // 自己那一发：起点改用**本地枪口**。
                    //
                    // 服务器的起点来自它的权威位置，而本机是本地预测 + 视觉跟随，
                    // 起步、变向时服务器位置能落后一米以上——用服务器起点画线，
                    // 玩家看到的就是"子弹从移动前的位置飞出来"（P-47 实机量到过 z=0.6
                    // 对客户端 z=2.16 的差距）。终点仍用服务器的命中点：那条才是权威结果。
                    //
                    // 别人的弹道不改：他们的位置本来就是服务器给的，起点与画面天然一致。
                    var origin = message.Origin;
                    if (message.SourceId == LocalNetworkPlayerId
                        && m_WeaponView != null
                        && m_WeaponView.IsEquipped)
                    {
                        origin = m_WeaponView.MuzzleWorldPosition;
                    }

                    m_EventBus.Publish(new WeaponFiredEvent(
                        message.SourceId,
                        origin,
                        message.EndPoint,
                        message.DidHit,
                        message.TargetId,
                        message.NoiseRadius,
                        timestamp: 0d,
                        sequence: message.Sequence,
                        pelletIndex: message.PelletIndex));

                    // 服务器在开火事件里带回了结算后的弹匣数：本地不推进武器，
                    // 不应用这个值，HUD 的弹药数会停在收到装备时的那一刻。
                    if (message.SourceId == LocalNetworkPlayerId)
                    {
                        ApplyLocalMagazineAmmo(message.MagazineAmmo);
                    }

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

                    // 挨打的是我：用服务器给的剩余生命刷新 HUD。
                    // 联机模式下本地没有生命模拟（那是服务器的权威），
                    // 少这一步玩家会发现自己掉血、界面却一直显示满血。
                    if (message.TargetId == LocalNetworkPlayerId)
                    {
                        ApplyLocalHealthFromServer(message.RemainingHealth, !message.WasKilled);
                    }

                    break;

                case CombatEventMessage.KindReload:
                    m_EventBus.Publish(new ReloadStateChangedEvent(
                        message.SourceId,
                        message.IsReloading,
                        message.MagazineAmmo));

                    if (message.SourceId == LocalNetworkPlayerId)
                    {
                        ApplyLocalMagazineAmmo(message.MagazineAmmo);
                    }

                    break;

                case CombatEventMessage.KindHealed:
                    // 治疗由服务器执行（RD-AUD-044），这里只刷新"我的"血条。
                    // 事件只发给本人，因此不需要再按 TargetId 过滤，但仍按同一口径判断，
                    // 免得将来把它改成广播时静默出错。
                    if (message.TargetId == LocalNetworkPlayerId)
                    {
                        ApplyLocalHealthFromServer(message.RemainingHealth, true);
                    }

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

        /// <summary>
        /// 把服务器给的弹匣数应用到本地武器运行时。
        /// </summary>
        /// <param name="magazineAmmo">服务器结算后的弹匣数；负数表示"本次事件没有带这个信息"。</param>
        /// <remarks>
        /// <para>联机客户端不推进武器（扣弹、换弹全在服务器算），因此每一次
        /// "我开的火"与"我的换弹状态变化"都是刷新 HUD 弹匣数的机会。</para>
        ///
        /// <para><b>为什么用负值做哨兵：</b>开火事件对 AI 射手不带弹药数（它们没有玩家弹匣），
        /// 把 0 当有效数据会把"没有信息"显示成"打空了"。</para>
        /// </remarks>
        private void ApplyLocalMagazineAmmo(int magazineAmmo)
        {
            if (magazineAmmo < 0)
            {
                return;
            }

            m_WeaponController?.Runtime?.SetMagazineAmmo(magazineAmmo);
        }

        /// <summary>
        /// 用服务器的权威结果刷新本机玩家的血量显示。
        /// </summary>
        /// <param name="remainingHealth">服务器结算后的剩余生命。</param>
        /// <param name="isAlive">是否仍存活。</param>
        /// <remarks>
        /// <para>联机模式下本地没有生命模拟：血量、护甲、死亡时刻全部由服务器说了算，
        /// 客户端只负责显示。单机那条路走的是 <c>UpdateAi</c> 里每帧从战斗世界拉取，两者不冲突
        /// （联机客户端不装配本地 AI）。</para>
        ///
        /// <para>护甲暂不同步：服务器还没有把护甲耐久下行（等 P3 的装备同步一起做），
        /// 因此联机里护甲条保持初始值。</para>
        /// </remarks>
        private void ApplyLocalHealthFromServer(float remainingHealth, bool isAlive)
        {
            var max = ServerCombatCoordinator.DefaultMaxHealth;
            m_CombatHud?.SetHealth(remainingHealth, max, isAlive);

            // 留一条可见痕迹：联机里"我的血条是不是服务器说了算"没法从画面上验证（无头验收没有画面），
            // 而这正是最容易悄悄退化成"本地自己算"的地方。
            m_Session?.Log.Verbose($"[联机] 本机生命（服务器权威）：{remainingHealth:F0}/{max:F0}，存活={isAlive}");
        }
    }
}
