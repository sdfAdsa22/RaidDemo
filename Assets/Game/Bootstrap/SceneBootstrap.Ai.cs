using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.UI;
using Unity.AI.Navigation;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的 AI 装配部分。
    /// </summary>
    /// <remarks>
    /// <para>装配工作分四件事：烘焙导航网格、把玩家登记为可受击目标、
    /// 生成敌人、每帧把"玩家在哪、有多吵"喂给 AI 调度器。</para>
    ///
    /// <para>与战斗装配拆成不同文件，是因为两者的关注点完全不同：
    /// 战斗装配关心"玩家怎么开火"，AI 装配关心"敌人怎么知道玩家在哪"。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>玩家的生命上限。</summary>
        private const float PlayerMaxHealth = 100f;

        /// <summary>敌人的生命上限。</summary>
        private const float EnemyMaxHealth = 100f;

        /// <summary>敌人的护甲等级与耐久。比玩家更容易被打穿，让 AI 不至于变成移动靶。</summary>
        private const int EnemyArmorLevel = 2;
        private const float EnemyArmorDurability = 45f;

        /// <summary>敌人初始备弹（发）。约等于三个弹匣。</summary>
        private const int EnemyReserveAmmo = 90;

        /// <summary>
        /// AI 活动范围半径（米）。
        /// </summary>
        /// <remarks>
        /// 灰盒地面是 60x60，围墙内侧约在 ±29.6 处。这里取 26，留出一点余量，
        /// 避免 AI 贴着围墙卡在角落里。三个撤离点都在这个范围内，
        /// 因此撤离区同样会有 AI 经过——这是刻意的：撤离前的最后一段路必须有风险。
        /// </remarks>
        private const float AiPlayAreaHalfExtent = 26f;

        /// <summary>
        /// 每个敌人的出生点与巡逻路线（M5 四分区地图版本）。
        /// </summary>
        /// <remarks>
        /// <para>五个敌人分布在四个分区：堆场两个（南、北各一）、厂房一个、
        /// 装卸平台一个、外围环道一个。数量按「玩家一路会遇到几次交火」来定：
        /// 太少则搜刮毫无压力，太多则 8 分钟根本搜不完。</para>
        ///
        /// <para>巡逻点全部落在通道与空地上，不穿箱子也不穿墙。路线刻意经过容器附近，
        /// 因此玩家搜刮时被撞见的概率是真实的——这是搜刮读条这个「成本」能成立的前提。</para>
        /// </remarks>
        private static readonly (Vector2F Spawn, Vector2F[] Route)[] s_EnemyLayouts =
        {
            // 堆场南侧：沿南入口向北推进，覆盖弹药箱与武器架之间的通道
            (new Vector2F(5f, -3f), new[]
            {
                new Vector2F(5f, -3f), new Vector2F(12f, -8f), new Vector2F(20f, -8f),
            }),

            // 堆场北侧：绕堆场北端与东侧，守着价值最高的武器架
            (new Vector2F(16f, 22f), new[]
            {
                new Vector2F(16f, 22f), new Vector2F(25f, 24f), new Vector2F(25f, 8f),
            }),

            // 厂房内部：穿过三个厅与两处门洞，是近战交火的主要来源
            (new Vector2F(-24f, -8f), new[]
            {
                new Vector2F(-24f, -8f), new Vector2F(-24f, 2f),
                new Vector2F(-20.5f, 8f), new Vector2F(-12f, 5.5f),
            }),

            // 装卸平台：在高台上巡逻，玩家爬坡时最容易遭遇
            (new Vector2F(2f, -23f), new[]
            {
                new Vector2F(2f, -23f), new Vector2F(-6f, -24f), new Vector2F(8f, -24f),
            }),

            // 外围环道：南北向长距离巡逻，让撤离路线始终有变数
            (new Vector2F(0f, 18f), new[]
            {
                new Vector2F(0f, 18f), new Vector2F(8f, 25f), new Vector2F(-6f, 24f),
            }),
        };

        private AiDirector m_AiDirector;
        private EnemyAgentView[] m_EnemyViews;
        private CombatTargetView m_PlayerTargetView;
        private DamageScreenFlash m_DamageFlash;
        private NavMeshSurface m_NavMeshSurface;
        private int m_PlayerCombatantId;

        /// <summary>本帧交给 AI 的目标快照。开发者模式读的也是这一份。</summary>
        private AiTargetInfo m_CurrentAiTarget = AiTargetInfo.None;

        /// <summary>AI 调度器，供调试与测试读取。</summary>
        public AiDirector Ai
        {
            get { return m_AiDirector; }
        }

        /// <summary>
        /// 玩家当前是否已阵亡。
        /// </summary>
        /// <remarks>
        /// 主循环用它决定"是否屏蔽操作"。判定放在这里而不是散落在各处，
        /// 是为了让主循环只读一个布尔值，避免每个系统各自去查战斗世界。
        /// </remarks>
        private bool IsPlayerDefeated()
        {
            if (m_CombatWorld == null || m_PlayerCombatantId == 0)
            {
                return false;
            }

            return m_CombatWorld.TryGet(m_PlayerCombatantId, out var state) && !state.IsAlive;
        }

        /// <summary>玩家在战斗层中的单位标识。</summary>
        public int PlayerCombatantId
        {
            get { return m_PlayerCombatantId; }
        }

        /// <summary>装配 AI 系统。</summary>
        private void InitializeAi()
        {
            BuildNavMesh();

            var profile = new AIPerceptionProfile();
            var problem = profile.Validate();
            if (problem != null)
            {
                Debug.LogError($"[RaidDemo] AI 感知参数不合法：{problem}。将使用默认值继续运行。", this);
                profile = new AIPerceptionProfile();
            }

            var pathfinding = new NavMeshPathfindingService();
            m_AiDirector = new AiDirector(
                new PhysicsHitProbe(),
                m_CombatWorld,
                m_CombatTuning,
                m_EventBus,
                profile,
                pathfinding);

            m_AiDirector.Bounds = new PlayAreaBounds(
                new Vector2F(-AiPlayAreaHalfExtent, -AiPlayAreaHalfExtent),
                new Vector2F(AiPlayAreaHalfExtent, AiPlayAreaHalfExtent));

            RegisterPlayerCombatant();
            BuildDamageFeedback();
            SpawnEnemies();
            EnablePhysicsAutoSync();
            SubscribeWeaponNoise();
            BuildDebugTools();
        }

        /// <summary>
        /// 构建玩家受击反馈。
        /// </summary>
        /// <remarks>
        /// 必须与玩家单位一起创建：受击反馈绑定的是玩家的单位标识，
        /// 若标识还没登记就绑定，界面会永远收不到事件——而画面上的症状是"被打中却毫无反馈"，
        /// 很容易被误判成伤害没结算。
        /// </remarks>
        private void BuildDamageFeedback()
        {
            if (m_PlayerCombatantId == 0)
            {
                return;
            }

            var host = new GameObject("DamageScreenFlash");
            host.transform.SetParent(transform, worldPositionStays: false);
            m_DamageFlash = host.AddComponent<DamageScreenFlash>();
            m_DamageFlash.Bind(m_EventBus, m_PlayerCombatantId);
        }

        /// <summary>
        /// 烘焙导航网格。
        /// </summary>
        /// <remarks>
        /// <para>选择**运行时烘焙**而不是在编辑器里预先烘焙，原因在场景生成器里已经写明：
        /// 灰盒布局由代码生成，预先烘焙的数据会与布局脱节。</para>
        /// <para>找不到 <see cref="NavMeshSurface"/> 时只警告不报错：AI 会退化为直线推进，
        /// 行为仍然可测；而直接抛异常会让整个灰盒场景无法进入。</para>
        /// </remarks>
        private void BuildNavMesh()
        {
            m_NavMeshSurface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (m_NavMeshSurface == null)
            {
                Debug.LogWarning(
                    "[RaidDemo] 场景里没有 NavMeshSurface，AI 将退化为直线移动。"
                    + "请执行菜单「RaidDemo → 生成灰盒测试场景」重新生成场景。",
                    this);
                return;
            }

            m_NavMeshSurface.BuildNavMesh();
        }

        /// <summary>
        /// 打开物理系统的自动同步。
        /// </summary>
        /// <remarks>
        /// <para>本项目的角色由 Transform 直接驱动（没有刚体），而 Unity 默认不会在每次物理查询前
        /// 同步被移动过的碰撞体。后果非常隐蔽：AI 的射线会打在敌人**出生时**的位置上，
        /// 表现为"敌人跑起来之后就打不中了"。</para>
        /// <para>打开自动同步的代价是每次物理查询前多做一次脏检查。本工程每帧的物理查询只有个位数，
        /// 这点开销换掉一整类难以复现的命中问题，非常划算。</para>
        /// </remarks>
        private void EnablePhysicsAutoSync()
        {
            Physics.autoSyncTransforms = true;
        }

        /// <summary>
        /// 把玩家登记为战斗层的可受击单位。
        /// </summary>
        /// <remarks>
        /// <para>玩家在此之前的身份只是"输入来源"，AI 无法攻击他。登记之后，玩家与敌人在战斗层里
        /// 是对等的单位，共用同一套伤害与护甲规则——这正是 M3 把 <c>CombatantState</c>
        /// 做成"任何能被打的东西都能用"的回报。</para>
        /// <para>护甲取自当前装备的身体护甲。护甲耐久的消耗记在战斗状态里而不是物品上，
        /// 属于灰盒阶段的简化，M6 的装备持久化会把它接回物品。</para>
        /// </remarks>
        private void RegisterPlayerCombatant()
        {
            if (m_PlayerMotor == null)
            {
                Debug.LogWarning("[RaidDemo] 没有玩家对象，AI 将没有可攻击的目标。", this);
                return;
            }

            var armor = m_Loadout?.Equipment?.Get(EquipmentSlot.Body)?.Definition?.ArmorStats;
            m_PlayerCombatantId = m_CombatWorld.Create(PlayerMaxHealth, armor);

            // 只登记标识、不接管颜色：玩家的配色与受击反馈由角色本身和界面负责。
            m_PlayerTargetView = m_PlayerMotor.gameObject.AddComponent<CombatTargetView>();
            m_PlayerTargetView.Initialize(m_PlayerCombatantId, colorFeedback: false);
        }

        /// <summary>按布局表生成敌人。</summary>
        private void SpawnEnemies()
        {
            var root = new GameObject("Enemies");
            root.transform.SetParent(transform, worldPositionStays: false);

            var armor = new GreyboxArmorStats(EnemyArmorLevel, EnemyArmorDurability);
            m_EnemyViews = new EnemyAgentView[s_EnemyLayouts.Length];

            for (var i = 0; i < s_EnemyLayouts.Length; i++)
            {
                var layout = s_EnemyLayouts[i];
                var route = new PatrolRoute(layout.Route);
                var agent = m_AiDirector.SpawnAgent(
                    layout.Spawn,
                    spawnFacingDegrees: 180f,
                    health: EnemyMaxHealth,
                    armor: armor,
                    route: route,
                    weapon: AiWeaponProfile.CreateGreyboxRifle(),
                    reserveAmmo: EnemyReserveAmmo);

                var host = new GameObject($"Enemy_{agent.CombatantId:D2}");
                host.transform.SetParent(root.transform, worldPositionStays: false);

                var view = host.AddComponent<EnemyAgentView>();
                view.Initialize(agent, m_EventBus);
                m_EnemyViews[i] = view;
            }
        }

        /// <summary>
        /// 每帧推进 AI：更新目标、广播噪音、推进调度器。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="inputBlocked">玩家输入是否被屏蔽（背包打开或已阵亡）。</param>
        private void UpdateAi(float deltaTime, bool inputBlocked)
        {
            if (m_AiDirector == null)
            {
                return;
            }

            var playerCombatant = m_CombatWorld.TryGet(m_PlayerCombatantId, out var state) ? state : null;
            var playerAlive = playerCombatant != null && playerCombatant.IsAlive;

            // 生命值推送给界面。推送而不是让界面自己取：界面不需要认识战斗层的单位状态。
            m_CombatHud?.SetHealth(
                playerCombatant != null ? playerCombatant.Health : 0f,
                PlayerMaxHealth,
                playerAlive);

            // 快照只构造一次并交给两个使用者：调度器与开发者模式。
            // 若两边各构造一份，调试面板显示的位置会比 AI 实际使用的晚一帧，
            // 看起来就像"AI 在追空气"。
            m_CurrentAiTarget = new AiTargetInfo(
                m_PlayerCombatantId,
                m_MoveHandler != null ? m_MoveHandler.Simulator.State.Position : Vector2F.Zero,
                m_PlayerTargetView != null ? m_PlayerTargetView.CenterWorldPosition : transform.position,
                playerAlive);
            m_AiDirector.SetTarget(m_CurrentAiTarget);

            if (playerAlive)
            {
                UpdatePlayerNoise(deltaTime, inputBlocked);
            }
            else
            {
                // 阵亡即静音：噪音是"移动发出的声音"，而尸体不会跑动。
                m_CurrentNoiseTier = NoiseTier.Silent;
                NotifyPlayerKilled();
            }

            m_AiDirector.Tick(deltaTime);
        }

        /// <summary>
        /// 玩家阵亡：交给战局会话判负。
        /// </summary>
        /// <remarks>
        /// <para>M4 阶段这里写的是「4 秒后原地灰盒复活」的临时代码，目的是让一次失误
        /// 不至于中断 AI 调试。M5 起阵亡就是这一局的结束。</para>
        ///
        /// <para>复活会直接抹掉「贪心要付出代价」这条核心规则：如果死亡可以无限撤销，
        /// 那么「再多搜一个箱子」就永远是正确的选择，整个紧张感也就不存在了。</para>
        /// </remarks>
        private void NotifyPlayerKilled()
        {
            m_RaidSession?.NotifyPlayerKilled();
        }
    }
}
