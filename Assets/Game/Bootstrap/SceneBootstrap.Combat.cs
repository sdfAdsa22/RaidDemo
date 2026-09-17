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

        private CombatWorld m_CombatWorld;
        private CombatTuning m_CombatTuning;
        private PlayerWeapon m_PlayerWeapon;
        private PlayerWeaponController m_WeaponController;
        private PlayerWeaponView m_WeaponView;
        private AudioService m_AudioService;
        private GameAudioDirector m_GameAudio;
        private CombatHud m_CombatHud;

        /// <summary>当前武器在背包里占的格数。用于推算灰盒枪身长度与枪声变体。</summary>
        private int m_WeaponLengthCells = 2;

        /// <summary>当前主武器的物品 ID（表现层按它查模型与枪声）。没有武器时为空。</summary>
        private string m_WeaponItemId;

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
                m_AmmoPouchContainerId);

            m_CommandRouter.Register<PlayerFireIntent>(new FireCommandHandler(m_WeaponController));
            m_CommandRouter.Register<PlayerReloadIntent>(new ReloadCommandHandler(m_WeaponController));

            m_EventBus.Subscribe<DamageAppliedEvent>(OnDamageApplied);

            BuildCombatPresentation();

            // 战局里**不再生成灰盒靶子**（2026-09-14 负责人决定删除）：
            // 它是灰盒阶段的试枪占位物，正常玩法里没有意义，还会在地图上留下一串洋红胶囊
            // （CreatePrimitive 的默认材质在 URP 构建里不存在）。
            // 试枪需求由安全屋的靶场承担——那里有真正的靶子、伤害数字与试枪流程。
        }

        /// <summary>取枪口世界坐标。</summary>
        private Vector3 ResolveMuzzlePosition()
        {
            // 有真实武器模型时，枪口就是枪管末端：弹道、枪口火焰、命中判定全部从那里出发。
            // 模型缺失时退回到"角色位置抬高一点"，与灰盒时代的取值一致。
            if (m_WeaponView != null && m_WeaponView.IsEquipped)
            {
                return m_WeaponView.MuzzleWorldPosition;
            }

            if (m_PlayerMotor == null)
            {
                return transform.position + (Vector3.up * MuzzleHeight);
            }

            // 兜底枪口推到体前：起点落在自己的胶囊体里时，射线的第一个命中就是射手本人
            // （2026-09-16 修自伤缺陷；偏移量与 PlayerWeaponView 的举枪前移量一致）。
            var motor = m_PlayerMotor.transform;
            return motor.position + (Vector3.up * MuzzleHeight) + (motor.forward * 0.6f);
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

            var aim = m_InputCollector.LookDirection;

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
                var reloadResult = m_CommandRouter.Dispatch(new PlayerReloadIntent(
                    m_InputCollector.PlayerId,
                    ++m_CommandSequence));
                // 失败必须说清原因：旧版本里"弹药挂没有匹配弹药"和"游戏卡了"长得一模一样。
                var hint = ReloadFeedback.BuildMessage(
                    reloadResult.Code,
                    m_WeaponController.Runtime?.Weapon?.CaliberId,
                    m_Loadout);
                if (hint != null)
                {
                    m_CombatHud?.ShowHint(hint);
                }
            }
        }

        /// <summary>
        /// 把当前瞄准方向与瞄准点同步给武器控制器（**表现用**的天线）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么单机与联机都必须每帧调用：</b>武器模型的朝向由
        /// <c>m_WeaponController.AimDegrees</c> 驱动，而瞄准点决定弹道与枪口位置。
        /// 只在"开火那一刻"更新的话，枪会停在最后一次开火的方向；
        /// 只在单机分支更新的话，联机时枪从头到尾都不会转（P-46 就是这样复现的）。</para>
        ///
        /// <para>联机时它**不产生任何玩法效果**：扣扳机与命中依旧由服务器结算，
        /// 这里只负责让"我看到的枪"对准"我在瞄的地方"。</para>
        /// </remarks>
        private void SyncAimToWeapon()
        {
            if (m_InputCollector == null || m_WeaponController == null)
            {
                return;
            }

            m_WeaponController.SetAimDirection(m_InputCollector.LookDirection);
            m_WeaponController.SetAimWorldPoint(m_InputCollector.AimWorldPosition);
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
