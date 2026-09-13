using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的战斗装配：让玩家能试枪，并让靶子真的挨打。
    /// </summary>
    /// <remarks>
    /// <para>与战局用的是同一套武器链路（<see cref="PlayerWeaponController"/> + 同一套命令），
    /// 差别只有两条：没有 AI，也没有战局会话。因此这里的「战斗」是纯粹的靶场。</para>
    ///
    /// <para><b>靶子会登记成战斗单位</b>：这样命中判定、伤害计算与伤害事件全部走现成链路，
    /// 不必为靶子另写一套弹道。它们不会再被 AI 盯上——安全屋里根本没有 AI。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        /// <summary>表现层资产目录：音效、武器模型与战斗特效。</summary>
        /// <remarks>字段定义放在战斗装配这一部分，与它的唯一使用点相邻；
        /// 主文件已接近工程规定的 400 行上限。</remarks>
        [SerializeField] private PresentationCatalog m_PresentationCatalog;

        /// <summary>玩家生命上限。靶场里不会掉血，但 HUD 需要一个数值。</summary>
        private const int PlayerMaxHealth = 100;

        /// <summary>靶子的「生命」：只用于让伤害事件成立，打到 0 也不会消失。</summary>
        private const float TargetMaxHealth = 100000f;

        private CombatTuning m_CombatTuning;
        private CombatWorld m_CombatWorld;
        private PlayerWeapon m_PlayerWeapon;
        private PlayerWeaponController m_WeaponController;
        private PlayerWeaponView m_WeaponView;
        private CombatHud m_CombatHud;
        private AudioService m_AudioService;
        private GameAudioDirector m_GameAudio;
        private int m_PlayerCombatantId;

        /// <summary>上一帧显示在手上的武器，用于判断「换枪了没有」。</summary>
        private ItemInstance m_LastShownWeapon;

        /// <summary>灰盒枪身长度（格）。步枪更长，手枪更短。</summary>
        private int m_WeaponLengthCells = 2;

        /// <summary>准星（战局里由场景启动层负责，安全屋同样需要）。</summary>
        private AimCrosshair m_Crosshair;

        /// <summary>装配武器与靶场。</summary>
        private void InitializeCombat()
        {
            m_CombatTuning = new CombatTuning();
            m_CombatWorld = new CombatWorld();
            m_PlayerWeapon = new PlayerWeapon(new DeterministicRandom(20260912u));

            m_WeaponController = new PlayerWeaponController(
                m_PlayerWeapon,
                m_Loadout,
                new PhysicsHitProbe(),
                m_CombatWorld,
                m_CombatTuning,
                m_EventBus,
                m_AmmoPouchContainerId);

            m_CommandRouter.Register<PlayerFireIntent>(new FireCommandHandler(m_WeaponController));
            m_CommandRouter.Register<PlayerReloadIntent>(new ReloadCommandHandler(m_WeaponController));

            var armor = m_Loadout.Equipment?.Get(EquipmentSlot.Body)?.Definition?.ArmorStats;
            m_PlayerCombatantId = m_CombatWorld.Create(PlayerMaxHealth, armor);
            if (m_PlayerMotor != null)
            {
                var targetView = m_PlayerMotor.gameObject.AddComponent<CombatTargetView>();
                targetView.Initialize(m_PlayerCombatantId, colorFeedback: false);
            }

            // 弹道、音效与特效：与战局一样挂在同一个「战斗效果」节点上。
            // 它们都只订阅开火事件，因此拿到事件总线就能工作。
            var effectsHost = new GameObject("CombatEffects");
            effectsHost.transform.SetParent(transform, worldPositionStays: false);
            effectsHost.AddComponent<TracerRenderer>().Bind(m_EventBus);

            var catalog = m_PresentationCatalog;
            var playerId = m_InputCollector != null ? m_InputCollector.PlayerId : 0;
            var playerTransform = m_PlayerMotor != null ? m_PlayerMotor.transform : null;

            m_AudioService = effectsHost.AddComponent<AudioService>();
            m_AudioService.Initialize(catalog != null ? catalog.Audio : null);

            var vfx = effectsHost.AddComponent<CombatVfxDirector>();
            vfx.Initialize(
                catalog != null ? catalog.MuzzleFlashPrefab : null,
                catalog != null ? catalog.ImpactSparkPrefab : null,
                catalog != null ? catalog.ImpactDustPrefab : null,
                catalog != null ? catalog.ImpactFleshPrefab : null);
            vfx.Bind(m_EventBus);

            var audioDirectorHost = new GameObject("GameAudioDirector");
            audioDirectorHost.transform.SetParent(transform, worldPositionStays: false);
            m_GameAudio = audioDirectorHost.AddComponent<GameAudioDirector>();
            m_GameAudio.Bind(m_EventBus, m_AudioService, playerTransform, playerId);

            if (playerTransform != null)
            {
                var footsteps = effectsHost.AddComponent<FootstepAudioDirector>();
                footsteps.Bind(m_EventBus, m_AudioService, playerTransform, playerId);
            }

            var viewHost = new GameObject("PlayerWeaponView");
            viewHost.transform.SetParent(transform, worldPositionStays: false);
            m_WeaponView = viewHost.AddComponent<PlayerWeaponView>();
            m_WeaponView.Build(
                playerTransform != null ? playerTransform : transform,
                catalog != null ? catalog.RifleWeaponPrefab : null,
                catalog != null ? catalog.PistolWeaponPrefab : null);

            var hudHost = new GameObject("CombatHud");
            hudHost.transform.SetParent(transform, worldPositionStays: false);
            m_CombatHud = hudHost.AddComponent<CombatHud>();
            m_CombatHud.Initialize(m_WeaponController, m_Loadout);

            var crosshairHost = new GameObject("AimCrosshair");
            crosshairHost.transform.SetParent(transform, worldPositionStays: false);
            m_Crosshair = crosshairHost.AddComponent<AimCrosshair>();

            // 准星贴图来自表现层资产目录；目录为空时组件自动退回程序化十字。
            m_Crosshair.Initialize(
                catalog != null ? catalog.CrosshairSprite : null,
                catalog != null ? catalog.CrosshairReloadSprite : null);

            RegisterTargets();
        }

        /// <summary>
        /// 把当前武器同步给表现层，并让枪模型与准星跟随。
        /// </summary>
        /// <remarks>
        /// 这里用「每帧比对上一帧的武器」而不是订阅换枪事件：安全屋只关心
        /// 「手上是什么」，比对一次引用比接一条事件更简单，也不会因为事件名变化而失效。
        /// </remarks>
        private void UpdateWeaponPresentation()
        {
            var weapon = m_Loadout?.Equipment?.Get(EquipmentSlot.PrimaryWeapon);

            // 枪口位置必须每帧写入：命中射线从枪口出发，不设置的话它停在世界原点，
            // 于是「枪响了、子弹也飞了」，但永远打不到眼前的靶子。
            if (m_PlayerMotor != null && m_WeaponController != null)
            {
                // 有真实武器模型时枪口取枪管末端，模型缺失时退回"角色位置抬高"。
                m_WeaponController.SetMuzzlePosition(
                    m_WeaponView != null && m_WeaponView.IsEquipped
                        ? m_WeaponView.MuzzleWorldPosition
                        : m_PlayerMotor.transform.position + (Vector3.up * 1.2f));
            }

            // 必须把「手上是什么武器」同步给控制器：弹匣容量、装弹、射速全部由它管理。
            // 漏掉这一步的症状很有迷惑性——界面上武器名是有的（那是这里直接读装备槽显示的），
            // 但弹药数是「-- / --」、按 R 没反应、开枪也打不响。
            var stats = weapon?.Definition?.WeaponStats;
            if (m_WeaponController.SyncEquippedWeapon(stats) && stats != null)
            {
                // 准星最大距离由当前武器射程决定，与战局同一条规则。
                m_InputCollector?.SetMaxAimDistance(stats.RangeMeters);
            }

            if (!ReferenceEquals(weapon, m_LastShownWeapon))
            {
                m_LastShownWeapon = weapon;
                m_WeaponLengthCells = weapon != null ? weapon.Definition.GridSize.Width : 2;
                m_CombatHud?.SetWeapon(
                    weapon != null ? weapon.Definition.DisplayName : null,
                    weapon?.Definition?.WeaponStats?.CaliberId);
                m_GameAudio?.SetLocalWeaponGridWidth(m_WeaponLengthCells);
            }

            if (m_WeaponView != null && m_PlayerMotor != null && m_WeaponController != null)
            {
                m_WeaponView.UpdateView(
                    m_PlayerMotor.transform.position,
                    m_WeaponController.AimDegrees,
                    m_WeaponController.Weapon.IsEquipped,
                    m_WeaponLengthCells);
            }

            UpdateCrosshair();
        }

        /// <summary>把世界瞄准点反投影回屏幕，与战局里同一套算法。</summary>
        private void UpdateCrosshair()
        {
            if (m_Crosshair == null || m_InputCollector == null || m_CameraController == null || m_PlayerMotor == null)
            {
                return;
            }

            var worldAim = m_InputCollector.AimWorldPosition;

            // 换弹时换一张准星造型：这是"现在打不出去"最直接的提示。
            m_Crosshair.SetReloading(
                m_WeaponController != null
                && m_WeaponController.Runtime != null
                && m_WeaponController.Runtime.IsReloading);

            if (worldAim.IsNearlyZero)
            {
                m_Crosshair.Hide();
                return;
            }

            var camera = m_CameraController.GetComponent<Camera>();
            if (camera == null)
            {
                m_Crosshair.Hide();
                return;
            }

            var screen = camera.WorldToScreenPoint(new Vector3(
                worldAim.X,
                m_PlayerMotor.transform.position.y,
                worldAim.Y));

            if (screen.z < 0f)
            {
                m_Crosshair.Hide();
                return;
            }

            m_Crosshair.SetScreenPosition(new Vector2(screen.x, screen.y));
        }

        /// <summary>把场景里的靶子登记成可受击单位，并挂上伤害数字。</summary>
        private void RegisterTargets()
        {
            var targets = Object.FindObjectsByType<ShootingTarget>(FindObjectsSortMode.None);
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                var combatantId = m_CombatWorld.Create(TargetMaxHealth);

                var view = target.gameObject.AddComponent<CombatTargetView>();
                view.Initialize(combatantId, colorFeedback: false);

                var numbers = target.gameObject.AddComponent<DamageNumberView>();
                numbers.Initialize(target, combatantId, m_EventBus);
            }
        }
    }
}
