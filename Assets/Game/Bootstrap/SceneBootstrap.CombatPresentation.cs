using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的战斗表现层装配部分。
    /// </summary>
    /// <remarks>
    /// <para>拆成 partial 文件：主文件已经接近项目规定的 400 行上限，
    /// 而"战斗规则怎么接"与"看到听到什么"本来就是两件事。</para>
    /// <para>把表现层装配单独放一个文件还有一个实际好处：
    /// 这类组件的通病是**写好了却忘记创建**，集中在一处更容易一眼看全。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>
        /// 创建战斗相关的表现层组件。
        /// </summary>
        /// <remarks>
        /// 这一步单独成方法是有原因的：M3 第一次交付时，弹道绘制的类写好了却没有被创建，
        /// 结果玩家开枪看不到任何弹道。功能实现与把功能接起来是两件事，
        /// 放在同一个方法里至少让遗漏更容易被发现。
        /// </remarks>
        private void BuildCombatPresentation()
        {
            EnsureAudioListener();

            var effectsHost = new GameObject("CombatEffects");
            effectsHost.transform.SetParent(transform, worldPositionStays: false);

            var tracer = effectsHost.AddComponent<TracerRenderer>();
            tracer.Bind(m_EventBus);

            // 表现层资产目录：音效、武器模型、战斗特效的唯一来源。
            // 目录为空时下面每一项都会自行退化成"没有声音 / 灰盒枪 / 没有特效"，游戏照常可玩。
            var catalog = m_PresentationCatalog;

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
            m_GameAudio.Bind(
                m_EventBus,
                m_AudioService,
                m_PlayerMotor != null ? m_PlayerMotor.transform : null,
                m_InputCollector != null ? m_InputCollector.PlayerId : 0);

            // 脚步单独一个组件：它关心的是移动事件，与战斗无关，
            // 放在同一个组件里会让"开枪的音效"和"走路的音效"改一处要动两处。
            if (m_PlayerMotor != null)
            {
                var footsteps = effectsHost.AddComponent<FootstepAudioDirector>();
                footsteps.Bind(
                    m_EventBus,
                    m_AudioService,
                    m_PlayerMotor.transform,
                    m_InputCollector != null ? m_InputCollector.PlayerId : 0);
            }

            var viewHost = new GameObject("PlayerWeaponView");
            viewHost.transform.SetParent(transform, worldPositionStays: false);
            m_WeaponView = viewHost.AddComponent<PlayerWeaponView>();
            m_WeaponView.Build(
                m_PlayerMotor != null ? m_PlayerMotor.transform : transform,
                catalog != null ? catalog.RifleWeaponPrefab : null,
                catalog != null ? catalog.PistolWeaponPrefab : null);

            var hudHost = new GameObject("CombatHud");
            hudHost.transform.SetParent(transform, worldPositionStays: false);
            m_CombatHud = hudHost.AddComponent<CombatHud>();
            m_CombatHud.Initialize(m_WeaponController, m_Loadout);

            // 弧形体力槽：挂在角色上方，面向相机。
            if (m_PlayerMotor != null)
            {
                var arcHost = new GameObject("StaminaArcView");
                arcHost.transform.SetParent(transform, worldPositionStays: false);
                var arc = arcHost.AddComponent<StaminaArcView>();
                var viewCamera = m_CameraController != null ? m_CameraController.GetComponent<Camera>() : Camera.main;
                arc.Initialize(m_PlayerMotor.transform, viewCamera, m_EventBus);
            }
        }

        /// <summary>
        /// 确保场景里存在音频监听器。
        /// </summary>
        /// <remarks>
        /// 没有 AudioListener 时 AudioSource 会静默播放：没有报错、没有警告，
        /// 只是听不到任何声音，最容易被误判成音效没做对。
        /// 灰盒场景的相机不一定带监听器，因此在这里兜底。
        /// </remarks>
        private void EnsureAudioListener()
        {
            if (Object.FindAnyObjectByType<AudioListener>() != null)
            {
                return;
            }

            var host = m_CameraController != null ? m_CameraController.gameObject : gameObject;
            host.AddComponent<AudioListener>();
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
            UpdateWeaponView();

            // 滚轮切换武器。界面打开时不响应，避免整理背包时误切。
            var switchDirection = m_InputCollector != null && !inventoryOpen
                ? m_InputCollector.ReadWeaponSwitch()
                : 0;
            if (switchDirection != 0)
            {
                m_CommandRouter.Dispatch(new PlayerSwitchWeaponIntent(
                    m_InputCollector.PlayerId,
                    switchDirection,
                    ++m_CommandSequence));
            }

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

            ApplyWeaponPresentation(weaponItem);
        }

        /// <summary>把当前武器的信息同步给表现层组件。</summary>
        /// <param name="weaponItem">主武器槽上的物品，没有武器时为 null。</param>
        /// <remarks>
        /// 武器占几格同时决定了灰盒枪身的长度与枪声的变体：3 格的步枪更长更沉，
        /// 2 格的手枪更短更脆。这样不需要为了表现效果新增纯展示用的数据字段。
        /// </remarks>
        private void ApplyWeaponPresentation(ItemInstance weaponItem)
        {
            m_WeaponLengthCells = weaponItem != null ? weaponItem.Definition.GridSize.Width : 2;

            var stats = weaponItem?.Definition?.WeaponStats;
            m_CombatHud?.SetWeapon(
                weaponItem != null ? weaponItem.Definition.DisplayName : null,
                stats != null ? stats.CaliberId : null);

            // 枪声、武器模型、枪口火焰共用同一条"长枪 / 短枪"判定，
            // 三者的切换点因此永远一致：不会出现"换了手枪、模型变了、枪声还是步枪"。
            m_GameAudio?.SetLocalWeaponGridWidth(m_WeaponLengthCells);
        }

        /// <summary>让灰盒武器模型跟随角色与瞄准方向。</summary>
        private void UpdateWeaponView()
        {
            if (m_WeaponView == null)
            {
                return;
            }

            var anchor = m_PlayerMotor != null ? m_PlayerMotor.transform.position : transform.position;
            m_WeaponView.UpdateView(
                anchor,
                m_WeaponController.AimDegrees,
                m_WeaponController.Weapon.IsEquipped,
                m_WeaponLengthCells);
        }

    }
}
