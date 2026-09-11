using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的战斗装配部分。
    /// </summary>
    /// <remarks>
    /// <para>与背包装配一样拆成 partial 文件：主文件要保持在项目规定的 400 行以内，
    /// 而且"装配战斗"与"装配移动"本来就是两件独立的事。</para>
    /// <para>战斗系统需要的东西比前两个阶段都多：射线能力、单位注册表、调参、
    /// 手持武器状态、以及一屋子靶子。这些都只在这一处装配。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>枪口相对角色脚底的高度（米）。</summary>
        private const float MuzzleHeight = 1.05f;

        /// <summary>灰盒靶子的高度（米）。</summary>
        private const float TargetHeight = 1.8f;

        /// <summary>靶子之间的距离（米）。</summary>
        private const float TargetSpacing = 4f;

        /// <summary>被四个靶子包围的射击线：正前方、左右两侧、以及一个更远的。</summary>
        private static readonly float[] s_TargetLateralOffsets = { -4f, 0f, 4f };

        /// <summary>靶子阵列距离原点的距离（米）。</summary>
        private const float TargetRangeDistance = 9f;

        /// <summary>靶子的生命值。</summary>
        private const float TargetHealth = 100f;

        /// <summary>靶子的防护等级。0 表示无甲。</summary>
        private const int TargetArmorLevel = 2;

        /// <summary>靶子的护甲耐久。</summary>
        private const float TargetArmorDurability = 60f;

        private CombatWorld m_CombatWorld;
        private CombatTuning m_CombatTuning;
        private PlayerWeapon m_PlayerWeapon;
        private PlayerWeaponController m_WeaponController;
        private PlayerWeaponView m_WeaponView;
        private WeaponAudioPlayer m_WeaponAudio;
        private CombatHud m_CombatHud;

        /// <summary>当前武器在背包里占的格数。用于推算灰盒枪身长度与枪声变体。</summary>
        private int m_WeaponLengthCells = 2;

        private readonly Dictionary<int, CombatTargetView> m_TargetViews = new Dictionary<int, CombatTargetView>();

        /// <summary>战斗世界，供调试与测试读取。</summary>
        public CombatWorld Combatants
        {
            get { return m_CombatWorld; }
        }

        /// <summary>玩家手持武器状态，供调试与测试读取。</summary>
        public PlayerWeapon Weapon
        {
            get { return m_PlayerWeapon; }
        }

        /// <summary>武器控制器，供调试与测试读取。</summary>
        public PlayerWeaponController WeaponController
        {
            get { return m_WeaponController; }
        }

        /// <summary>装配战斗系统。</summary>
        private void InitializeCombat()
        {
            m_CombatTuning = new CombatTuning();
            var tuningProblem = m_CombatTuning.Validate();
            if (tuningProblem != null)
            {
                Debug.LogError($"[RaidDemo] 战斗调参不合法：{tuningProblem}", this);
                m_CombatTuning = CombatTuning.Default;
            }

            m_CombatWorld = new CombatWorld();
            m_PlayerWeapon = new PlayerWeapon(new DeterministicRandom(20260911u));

            var probe = new PhysicsHitProbe();
            m_WeaponController = new PlayerWeaponController(
                m_PlayerWeapon,
                m_Loadout,
                probe,
                m_CombatWorld,
                m_CombatTuning,
                m_EventBus,
                m_BackpackContainerId);

            m_CommandRouter.Register<PlayerFireIntent>(new FireCommandHandler(m_WeaponController));
            m_CommandRouter.Register<PlayerReloadIntent>(new ReloadCommandHandler(m_WeaponController));

            m_EventBus.Subscribe<DamageAppliedEvent>(OnDamageApplied);

            BuildCombatPresentation();
            SpawnTargets();
        }

        /// <summary>取枪口世界坐标。</summary>
        private Vector3 ResolveMuzzlePosition()
        {
            if (m_PlayerMotor == null)
            {
                return transform.position + (Vector3.up * MuzzleHeight);
            }

            return m_PlayerMotor.transform.position + (Vector3.up * MuzzleHeight);
        }

        /// <summary>
        /// 把相机朝向投影到地面，作为暴击轴。
        /// </summary>
        /// <remarks>
        /// 每帧重算而不是初始化时算一次：相机在跟随角色，朝向理论上不变，
        /// 但一旦后续加入相机旋转或抖动，写死的轴会悄悄失效，而症状是
        /// "暴击时有时无"——那种问题极难定位。一次点乘的开销可以忽略。
        /// </remarks>
        private void UpdateCriticalAxis()
        {
            if (m_CameraController == null || m_CombatTuning == null)
            {
                return;
            }

            var camera = m_CameraController.GetComponent<Camera>();
            if (camera == null)
            {
                return;
            }

            var axis = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (axis.sqrMagnitude > 1e-4f)
            {
                m_CombatTuning.CriticalAxis = axis.normalized;
            }
        }

        /// <summary>读取本帧的战斗输入并派发命令。</summary>
        private void CollectCombatInput()
        {
            if (m_InputCollector == null)
            {
                m_WeaponController.SetTriggerHeld(false);
                return;
            }

            m_InputCollector.ReadCombatIntent(out var wantsToFire, out var wantsToReload);

            // 瞄准方向每帧都要同步给武器，而不是只在开火时同步。
            // 武器模型的朝向靠它驱动，而玩家不开火时朝向一样在变——
            // 只更新开火帧的话，枪会一直停在最后一次开火的方向上。
            var aim = m_InputCollector.LookDirection;
            m_WeaponController.SetAimDirection(aim);

            // 瞄准点也要一并交给武器：弹道要指向准星所在的那一点，
            // 只给方向的话，射线只能水平打出去，与准星对不上。
            m_WeaponController.SetAimWorldPoint(m_InputCollector.AimWorldPosition);

            if (!wantsToFire)
            {
                m_WeaponController.SetTriggerHeld(false);
            }
            else
            {
                DispatchFire(aim);
            }

            // 换弹与开火是彼此独立的两件事：不按住左键也应该能按 R 换弹。
            if (wantsToReload)
            {
                m_CommandRouter.Dispatch(new PlayerReloadIntent(
                    m_InputCollector.PlayerId,
                    ++m_CommandSequence));
            }
        }

        /// <summary>派发一次射击意图。</summary>
        private void DispatchFire(Vector2F aim)
        {
            var fireResult = m_CommandRouter.Dispatch(new PlayerFireIntent(
                m_InputCollector.PlayerId,
                aim,
                ++m_CommandSequence));

            if (!fireResult.Success)
            {
                m_WeaponController.SetTriggerHeld(false);
            }
        }
    }

    /// <summary>
    /// 灰盒靶子用的护甲参数。
    /// </summary>
    /// <remarks>
    /// 靶子不是物品，因此没有物品定义可以挂 <c>ArmorStats</c> 资产。
    /// 但护甲参数契约（<see cref="IArmorStats"/>）本来就是给"任何能被打的东西"用的，
    /// 这个几行的实现正好证明：战斗规则确实只依赖契约，不依赖资产。
    /// </remarks>
    internal sealed class GreyboxArmorStats : IArmorStats
    {
        /// <summary>创建灰盒护甲参数。</summary>
        /// <param name="level">防护等级。</param>
        /// <param name="maxDurability">最大耐久。</param>
        public GreyboxArmorStats(int level, float maxDurability)
        {
            ProtectionLevel = level;
            MaxDurability = maxDurability;
        }

        /// <inheritdoc />
        public int ProtectionLevel { get; }

        /// <inheritdoc />
        public float MaxDurability { get; }

        /// <inheritdoc />
        public float WearFactor
        {
            get { return 0.35f; }
        }
    }
}
