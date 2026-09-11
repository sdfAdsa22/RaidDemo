using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;
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
                m_EventBus);

            m_CommandRouter.Register<PlayerFireIntent>(new FireCommandHandler(m_WeaponController));
            m_CommandRouter.Register<PlayerReloadIntent>(new ReloadCommandHandler(m_WeaponController));

            m_EventBus.Subscribe<DamageAppliedEvent>(OnDamageApplied);

            SpawnTargets();
        }

        /// <summary>
        /// 生成一排灰盒靶子。
        /// </summary>
        /// <remarks>
        /// <para>靶子在运行时创建而不是烘焙进场景：灰盒阶段靶子的位置与数量还要反复调整，
        /// 放在代码里改一个常量就生效，不必每次重新生成场景。</para>
        /// <para>排成一排是为了方便验证射程与散布——站定不动往一个方向打，
        /// 就能看出子弹落在哪、打不打得穿护甲。</para>
        /// </remarks>
        private void SpawnTargets()
        {
            var root = new GameObject("CombatTargets");
            root.transform.SetParent(transform, worldPositionStays: false);

            var armor = new GreyboxArmorStats(TargetArmorLevel, TargetArmorDurability);

            // 正前方必须有靶子：玩家站定不动往瞄准方向打，就能立刻看到命中反馈。
            // 如果排成一圈但正中间是空的，第一次试枪会全打空，看起来像射击没生效。
            for (var i = 0; i < s_TargetLateralOffsets.Length; i++)
            {
                CreateTarget(
                    root.transform,
                    new Vector3(TargetRangeDistance, 0f, s_TargetLateralOffsets[i]),
                    armor);
            }

            CreateTarget(
                root.transform,
                new Vector3(TargetRangeDistance + TargetSpacing, 0f, 0f),
                armor);
        }

        /// <summary>创建单个靶子。</summary>
        private void CreateTarget(Transform parent, Vector3 groundPosition, IArmorStats armor)
        {
            var host = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            host.name = $"Target_{m_CombatWorld.Count + 1}";
            host.transform.SetParent(parent, worldPositionStays: false);

            // 胶囊图元的原点在几何中心，因此抬高半个高度才是"站在地面上"。
            host.transform.position = groundPosition + (Vector3.up * (TargetHeight * 0.5f));
            host.transform.localScale = new Vector3(0.8f, TargetHeight * 0.5f, 0.8f);

            var view = host.AddComponent<CombatTargetView>();
            var id = m_CombatWorld.Create(TargetHealth, armor);
            view.Initialize(id);
            m_TargetViews[id] = view;
        }

        /// <summary>命中后让靶子闪一下。</summary>
        private void OnDamageApplied(DamageAppliedEvent evt)
        {
            if (!m_TargetViews.TryGetValue(evt.TargetId, out var view) || view == null)
            {
                return;
            }

            if (evt.WasKilled)
            {
                view.MarkDestroyed();
                return;
            }

            view.FlashHit();
        }

        /// <summary>
        /// 每帧推进战斗：同步武器、更新枪口与瞄准、驱动扳机与换弹。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="inventoryOpen">背包界面是否打开。打开时不接受射击输入。</param>
        private void UpdateCombat(float deltaTime, bool inventoryOpen)
        {
            if (m_WeaponController == null)
            {
                return;
            }

            SyncEquippedWeapon();
            m_WeaponController.SetMuzzlePosition(ResolveMuzzlePosition());
            UpdateCriticalAxis();

            if (inventoryOpen)
            {
                // 翻背包时松开扳机。否则关掉背包的瞬间会立刻打出一发，
                // 而玩家以为自己刚才只是在整理东西。
                m_WeaponController.SetTriggerHeld(false);
            }
            else
            {
                CollectCombatInput();
            }

            m_WeaponController.Tick(deltaTime);
        }

        /// <summary>把装备槽里的主武器同步到手持武器状态。</summary>
        private void SyncEquippedWeapon()
        {
            var weaponItem = m_Loadout?.Equipment?.Get(EquipmentSlot.PrimaryWeapon);
            var stats = weaponItem?.Definition?.WeaponStats;

            if (m_WeaponController.SyncEquippedWeapon(stats) && stats != null)
            {
                // M1 留下的推迟项 P-08：准星的最大距离改为由当前武器射程决定，
                // 而不是一个写死的常数。换枪之后准星能标出的范围随之变化。
                m_InputCollector?.SetMaxAimDistance(stats.RangeMeters);
            }
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

            if (!wantsToFire)
            {
                m_WeaponController.SetTriggerHeld(false);
                return;
            }

            var aim = m_InputCollector.LookDirection;
            var fireResult = m_CommandRouter.Dispatch(new PlayerFireIntent(
                m_InputCollector.PlayerId,
                aim,
                ++m_CommandSequence));

            if (!fireResult.Success)
            {
                m_WeaponController.SetTriggerHeld(false);
            }

            if (wantsToReload)
            {
                m_CommandRouter.Dispatch(new PlayerReloadIntent(
                    m_InputCollector.PlayerId,
                    ++m_CommandSequence));
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
