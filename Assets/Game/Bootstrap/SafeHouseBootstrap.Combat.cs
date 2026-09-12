using RaidDemo.Combat;
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
        private int m_PlayerCombatantId;

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

            var viewHost = new GameObject("PlayerWeaponView");
            viewHost.transform.SetParent(transform, worldPositionStays: false);
            m_WeaponView = viewHost.AddComponent<PlayerWeaponView>();
            m_WeaponView.Build(m_PlayerMotor != null ? m_PlayerMotor.transform : transform);

            var hudHost = new GameObject("CombatHud");
            hudHost.transform.SetParent(transform, worldPositionStays: false);
            m_CombatHud = hudHost.AddComponent<CombatHud>();
            m_CombatHud.Initialize(m_WeaponController, m_Loadout);

            RegisterTargets();
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
