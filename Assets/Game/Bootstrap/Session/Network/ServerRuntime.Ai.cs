using System;
using System.Collections.Generic;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的 AI 部分：在无头进程里跑同一套 AI 逻辑，并对客户端广播其结果（P2-2 起）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么 AI 必须搬到服务器：</b>AI 会改变世界状态——它开火、扣玩家的血、把玩家打死。
    /// 若每个客户端各跑一份，两名玩家看到的死亡时刻、敌人位置、谁被瞄准全都不一样，
    /// 而这些差异是无法靠"事后对账"弥补的：它们从一开始就是两份真相。
    /// 搬到服务器之后，客户端只剩一件事可做——把服务器给的结果画出来（20.5 接入）。</para>
    ///
    /// <para><b>为什么搬起来不难：</b>AI 模块从一开始就是纯逻辑（不认识 MonoBehaviour、
    /// 不持有场景对象），它要的只是四个接口 + 一个战斗世界。服务器把它们凑齐即可，
    /// 用的还是客户端那份实现（<see cref="PhysicsHitProbe"/> / <see cref="PhysicsMovementCollisionService"/>）。</para>
    ///
    /// <para><b>碰撞载体仍然必要：</b>AI 的移动是"胶囊扫掠"式的（会撞墙、会上坡），
    /// 扫掠需要一个真实存在的 Transform 作为起点。服务器不渲染它，但必须维护它。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>敌人移动体的高度（米），与客户端角色一致。</summary>
        private const float EnemyBodyHeight = 1.8f;

        /// <summary>
        /// 敌人移动碰撞半径（米）。
        /// </summary>
        /// <remarks>
        /// 比胶囊本身的 0.4 大：敌人模型（含持枪手臂）实测宽约 1.3 米，
        /// 用胶囊半径贴墙时手臂会插进墙里。客户端用的是同一个值，两端必须一致——
        /// 否则服务器认为能过的缝，客户端预测会撞墙（表现为持续回拉）。
        /// </remarks>
        private const float EnemyBodyRadius = 0.55f;

        /// <summary>敌人编号的起点。0 以下留给玩家（NGO 的客户端编号从 0 开始）。</summary>
        public const int EnemyIdBase = 1000;

        private AiDirector m_AiDirector;
        private readonly List<AiTargetInfo> m_AiTargets = new List<AiTargetInfo>(4);
        private readonly Dictionary<int, GameObject> m_EnemyBodies = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, int> m_EnemyEntityIds = new Dictionary<int, int>();
        private readonly Dictionary<int, float> m_EnemyGroundHeights = new Dictionary<int, float>();
        private IDisposable m_WeaponNoiseSubscription;

        /// <summary>服务器侧的 AI 调度器；未就绪时为 null。</summary>
        public AiDirector Ai
        {
            get { return m_AiDirector; }
        }

        /// <summary>
        /// 按需建立 AI 世界。
        /// </summary>
        /// <remarks>
        /// <para>依赖两件启动期才具备的东西：服务器自己的导航数据（寻路用）与战斗权威
        /// （AI 要与玩家在**同一个**战斗世界里结算伤害，否则"打中了"这件事会有两份记录）。
        /// 因此做成惰性初始化——第一帧拿不到就下一帧再试。</para>
        ///
        /// <para>AI 在服务器启动后即开始巡逻，不需要等玩家接入：这是战局本来的样子
        /// （敌人守着自己的区域，玩家是闯入者）。</para>
        /// </remarks>
        private bool EnsureAi()
        {
            if (m_AiDirector != null)
            {
                return true;
            }

            if (m_Pathfinding == null)
            {
                return false;
            }

            // 战斗权威是惰性建立的（要等物品目录交接完成），这里主动推一把。
            if (!EnsureCombat())
            {
                return false;
            }

            m_AiDirector = new AiDirector(
                probe: new PhysicsHitProbe(),
                world: m_Combat.World,
                tuning: null,
                eventBus: m_Session.Events,
                profile: new AIPerceptionProfile(),
                pathfinding: m_Pathfinding,
                groundHeight: new NavMeshGroundHeightProvider())
            {
                Bounds = new PlayAreaBounds(
                    new Vector2F(-RaidEnemyLayout.PlayAreaHalfExtent, -RaidEnemyLayout.PlayAreaHalfExtent),
                    new Vector2F(RaidEnemyLayout.PlayAreaHalfExtent, RaidEnemyLayout.PlayAreaHalfExtent)),
            };

            SpawnEnemies();

            // AI 的移动碰撞靠 PhysX 扫掠，而扫掠读的是 Transform 的当前值；
            // 自动同步保证"刚写进去的位置"立刻对物理查询可见（与客户端同一处理）。
            Physics.autoSyncTransforms = true;

            m_WeaponNoiseSubscription = m_Session.Events.Subscribe<WeaponFiredEvent>(OnWeaponFired);

            m_Session.Log.Info(
                $"[服务器] AI 已就绪：{m_AiDirector.Agents.Count} 名敌人，"
                + $"寻路={(m_Pathfinding != null ? "导航网格" : "直线降级")}。");
            return true;
        }

        /// <summary>按共享布局生成敌人，并为每个敌人建一个碰撞载体。</summary>
        private void SpawnEnemies()
        {
            var root = new GameObject("ServerEnemies");
            var armor = new GreyboxArmorStats(RaidEnemyLayout.ArmorLevel, RaidEnemyLayout.ArmorDurability);

            for (var i = 0; i < RaidEnemyLayout.Entries.Count; i++)
            {
                var layout = RaidEnemyLayout.Entries[i];
                var agent = m_AiDirector.SpawnAgent(
                    layout.Spawn,
                    RaidEnemyLayout.SpawnFacingDegrees,
                    RaidEnemyLayout.MaxHealth,
                    armor,
                    new PatrolRoute(layout.Route),
                    AiWeaponProfile.CreateGreyboxRifle(),
                    RaidEnemyLayout.ReserveAmmo);

                var body = new GameObject($"ServerEnemyBody_{i}");
                body.transform.SetParent(root.transform, worldPositionStays: false);

                var ground = ResolveEnemyGroundHeight(agent.Position);
                body.transform.position = new Vector3(agent.Position.X, ground, agent.Position.Y);
                m_EnemyGroundHeights[agent.CombatantId] = ground;

                // 形状与客户端敌人一致：命中判定用的是同一套胶囊尺寸，
                // 否则"客户端看着打中了、服务器说没中"会成为常态（M9-P-13 的同类问题）。
                var collider = body.AddComponent<CapsuleCollider>();
                collider.height = EnemyBodyHeight;
                collider.radius = 0.4f;
                collider.center = new Vector3(0f, EnemyBodyHeight * 0.5f, 0f);
                PhysicsLayers.ApplyUnitLayer(body);

                // 可被命中：射线只认 CombatTargetView 上的编号，没有它子弹会被当成环境命中。
                var targetView = body.AddComponent<CombatTargetView>();
                targetView.Initialize(agent.CombatantId, colorFeedback: false);

                agent.SetMovementCollision(
                    new PhysicsMovementCollisionService(body.transform, EnemyBodyHeight),
                    EnemyBodyRadius);

                m_EnemyBodies[agent.CombatantId] = body;
                m_EnemyEntityIds[agent.CombatantId] = EnemyIdBase + i;
            }
        }

        /// <summary>
        /// 每帧推进 AI：喂目标、喂脚步噪音、推进调度器、把结果写回载体。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        private void TickAi(float deltaTime)
        {
            if (!EnsureAi())
            {
                return;
            }

            m_AiDirector.SetTargets(CollectPlayerTargets());
            TickFootstepNoise(deltaTime);
            m_AiDirector.Tick(deltaTime);
            SyncEnemyBodies();
            ReportAiState();
        }

        /// <summary>
        /// 把所有存活玩家的目标快照交给 AI。
        /// </summary>
        /// <remarks>
        /// 快照里必须带**三维中心点**：AI 的视线判定是一条真实射线，
        /// 只给平面坐标的话射线会贴着地面打、被地形挡住（表现为"AI 永远看不见人"）。
        /// 中心点取自玩家载体的碰撞盒——与命中判定用的是同一个几何体。
        /// </remarks>
        private List<AiTargetInfo> CollectPlayerTargets()
        {
            m_AiTargets.Clear();

            foreach (var pair in m_PlayerBodies)
            {
                var playerId = pair.Key;
                var body = pair.Value;
                if (body == null || !m_World.TryGetSnapshot(playerId, out var snapshot))
                {
                    continue;
                }

                var combatantId = m_Combat != null ? m_Combat.GetCombatantId(playerId) : 0;
                if (combatantId == 0)
                {
                    continue;
                }

                var alive = IsPlayerAlive(playerId);
                var center = body.position;
                if (m_PlayerColliders.TryGetValue(playerId, out var host) && host != null)
                {
                    var targetView = host.GetComponent<CombatTargetView>();
                    if (targetView != null)
                    {
                        center = targetView.CenterWorldPosition;
                    }
                }

                m_AiTargets.Add(new AiTargetInfo(
                    combatantId,
                    snapshot.State.Position,
                    center,
                    alive));
            }

            return m_AiTargets;
        }

        /// <summary>玩家是否还活着（生命来自战斗权威）。</summary>
        private bool IsPlayerAlive(int playerId)
        {
            if (m_Combat == null)
            {
                return false;
            }

            var combatantId = m_Combat.GetCombatantId(playerId);
            return combatantId != 0
                && m_Combat.World.TryGet(combatantId, out var state)
                && state.IsAlive;
        }

        /// <summary>把权威位置写回敌人载体，并让它贴着真实地面。</summary>
        private void SyncEnemyBodies()
        {
            for (var i = 0; i < m_AiDirector.Agents.Count; i++)
            {
                var agent = m_AiDirector.Agents[i];
                if (!m_EnemyBodies.TryGetValue(agent.CombatantId, out var body) || body == null)
                {
                    continue;
                }

                var plane = agent.Position;
                var previous = m_EnemyGroundHeights.TryGetValue(agent.CombatantId, out var height)
                    ? height
                    : body.transform.position.y;

                var ground = GroundProbe.SampleGroundHeight(plane, body.transform.position.y, previous, body.transform);
                m_EnemyGroundHeights[agent.CombatantId] = ground;

                body.transform.position = new Vector3(plane.X, ground, plane.Y);
                body.transform.rotation = Quaternion.Euler(0f, -agent.FacingDegrees, 0f);
            }
        }

        /// <summary>出生点的地面高度：优先问导航网格，问不到就退化为 0 米。</summary>
        private float ResolveEnemyGroundHeight(Vector2F position)
        {
            return NavMeshGroundSampler.TrySample(position, out var height) ? height : 0f;
        }

        /// <summary>该战斗单位编号是不是敌人。</summary>
        private bool IsEnemyCombatant(int combatantId)
        {
            return m_EnemyBodies.ContainsKey(combatantId);
        }

        /// <summary>
        /// 给开火日志补一句"这名敌人此刻在瞄谁、离多远"。
        /// </summary>
        /// <remarks>
        /// "打了但没打中"有很多种成因（瞄错人、被掩体挡住、弹道高度不对），
        /// 而日志里只有"命中=False"时无法区分。把瞄准目标与距离一起打出来，
        /// 一次就能判断是"瞄错了"还是"被挡住/高度不对"。
        /// 玩家开火时不需要这段（玩家的意图在客户端）。
        /// </remarks>
        private string DescribeAiAim(int shooterCombatantId)
        {
            if (m_AiDirector == null || !m_AiDirector.TryGetAgent(shooterCombatantId, out var agent))
            {
                return string.Empty;
            }

            var target = agent.Target;
            if (!target.Exists)
            {
                return " 瞄准=无目标";
            }

            var dx = target.Position.X - agent.Position.X;
            var dy = target.Position.Y - agent.Position.Y;
            var distance = Mathf.Sqrt((dx * dx) + (dy * dy));
            return $" 射手在=({agent.Position.X:F1},{agent.Position.Y:F1})" +
                   $" 瞄准=({target.Position.X:F1},{target.Position.Y:F1}) 距离={distance:F1}米";
        }

        /// <summary>把战斗单位编号翻译成客户端认识的实体编号。</summary>
        /// <remarks>
        /// <para>客户端不认识服务器的战斗世界编号，只认识"玩家编号"与"敌人编号"两套：
        /// 玩家编号就是 NGO 的连接编号（0 起），敌人编号从 <see cref="EnemyIdBase"/> 起，
        /// 因此 <c>1000 以上一定是敌人</c>——这条约定让客户端能用一次比较就分流。</para>
        /// </remarks>
        private int ResolveEntityId(int combatantId)
        {
            var playerId = ResolvePlayerId(combatantId);
            if (playerId != 0 || IsPlayerCombatant(combatantId))
            {
                return playerId;
            }

            return m_EnemyEntityIds.TryGetValue(combatantId, out var enemyId) ? enemyId : 0;
        }

        /// <summary>该编号是否是玩家（玩家的战斗单位编号可能解析出 0 号玩家，因此要单独判一次）。</summary>
        private bool IsPlayerCombatant(int combatantId)
        {
            return m_Combat != null && m_Combat.IsPlayerCombatant(combatantId);
        }

        /// <summary>给日志用：把实体编号写成"玩家 2"或"敌人 3"。</summary>
        private static string DescribeEntity(int entityId)
        {
            return entityId >= EnemyIdBase ? $"敌人 {entityId - EnemyIdBase}" : $"玩家 {entityId}";
        }

        /// <summary>释放 AI 订阅与载体。可重复调用。</summary>
        private void ShutdownAi()
        {
            m_WeaponNoiseSubscription?.Dispose();
            m_WeaponNoiseSubscription = null;

            m_AiDirector?.Dispose();
            m_AiDirector = null;

            foreach (var body in m_EnemyBodies.Values)
            {
                if (body != null)
                {
                    UnityEngine.Object.Destroy(body);
                }
            }

            m_EnemyBodies.Clear();
            m_EnemyEntityIds.Clear();
            m_EnemyGroundHeights.Clear();
            m_AiTargets.Clear();
        }

        /// <summary>
        /// 敌人阵亡后关掉它的碰撞体。
        /// </summary>
        /// <remarks>
        /// 保留碰撞体的话，子弹会打在"看不见的尸体"上：玩家对着空地开枪却收到命中反馈，
        /// 而客户端那边敌人已经倒下（20.5 起会同步死亡状态）。
        /// </remarks>
        private void DisableEnemyCollider(int combatantId)
        {
            if (m_EnemyBodies.TryGetValue(combatantId, out var body) && body != null)
            {
                var collider = body.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }
    }
}
