using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Raid;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 玩法音效导演：把战斗、搜刮、撤离、结算的事件翻译成具体的声音。
    /// </summary>
    /// <remarks>
    /// <para>放在启动层而不是表现层，是因为它要同时听懂战斗、背包、战局三个模块的事件，
    /// 而表现层按规定不允许引用战局与背包模块——那会把"表现"变成"什么都依赖"。</para>
    /// <para><b>只有本地玩家才有"自己"的声音：</b>换弹、脚步、拾取这类动作音只对本地玩家播放。
    /// 联机时若把队友的换弹也播出来，四个人的换弹声会盖住真正重要的枪声；
    /// 而枪声不同——枪声是战斗信息，敌人与队友的枪声都必须能听见方位，
    /// 因此它走空间音效、按距离衰减，并且所有射手共用一条链路。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameAudioDirector : MonoBehaviour
    {
        /// <summary>本地玩家开枪的音量。</summary>
        private const float LocalShotVolume = 0.85f;

        /// <summary>他人（AI / 队友）开枪的音量：略低，但必须有方位感。</summary>
        private const float RemoteShotVolume = 0.7f;

        /// <summary>命中活体的音量。</summary>
        private const float FleshImpactVolume = 0.6f;

        /// <summary>命中环境的音量。</summary>
        private const float HardImpactVolume = 0.45f;

        /// <summary>活体命中的最远可听距离（米）。</summary>
        private const float FleshImpactDistance = 24f;

        /// <summary>环境命中的最远可听距离（米）。</summary>
        private const float HardImpactDistance = 20f;

        /// <summary>枪声的最远可听距离（米）：步枪与射程同量级（12 米）的 3 倍余量，覆盖整张盆地。</summary>
        private const float RifleShotDistance = 45f;

        /// <summary>手枪枪声的最远可听距离（米）。</summary>
        private const float PistolShotDistance = 32f;

        /// <summary>换弹动作音的音量。</summary>
        private const float ReloadVolume = 0.6f;

        /// <summary>翻找声的间隔（秒）。</summary>
        private const float RummageInterval = 0.45f;

        /// <summary>撤离读秒的提示分档：进度跨过 25% / 50% / 75% 各响一声。</summary>
        private const int ExtractionTicks = 3;

        /// <summary>枪声的音高抖动幅度：比默认略大，让连射听起来不是同一发在重复。</summary>
        private const float ShotPitchJitter = 0.07f;

        private readonly List<IDisposable> m_Subscriptions = new List<IDisposable>();

        private AudioService m_Audio;
        private Transform m_Player;
        private int m_LocalPlayerId;
        private int m_ShotVariant;
        private int m_ImpactVariant;
        private int m_RummageVariant;
        private float m_NextRummageTime;
        private int m_ExtractionStep = -1;

        /// <summary>本地玩家当前手持武器的表现类别。</summary>
        public WeaponPresentationKind LocalWeaponKind { get; private set; } = WeaponPresentationKind.Rifle;

        /// <summary>
        /// 绑定事件总线与本地玩家。
        /// </summary>
        /// <param name="eventBus">事件总线。</param>
        /// <param name="audio">音效服务。</param>
        /// <param name="player">本地玩家根节点，可为 null（安全屋尚未生成玩家时）。</param>
        /// <param name="localPlayerId">本地玩家编号。</param>
        public void Bind(EventBus eventBus, AudioService audio, Transform player, int localPlayerId)
        {
            m_Audio = audio;
            m_Player = player;
            m_LocalPlayerId = localPlayerId;

            DisposeSubscriptions();
            if (eventBus == null)
            {
                return;
            }

            m_Subscriptions.Add(eventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired));
            m_Subscriptions.Add(eventBus.Subscribe<ReloadStateChangedEvent>(OnReloadStateChanged));
            m_Subscriptions.Add(eventBus.Subscribe<LootSearchProgressEvent>(OnLootSearchProgress));
            m_Subscriptions.Add(eventBus.Subscribe<LootSearchCompletedEvent>(OnLootSearchCompleted));
            m_Subscriptions.Add(eventBus.Subscribe<InventoryChangedEvent>(OnInventoryChanged));
            m_Subscriptions.Add(eventBus.Subscribe<ExtractionProgressChangedEvent>(OnExtractionProgress));
            m_Subscriptions.Add(eventBus.Subscribe<ExtractionCompletedEvent>(OnExtractionCompleted));
            m_Subscriptions.Add(eventBus.Subscribe<RaidEndedEvent>(OnRaidEnded));
        }

        /// <summary>
        /// 更新本地玩家的武器类别（换枪时由启动层调用）。
        /// </summary>
        /// <param name="gridWidth">主武器占几格宽；没有武器时传 0。</param>
        public void SetLocalWeaponGridWidth(int gridWidth)
        {
            LocalWeaponKind = AudioPlaybackRules.ResolveWeaponKind(gridWidth);
        }

        /// <summary>本地玩家位置（每帧由启动层刷新，玩家可能在场景中重生）。</summary>
        public void SetPlayer(Transform player)
        {
            m_Player = player;
        }

        private void OnDestroy()
        {
            DisposeSubscriptions();
        }

        private void DisposeSubscriptions()
        {
            foreach (var subscription in m_Subscriptions)
            {
                subscription?.Dispose();
            }

            m_Subscriptions.Clear();
        }

        /// <summary>开火：枪声 + （若命中）命中音。</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            var catalog = m_Audio != null ? m_Audio.Catalog : null;
            if (catalog == null)
            {
                return;
            }

            var isLocal = evt.ShooterId == m_LocalPlayerId;
            var kind = isLocal ? LocalWeaponKind : WeaponPresentationKind.Rifle;
            var clip = kind == WeaponPresentationKind.Rifle
                ? catalog.PickRifleShot(m_ShotVariant++)
                : catalog.PickPistolShot(m_ShotVariant++);

            m_Audio.PlayAt(
                clip,
                evt.Origin,
                isLocal ? LocalShotVolume : RemoteShotVolume,
                kind == WeaponPresentationKind.Rifle ? RifleShotDistance : PistolShotDistance,
                ShotPitchJitter,
                // 自己的枪声不参与空间化：它必须满音量、居中；别人的枪声走 3D 才有方位与距离感。
                flat: isLocal);

            if (!evt.DidHit)
            {
                return;
            }

            var isUnit = evt.HitTargetId != 0;
            var impact = isUnit
                ? catalog.PickImpactFlesh(m_ImpactVariant++)
                : catalog.PickImpactHard(m_ImpactVariant++);
            m_Audio.PlayAt(
                impact,
                evt.EndPoint,
                isUnit ? FleshImpactVolume : HardImpactVolume,
                isUnit ? FleshImpactDistance : HardImpactDistance);
        }

        /// <summary>换弹：开始是"卸弹匣"，完成是"推弹匣 + 拉栓"。</summary>
        private void OnReloadStateChanged(ReloadStateChangedEvent evt)
        {
            if (evt.OwnerId != m_LocalPlayerId || m_Audio == null || m_Audio.Catalog == null)
            {
                return;
            }

            var position = ResolvePlayerPosition();
            if (evt.IsReloading)
            {
                m_Audio.PlayAt(m_Audio.Catalog.MagazineOut, position, ReloadVolume, 8f, 0.04f, flat: true);
                return;
            }

            m_Audio.PlayAt(m_Audio.Catalog.MagazineIn, position, ReloadVolume, 8f, 0.04f, flat: true);
            m_Audio.PlayAt(m_Audio.Catalog.BoltClose, position, ReloadVolume * 0.9f, 8f, 0.04f, flat: true);
        }

        /// <summary>搜刮读条：按固定间隔播放翻找声，让"正在搜"这件事有听觉反馈。</summary>
        private void OnLootSearchProgress(LootSearchProgressEvent evt)
        {
            if (m_Audio == null || m_Audio.Catalog == null || Time.unscaledTime < m_NextRummageTime)
            {
                return;
            }

            m_NextRummageTime = Time.unscaledTime + RummageInterval;
            m_Audio.PlayAt(
                m_Audio.Catalog.PickLootRummage(m_RummageVariant++),
                ResolvePlayerPosition(),
                0.45f,
                14f,
                0.07f);
        }

        /// <summary>搜刮完成：容器开启。</summary>
        private void OnLootSearchCompleted(LootSearchCompletedEvent evt)
        {
            PlayFlatCue(m_Audio?.Catalog?.LootOpen, 0.6f);
        }

        /// <summary>物品入包：一声轻响。</summary>
        /// <remarks>只在"放入"类型上响：整理背包、拆分堆叠如果也响，翻箱子时会变成一串噪音。</remarks>
        private void OnInventoryChanged(InventoryChangedEvent evt)
        {
            if (evt.ChangeType != InventoryChangeTypes.Place &&
                evt.ChangeType != InventoryChangeTypes.Equip)
            {
                return;
            }

            PlayFlatCue(m_Audio?.Catalog?.LootPickup, 0.45f);
        }

        /// <summary>撤离读秒：按进度分档提示。</summary>
        private void OnExtractionProgress(ExtractionProgressChangedEvent evt)
        {
            if (!evt.IsActive)
            {
                m_ExtractionStep = -1;
                return;
            }

            var step = Mathf.FloorToInt(evt.Progress01 * (ExtractionTicks + 1));
            if (step <= m_ExtractionStep)
            {
                return;
            }

            m_ExtractionStep = step;
            PlayFlatCue(m_Audio?.Catalog?.ExtractionTick, 0.55f);
        }

        /// <summary>撤离成立：成功提示音。</summary>
        private void OnExtractionCompleted(ExtractionCompletedEvent evt)
        {
            PlayFlatCue(m_Audio?.Catalog?.RaidSuccess, 0.75f);
        }

        /// <summary>战局结束：按结局给成功或失败提示音。</summary>
        private void OnRaidEnded(RaidEndedEvent evt)
        {
            var clip = evt.Outcome == RaidOutcome.Extracted
                ? m_Audio?.Catalog?.RaidSuccess
                : m_Audio?.Catalog?.RaidFail;
            PlayFlatCue(clip, 0.7f);
        }

        /// <summary>播放一条不随距离衰减的提示音。</summary>
        private void PlayFlatCue(AudioClip clip, float volume)
        {
            if (m_Audio == null || clip == null)
            {
                return;
            }

            m_Audio.PlayFlat(clip, volume);
        }

        /// <summary>取玩家当前位置；玩家不存在时退回本组件位置。</summary>
        private Vector3 ResolvePlayerPosition()
        {
            return m_Player != null ? m_Player.position + (Vector3.up * 1.2f) : transform.position;
        }
    }
}
