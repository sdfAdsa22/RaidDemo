using System.Collections.Generic;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 一次移动推进的结果。
    /// </summary>
    /// <remarks>
    /// 用结构体返回而不是让调用方去读一堆属性：移动每帧都会算一次，
    /// 结构体不产生堆分配，也不会出现"读到上一帧残留值"的问题。
    /// </remarks>
    public readonly struct AiMovementResult
    {
        /// <summary>创建移动结果。</summary>
        public AiMovementResult(Vector2F position, Vector2F direction, bool moved, bool reachedGoal, bool usedPath)
        {
            Position = position;
            Direction = direction;
            Moved = moved;
            ReachedGoal = reachedGoal;
            UsedPath = usedPath;
        }

        /// <summary>推进后的位置。</summary>
        public Vector2F Position { get; }

        /// <summary>本帧实际移动方向（未移动时为零向量）。</summary>
        public Vector2F Direction { get; }

        /// <summary>本帧是否发生了位移。</summary>
        public bool Moved { get; }

        /// <summary>是否已经到达终点。</summary>
        public bool ReachedGoal { get; }

        /// <summary>本帧是否在沿寻路结果行走（false 表示退化为直线推进）。</summary>
        public bool UsedPath { get; }
    }

    /// <summary>
    /// AI 的移动执行器：把"想去哪"变成"这一帧走到哪"。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用 NavMeshAgent 直接驱动 Transform：</b>NavMeshAgent 会自己移动对象、
    /// 自己写 Transform，于是"AI 的移动规则"就散落在引擎组件里，既不能在无头服务端复用，
    /// 也没法在 EditMode 测试里断言。本项目的做法是：寻路只用来问路（<see cref="IPathfindingService"/>），
    /// 走路由这里算，表现层只负责把结果搬到 Transform 上——与玩家的处理完全一致。</para>
    ///
    /// <para><b>按间隔重新寻路而不是每帧：</b>目标点每帧都在动，但路径不需要每帧重算。
    /// 每帧寻路会让 AI 的路径抖动（相邻两帧的目标点差异导致路径在两个绕行方案之间跳），
    /// 表现为 AI 在原地左右摇摆。</para>
    ///
    /// <para><b>寻路失败时退化为直线：</b>失败通常意味着导航网格还没烘焙好或目标不可达。
    /// 原地不动会让"AI 卡住"变成必须排查的故障，而直线推进至少能让灰盒阶段继续验证行为。</para>
    /// </remarks>
    public sealed class AiMovement
    {
        /// <summary>判定"目标点没变"的距离阈值（米）。小于它就不重新寻路。</summary>
        private const float GoalChangeThreshold = 0.35f;

        private readonly IPathfindingService m_Pathfinding;
        private readonly AIPerceptionProfile m_Profile;
        private readonly List<Vector2F> m_Path = new List<Vector2F>(16);

        private Vector2F m_PathGoal;
        private bool m_HasPathGoal;
        private float m_RepathTimer;

        /// <summary>创建移动执行器。</summary>
        /// <param name="pathfinding">寻路能力。传 null 时一律直线推进。</param>
        /// <param name="profile">参数来源（重新寻路间隔、路径点到达距离等）。</param>
        public AiMovement(IPathfindingService pathfinding, AIPerceptionProfile profile)
        {
            m_Pathfinding = pathfinding;
            m_Profile = profile;
        }

        /// <summary>活动范围。默认不限制。</summary>
        public PlayAreaBounds Bounds { get; set; }

        /// <summary>当前尚未走完的路径点数量。</summary>
        public int PendingWaypointCount
        {
            get { return m_Path.Count; }
        }

        /// <summary>是否正在沿一条多于一个点的路径行走。</summary>
        public bool IsFollowingPath
        {
            get { return m_Path.Count > 0; }
        }

        /// <summary>最近一次重新寻路是否成功。</summary>
        public bool LastRepathSucceeded { get; private set; }

        /// <summary>
        /// 推进一帧移动。
        /// </summary>
        /// <param name="from">当前位置。</param>
        /// <param name="goal">想要到达的位置。</param>
        /// <param name="speed">移动速度（米/秒）。</param>
        /// <param name="deltaTime">时间步长（秒）。</param>
        public AiMovementResult Step(Vector2F from, Vector2F goal, float speed, float deltaTime)
        {
            var clampedGoal = Bounds.Clamp(goal);
            if (deltaTime <= 0f)
            {
                return new AiMovementResult(from, Vector2F.Zero, false, false, false);
            }

            UpdatePath(from, clampedGoal, deltaTime);

            // 走到路径点附近就把它从队列里去掉：留着会让 AI 来回蹭那个点。
            var reach = m_Profile != null ? m_Profile.WaypointReachDistance : 0.5f;
            var reachSqr = reach * reach;
            while (m_Path.Count > 0 && Vector2F.SqrDistance(from, m_Path[0]) <= reachSqr)
            {
                m_Path.RemoveAt(0);
            }

            if (m_Path.Count == 0 && Vector2F.Distance(from, clampedGoal) <= reach)
            {
                return new AiMovementResult(from, Vector2F.Zero, false, true, false);
            }

            var usedPath = m_Path.Count > 0;
            var target = usedPath ? m_Path[0] : clampedGoal;
            var toTarget = target - from;
            var distance = toTarget.Magnitude;
            if (distance < 1e-4f || speed <= 0f)
            {
                return new AiMovementResult(from, Vector2F.Zero, false, false, usedPath);
            }

            var direction = toTarget / distance;
            var step = speed * deltaTime;
            var next = step >= distance ? target : from + (direction * step);
            next = Bounds.Clamp(next);

            // 到达判定用移动后的位置：否则"这一帧刚好走到终点"会被报成未到达，
            // 调用方要再等一帧，表现为 AI 到点后停一下才切换行为。
            var reached = m_Path.Count == 0 && Vector2F.Distance(next, clampedGoal) <= reach;
            return new AiMovementResult(next, direction, true, reached, usedPath);
        }

        /// <summary>丢弃当前路径。目标切换或状态切换时调用，避免沿着旧路径继续走。</summary>
        public void ResetPath()
        {
            m_Path.Clear();
            m_HasPathGoal = false;
            m_RepathTimer = 0f;
        }

        /// <summary>
        /// 把当前尚未走完的路径点复制到调用方提供的列表里。
        /// </summary>
        /// <param name="destination">目标列表。会先被清空。</param>
        /// <returns>复制的路径点数量。</returns>
        /// <remarks>
        /// <para>仅供调试可视化读取，逻辑层自己不使用它。</para>
        /// <para><b>为什么是"复制到传入的列表"而不是对外暴露只读集合：</b>把内部列表包装成
        /// <c>IReadOnlyList</c> 返回看似安全，但调用方一旦把它转回 <c>List</c> 就能直接改内部状态，
        /// 而"AI 的路径被界面改坏"这种缺陷极难定位。复制一次的开销只在调试开关打开时发生。</para>
        /// </remarks>
        public int CopyWaypoints(List<Vector2F> destination)
        {
            if (destination == null)
            {
                return 0;
            }

            destination.Clear();
            for (var i = 0; i < m_Path.Count; i++)
            {
                destination.Add(m_Path[i]);
            }

            return destination.Count;
        }

        /// <summary>决定本帧是否需要重新寻路，并在需要时执行一次。</summary>
        private void UpdatePath(Vector2F from, Vector2F goal, float deltaTime)
        {
            m_RepathTimer -= deltaTime;

            var goalChanged = !m_HasPathGoal
                              || Vector2F.SqrDistance(m_PathGoal, goal) > GoalChangeThreshold * GoalChangeThreshold;

            if (!goalChanged && m_RepathTimer > 0f)
            {
                return;
            }

            m_PathGoal = goal;
            m_HasPathGoal = true;
            m_RepathTimer = m_Profile != null ? m_Profile.RepathIntervalSeconds : 0.6f;

            if (m_Pathfinding == null)
            {
                m_Path.Clear();
                LastRepathSucceeded = false;
                return;
            }

            LastRepathSucceeded = m_Pathfinding.TryFindPath(from, goal, m_Path);
            if (!LastRepathSucceeded)
            {
                // 路径为空即退化为直线推进，见类型注释。
                m_Path.Clear();
            }
        }
    }
}
