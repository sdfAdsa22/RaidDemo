using System.Collections.Generic;
using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 可编排的射线替身：既可以"什么都没打中"，也可以返回一条预先安排好的命中。
    /// </summary>
    /// <remarks>
    /// 视线判定与射击命中都用它。这样的测试可以在不加载场景的前提下
    /// 精确构造"隔着掩体"或"正对目标"这两种在真实场景里极难稳定复现的条件。
    /// </remarks>
    internal sealed class ScriptedHitProbe : IHitProbe
    {
        /// <summary>是否返回命中。</summary>
        public bool ReturnsHit { get; set; }

        /// <summary>命中结果。</summary>
        public HitInfo Result { get; set; }

        /// <summary>被调用的次数。</summary>
        public int CallCount { get; private set; }

        /// <summary>最近一次投射使用的最大距离。</summary>
        public float LastMaxDistance { get; private set; }

        /// <summary>最近一次投射的方向。</summary>
        public Vector3 LastDirection { get; private set; }

        /// <inheritdoc />
        public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit)
        {
            CallCount++;
            LastMaxDistance = maxDistance;
            LastDirection = direction;
            hit = Result;
            return ReturnsHit;
        }

        /// <summary>只让射线命中指定单位，便于表达"看见了谁"。</summary>
        public void HitTarget(int targetId, float distance = 5f)
        {
            ReturnsHit = true;
            Result = new HitInfo(targetId, new Vector3(0f, 1f, 1f), new Vector3(0f, 1f, 1f), distance);
        }

        /// <summary>让射线打空（例如目标被掩体挡住、或视线被墙截断）。</summary>
        public void Miss()
        {
            ReturnsHit = false;
            Result = default;
        }
    }

    /// <summary>
    /// 可编排的寻路替身：返回一条预设路径，或者直接失败。
    /// </summary>
    /// <remarks>
    /// 用假路径而不是真导航网格，测试才能真正验证"AI 沿着路径点走"这件事：
    /// 真实导航网格的结果取决于烘焙参数与场景几何，断言会变得脆弱且难以解释。
    /// </remarks>
    internal sealed class ScriptedPathfindingService : IPathfindingService
    {
        private readonly List<Vector2F> m_Waypoints = new List<Vector2F>();

        /// <summary>为 false 时表示寻路失败，调用方应退化直线推进。</summary>
        public bool Succeeds { get; set; } = true;

        /// <summary>被调用的次数，用于验证"按间隔重新寻路"而不是每帧。</summary>
        public int CallCount { get; private set; }

        /// <summary>设置下一次返回的路径点。</summary>
        public void SetWaypoints(params Vector2F[] waypoints)
        {
            m_Waypoints.Clear();
            if (waypoints != null)
            {
                m_Waypoints.AddRange(waypoints);
            }
        }

        /// <inheritdoc />
        public bool TryFindPath(Vector2F from, Vector2F to, List<Vector2F> waypoints)
        {
            CallCount++;
            waypoints.Clear();

            if (!Succeeds || m_Waypoints.Count == 0)
            {
                return false;
            }

            waypoints.AddRange(m_Waypoints);
            return true;
        }
    }

    /// <summary>
    /// AI 测试的公共装配：造一个调度器、一个玩家目标和必要的替身。
    /// </summary>
    /// <remarks>
    /// 集中在一处是为了让每个用例只表达"这次要验证什么"，
    /// 而不是把十几行装配代码复制二十遍——复制出来的装配迟早会各自漂移。
    /// </remarks>
    internal sealed class AiTestFixture
    {
        /// <summary>武器穿透力。测试里固定值，便于断言伤害。</summary>
        public const float WeaponPenetration = 0.45f;

        /// <summary>事件总线。</summary>
        public EventBus Bus { get; } = new EventBus();

        /// <summary>战斗世界。</summary>
        public CombatWorld World { get; } = new CombatWorld();

        /// <summary>射线替身。</summary>
        public ScriptedHitProbe Probe { get; } = new ScriptedHitProbe();

        /// <summary>寻路替身。默认失败，即直线推进。</summary>
        public ScriptedPathfindingService Pathfinding { get; } = new ScriptedPathfindingService();

        /// <summary>感知参数。</summary>
        public AIPerceptionProfile Profile { get; }

        /// <summary>调度的 AI。</summary>
        public AiDirector Director { get; }

        /// <summary>玩家在战斗层中的标识。</summary>
        public int PlayerId { get; }

        /// <summary>创建一套完整的测试环境。</summary>
        /// <param name="profile">自定义感知参数，可为 null（使用默认值）。</param>
        /// <param name="playerPosition">玩家初始位置。</param>
        public AiTestFixture(AIPerceptionProfile profile = null, Vector2F playerPosition = default)
        {
            Profile = profile ?? new AIPerceptionProfile();
            Director = new AiDirector(
                Probe,
                World,
                new CombatTuning(),
                Bus,
                Profile,
                Pathfinding);

            Director.WeaponPenetration = WeaponPenetration;

            PlayerId = World.Create(100f);
            SetPlayer(playerPosition, alive: true);
        }

        /// <summary>更新玩家目标快照。</summary>
        public void SetPlayer(Vector2F position, bool alive)
        {
            var center = new Vector3(position.X, 1f, position.Y);
            Director.SetTarget(new AiTargetInfo(PlayerId, position, center, alive));
        }

        /// <summary>取得玩家单位的战斗状态。</summary>
        public CombatantState GetPlayer()
        {
            World.TryGet(PlayerId, out var state);
            return state;
        }

        /// <summary>
        /// 生成一个默认配置的 AI。
        /// </summary>
        /// <param name="position">出生位置。</param>
        /// <param name="facingDegrees">出生朝向（度）。</param>
        /// <param name="route">巡逻路线，可为 null。</param>
        /// <param name="health">生命上限。</param>
        /// <param name="weapon">武器参数，可为 null。</param>
        /// <param name="reserveAmmo">备弹。</param>
        public AiAgent SpawnAgent(
            Vector2F position = default,
            float facingDegrees = 0f,
            PatrolRoute route = null,
            float health = 100f,
            AiWeaponProfile weapon = null,
            int reserveAmmo = 90)
        {
            return Director.SpawnAgent(
                position,
                facingDegrees,
                health,
                armor: null,
                route: route,
                weapon: weapon,
                reserveAmmo: reserveAmmo);
        }

        /// <summary>推进若干次调度，总时长等于 count × stepSeconds。</summary>
        public void Advance(float totalSeconds, float stepSeconds = 0.1f)
        {
            var steps = Mathf.Max(1, Mathf.RoundToInt(totalSeconds / stepSeconds));
            for (var i = 0; i < steps; i++)
            {
                Director.Tick(stepSeconds);
            }
        }
    }
}
