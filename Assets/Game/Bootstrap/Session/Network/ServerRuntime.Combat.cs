using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的战斗部分：持有战斗权威、消费上行输入、广播结算结果。
    /// </summary>
    /// <remarks>
    /// 它只做搬运与生命周期：规则在 <see cref="ServerCombatCoordinator"/> 里，
    /// 网络编解码在消息结构里。这样单机与联机用的是同一套战斗规则，
    /// 差别只在"输入从哪来、结果到哪去"。
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>枪口离地高度（米）。与客户端 <c>SceneBootstrap</c> 的取值保持一致。</summary>
        private const float MuzzleHeight = 1.05f;

        private ServerCombatCoordinator m_Combat;
        private readonly List<IDisposable> m_CombatSubscriptions = new List<IDisposable>();
        private readonly HashSet<int> m_TriggerReported = new HashSet<int>();

        /// <summary>服务器侧战斗权威；未就绪时为 null。</summary>
        public ServerCombatCoordinator Combat => m_Combat;

        /// <summary>
        /// 按需建立战斗权威。
        /// </summary>
        /// <remarks>
        /// 物品目录来自地图场景（客户端装配根在服务器模式下交接给 <see cref="ServerMode"/>），
        /// 而地图是异步加载的，因此这里做成惰性初始化：第一次有玩家接入时才建。
        /// </remarks>
        private bool EnsureCombat()
        {
            if (m_Combat != null)
            {
                return true;
            }

            var catalog = ServerMode.SceneItemCatalog;
            if (catalog == null)
            {
                m_Session?.Log.Warning("[服务器] 尚未拿到物品目录，战斗权威未启用（玩家将无法开火）。");
                return false;
            }

            m_Combat = new ServerCombatCoordinator(
                catalog,
                m_Session.Events,
                originProvider: ResolvePlayerOrigin,
                probe: new PhysicsHitProbe());

            var bus = m_Session.Events;
            m_CombatSubscriptions.Add(bus.Subscribe<WeaponFiredEvent>(OnCombatFired));
            m_CombatSubscriptions.Add(bus.Subscribe<DamageAppliedEvent>(OnCombatDamaged));
            m_CombatSubscriptions.Add(bus.Subscribe<TargetDestroyedEvent>(OnCombatDestroyed));
            m_CombatSubscriptions.Add(bus.Subscribe<ReloadStateChangedEvent>(OnCombatReload));

            m_Session.Log.Info("[服务器] 战斗权威已就绪（武器 / 弹药 / 命中 / 伤害）。");
            return true;
        }

        /// <summary>推进战斗：武器冷却、连发节奏、换弹计时。</summary>
        private void TickCombat(float deltaTime)
        {
            m_Combat?.Tick(deltaTime);
        }

        /// <summary>释放战斗订阅。</summary>
        private void ShutdownCombat()
        {
            foreach (var subscription in m_CombatSubscriptions)
            {
                subscription?.Dispose();
            }

            m_CombatSubscriptions.Clear();
            m_Combat = null;
        }

        /// <summary>子弹从玩家的权威位置发出。</summary>
        private Vector3 ResolvePlayerOrigin(int playerId)
        {
            if (!m_PlayerBodies.TryGetValue(playerId, out var body) || body == null)
            {
                return Vector3.zero;
            }

            // 枪口高度与客户端一致（SceneBootstrap 的 MuzzleHeight）：
            // 从脚底平射会让子弹贴着地面走，先打中地形、永远打不到站立的角色。
            return body.position + (Vector3.up * MuzzleHeight);
        }

        /// <summary>把一条上行输入同时交给移动与战斗。</summary>
        private void ApplyCombatInput(int playerId, in PlayerInputMessage message, Vector2F aimDirection)
        {
            if (m_Combat == null)
            {
                return;
            }

            m_Combat.SubmitInput(playerId, message.TriggerHeld, aimDirection);

            // 诊断留痕：第一次收到某名玩家的"开火"意图时记一笔。
            // 联机战斗出问题时，"输入有没有上来"与"上来之后有没有打出去"是两个必须分开确认的环节。
            if (message.TriggerHeld && m_TriggerReported.Add(playerId))
            {
                m_Session?.Log.Verbose($"[服务器] 收到玩家 {playerId} 的开火意图（扳机置位）。");
            }

            if (message.ReloadRequested
                && !m_Combat.RequestReload(playerId, message.Sequence, out var failure)
                && m_Session != null
                && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                m_Session.Log.Verbose($"[服务器] 玩家 {playerId} 换弹被拒绝：{failure}");
            }
        }

        /// <summary>玩家接入时让他参战。</summary>
        /// <summary>
        /// 给玩家的碰撞载体挂上"可被命中"的标识。
        /// </summary>
        /// <remarks>
        /// 命中射线只认 <see cref="CombatTargetView"/> 上的目标编号；载体没有它，
        /// 子弹打上去只会被当成环境命中。这一步必须在参战之后做——
        /// 目标编号是参战时由战斗世界分配的。
        /// </remarks>
        private void BindPlayerHitTarget(int playerId)
        {
            if (m_Combat == null
                || !m_PlayerColliders.TryGetValue(playerId, out var body)
                || body == null)
            {
                return;
            }

            var combatantId = m_Combat.GetCombatantId(playerId);
            if (combatantId == 0)
            {
                return;
            }

            var targetView = body.GetComponent<CombatTargetView>();
            if (targetView == null)
            {
                targetView = body.AddComponent<CombatTargetView>();
            }

            // 服务器不需要命中染色：那是表现层的事，由客户端各自播放。
            targetView.Initialize(combatantId, colorFeedback: false);
        }

        private void AddPlayerToCombat(int playerId)
        {
            if (!EnsureCombat())
            {
                return;
            }

            if (m_Combat.TryAddPlayer(playerId, out var error))
            {
                m_Session?.Log.Info($"[服务器] 玩家 {playerId} 已进入战斗（配发武器与备弹）。");
            }
            else
            {
                m_Session?.Log.Warning($"[服务器] 玩家 {playerId} 参战失败：{error}");
            }
        }

        /// <summary>玩家断开时让他退出战斗。</summary>
        private void RemovePlayerFromCombat(int playerId)
        {
            m_Combat?.RemovePlayer(playerId);
        }

        private void OnCombatFired(WeaponFiredEvent evt)
        {
            if (m_Session != null && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                // 开火每秒钟可能十几次，只在详细日志下记录；伤害与击杀才是需要留痕的事件。
                m_Session.Log.Verbose(
                    $"[服务器] {DescribeEntity(ResolveEntityId(evt.ShooterId))}" +
                    $"(战斗单位#{evt.ShooterId}) 开火" +
                    $"（命中={evt.DidHit}，目标={ResolveEntityId(evt.HitTargetId)}）" +
                    $" 起点=({evt.Origin.x:F1},{evt.Origin.y:F2},{evt.Origin.z:F1})" +
                    $" 终点=({evt.EndPoint.x:F1},{evt.EndPoint.y:F2},{evt.EndPoint.z:F1})" +
                    DescribeAiAim(evt.ShooterId));
            }

            BroadcastCombatEvent(new CombatEventMessage
            {
                Kind = CombatEventMessage.KindFired,
                SourceId = ResolveEntityId(evt.ShooterId),
                TargetId = ResolveEntityId(evt.HitTargetId),
                Origin = evt.Origin,
                EndPoint = evt.EndPoint,
                DidHit = evt.DidHit,
                PelletIndex = evt.PelletIndex,
                NoiseRadius = evt.NoiseRadiusMeters,
                Sequence = evt.Sequence,
            });
        }

        private void OnCombatDamaged(DamageAppliedEvent evt)
        {
            m_Combat?.OnDamageApplied(evt);

            var attacker = ResolveEntityId(evt.AttackerId);
            var target = ResolveEntityId(evt.TargetId);
            m_Session?.Log.Info(
                $"[服务器] 命中：{DescribeEntity(attacker)} → {DescribeEntity(target)}，" +
                $"伤害 {evt.Damage:F1}，剩余 {evt.RemainingHealth:F1}" +
                (evt.WasKilled ? "（已阵亡）" : string.Empty));

            BroadcastCombatEvent(new CombatEventMessage
            {
                Kind = CombatEventMessage.KindDamaged,
                SourceId = attacker,
                TargetId = target,
                Damage = evt.Damage,
                RemainingHealth = evt.RemainingHealth,
                WasKilled = evt.WasKilled,
                Sequence = evt.Sequence,
            });
        }

        private void OnCombatDestroyed(TargetDestroyedEvent evt)
        {
            // 敌人阵亡后不再可被命中：留着碰撞体的话，子弹会打在"看不见的尸体"上，
            // 表现为"明明没人了，开枪还是有命中反馈"。
            DisableEnemyCollider(evt.TargetId);

            BroadcastCombatEvent(new CombatEventMessage
            {
                Kind = CombatEventMessage.KindDestroyed,
                SourceId = ResolveEntityId(evt.KillerId),
                TargetId = ResolveEntityId(evt.TargetId),
            });
        }

        private void OnCombatReload(ReloadStateChangedEvent evt)
        {
            BroadcastCombatEvent(new CombatEventMessage
            {
                Kind = CombatEventMessage.KindReload,
                SourceId = ResolveEntityId(evt.OwnerId),
                IsReloading = evt.IsReloading,
                MagazineAmmo = evt.MagazineAmmo,
            });
        }

        /// <summary>
        /// 把战斗单位标识翻译回玩家标识。
        /// </summary>
        /// <remarks>
        /// 客户端不认识服务器的战斗单位编号，只认识玩家标识；
        /// 翻译发生在服务器侧，客户端就不必知道战斗世界的内部编号规则。
        /// </remarks>
        private int ResolvePlayerId(int combatantId)
        {
            return m_Combat != null ? m_Combat.GetPlayerId(combatantId) : 0;
        }

        private void BroadcastCombatEvent(in CombatEventMessage message)
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null
                || manager.ConnectedClientsIds.Count == 0)
            {
                return;
            }

            using (var writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessageToAll(
                    CombatNetworkChannel.EventMessageName,
                    writer);
            }
        }
    }
}
