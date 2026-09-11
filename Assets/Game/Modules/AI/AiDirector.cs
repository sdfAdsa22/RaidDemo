using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// AI 调度器：持有全部 AI 单位、维护统一时钟、转发噪音与受击、驱动每帧决策。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要一个调度器而不是让每个 AI 自己跑：</b>三个理由。</para>
    /// <list type="number">
    /// <item><description><b>时钟唯一。</b>记忆衰减、调查超时、反应时间都必须基于同一个时间线，
    /// 否则"某个 AI 的超时总是慢半拍"这类问题无法解释。</description></item>
    /// <item><description><b>共享服务只装配一次。</b>射线能力、寻路、战斗调参、事件总线都是全局的，
    /// 让每个 AI 各拿一份必然出现"参数改了但只有一半 AI 生效"。</description></item>
    /// <item><description><b>它是联机与热更的天然边界。</b>服务端只需构造一个调度器并喂入同样的噪音与目标，
    /// 就能得到与客户端一致的行为序列——这正是逻辑与表现分离换来的收益。</description></item>
    /// </list>
    ///
    /// <para>本类同样不引用任何场景对象：它只知道平面坐标、事件与接口。</para>
    /// </remarks>
    public sealed class AiDirector : IDisposable
    {
        /// <summary>默认随机种子。固定值让同一局的 AI 弹道散布可复现。</summary>
        public const uint DefaultBaseSeed = 20260911u;

        /// <summary>相邻 AI 之间派生种子时的步长，取一个大的质数以减少序列重叠。</summary>
        private const uint SeedStride = 977u;

        private readonly List<AiAgent> m_Agents = new List<AiAgent>(8);
        private readonly Dictionary<int, AiAgent> m_AgentsById = new Dictionary<int, AiAgent>(8);
        private readonly uint m_BaseSeed;

        private IDisposable m_DamageSubscription;
        private AiTargetInfo m_Target = AiTargetInfo.None;

        /// <summary>
        /// 创建 AI 调度器。
        /// </summary>
        /// <param name="probe">射线能力（视线与射击共用）。</param>
        /// <param name="world">战斗单位注册表。AI 会把新单位登记进去。</param>
        /// <param name="tuning">战斗调参，可为 null（自动使用默认值）。</param>
        /// <param name="eventBus">事件总线，可为 null（测试里不需要事件）。</param>
        /// <param name="profile">感知与行为参数。</param>
        /// <param name="pathfinding">寻路能力，可为 null（退化为直线推进）。</param>
        /// <param name="baseSeed">随机种子基数。</param>
        public AiDirector(
            IHitProbe probe,
            CombatWorld world,
            CombatTuning tuning,
            EventBus eventBus,
            AIPerceptionProfile profile,
            IPathfindingService pathfinding = null,
            uint baseSeed = DefaultBaseSeed)
        {
            Probe = probe;
            World = world ?? throw new ArgumentNullException(nameof(world));
            Tuning = tuning ?? CombatTuning.Default;
            EventBus = eventBus;
            Profile = profile ?? new AIPerceptionProfile();
            Pathfinding = pathfinding;
            m_BaseSeed = baseSeed;

            if (EventBus != null)
            {
                // 订阅写在调度器而不是每个 AI 上：订阅数量因此与 AI 数量无关。
                m_DamageSubscription = EventBus.Subscribe<DamageAppliedEvent>(OnDamageApplied);
            }
        }

        /// <summary>射线能力。</summary>
        public IHitProbe Probe { get; }

        /// <summary>战斗单位注册表。</summary>
        public CombatWorld World { get; }

        /// <summary>战斗调参。</summary>
        public CombatTuning Tuning { get; }

        /// <summary>事件总线，可为 null。</summary>
        public EventBus EventBus { get; }

        /// <summary>感知与行为参数。</summary>
        public AIPerceptionProfile Profile { get; }

        /// <summary>寻路能力，可为 null。</summary>
        public IPathfindingService Pathfinding { get; }

        /// <summary>活动范围。由启动层按地图尺寸注入。</summary>
        public PlayAreaBounds Bounds { get; set; }

        /// <summary>
        /// AI 武器的穿透力。
        /// </summary>
        /// <remarks>
        /// 玩家的穿透力来自"当前弹匣里装的弹药"；AI 没有背包，因此用常数。
        /// 放在调度器上而不是写死在 Agent 里，是为了让数值调整只改一处。
        /// </remarks>
        public float WeaponPenetration { get; set; } = 0.45f;

        /// <summary>当前战局时刻（秒），由 Tick 累加。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>全部 AI 单位。</summary>
        public IReadOnlyList<AiAgent> Agents
        {
            get { return m_Agents; }
        }

        /// <summary>存活 AI 数量。</summary>
        public int AliveCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < m_Agents.Count; i++)
                {
                    if (m_Agents[i].IsAlive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 生成一个 AI 单位并登记到战斗世界。
        /// </summary>
        /// <param name="spawnPosition">出生位置。</param>
        /// <param name="spawnFacingDegrees">出生朝向（度）。</param>
        /// <param name="health">生命上限。</param>
        /// <param name="armor">护甲参数，可为 null。</param>
        /// <param name="route">巡逻路线，可为 null。</param>
        /// <param name="weapon">武器参数，可为 null（使用默认灰盒步枪）。</param>
        /// <param name="reserveAmmo">初始备弹。</param>
        /// <returns>新建的 AI 单位。</returns>
        public AiAgent SpawnAgent(
            Vector2F spawnPosition,
            float spawnFacingDegrees,
            float health,
            IArmorStats armor = null,
            PatrolRoute route = null,
            AiWeaponProfile weapon = null,
            int reserveAmmo = 90)
        {
            var combatantId = World.Create(health, armor);
            if (!World.TryGet(combatantId, out var combatant))
            {
                throw new InvalidOperationException("战斗世界未能创建 AI 单位，装配顺序可能有问题。");
            }

            var seed = m_BaseSeed + ((uint)m_Agents.Count * SeedStride);
            var agent = new AiAgent(
                this,
                combatant,
                weapon,
                route,
                spawnPosition,
                spawnFacingDegrees,
                seed,
                reserveAmmo);

            m_Agents.Add(agent);
            m_AgentsById[combatantId] = agent;
            return agent;
        }

        /// <summary>按单位标识查找 AI。</summary>
        public bool TryGetAgent(int combatantId, out AiAgent agent)
        {
            return m_AgentsById.TryGetValue(combatantId, out agent);
        }

        /// <summary>更新当前目标（玩家）的快照。每帧由启动层写入。</summary>
        public void SetTarget(in AiTargetInfo target)
        {
            m_Target = target;
        }

        /// <summary>
        /// 转发一次噪音刺激，落在可听范围内的 AI 会记住这个位置。
        /// </summary>
        /// <remarks>
        /// 是否听得见在这里判定（用到各 AI 的位置与感知参数），
        /// 但"听到之后做什么"留给状态机：两者分开之后，
        /// 调整听觉半径不会影响任何行为逻辑。
        /// </remarks>
        public void ReportNoise(in NoiseEvent noise)
        {
            for (var i = 0; i < m_Agents.Count; i++)
            {
                var agent = m_Agents[i];
                if (!agent.IsAlive)
                {
                    continue;
                }

                // 声源自己不需要被告知"自己发出了声音"：它已经在处理更确切的信息
                // （自己开火、自己在跑）。否则 AI 每次开枪都会给自己塞一条噪音。
                if (agent.CombatantId == noise.SourceId)
                {
                    continue;
                }

                if (AISensor.CanHear(agent.Position, noise.Position, noise.RadiusMeters))
                {
                    agent.EnqueueNoise(noise.Position, noise.Tier);
                }
            }
        }

        /// <summary>推进全部 AI 一帧。</summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            ElapsedSeconds += deltaTime;

            for (var i = 0; i < m_Agents.Count; i++)
            {
                m_Agents[i].Tick(deltaTime, ElapsedSeconds, m_Target);
            }
        }

        /// <summary>释放订阅。</summary>
        public void Dispose()
        {
            m_DamageSubscription?.Dispose();
            m_DamageSubscription = null;
        }

        /// <summary>把"某个 AI 挨打了"转告给它本人。</summary>
        private void OnDamageApplied(DamageAppliedEvent evt)
        {
            if (!m_AgentsById.TryGetValue(evt.TargetId, out var agent) || agent == null)
            {
                // 玩家挨打也会走这个事件，但玩家不是 AI，直接忽略。
                return;
            }

            if (evt.WasKilled)
            {
                // 死亡由生命值本身表达，不需要额外的通知：Agent 的 Tick 会自行停止。
                return;
            }

            // 伤害事件里没有攻击者的位置，只有标识。当前唯一的攻击者是玩家，
            // 因此用目标快照的位置作为"子弹来自哪"的近似——对"转身查看"来说足够准确。
            agent.NotifyDamaged(m_Target.Position, evt.AttackerId);
        }
    }
}
