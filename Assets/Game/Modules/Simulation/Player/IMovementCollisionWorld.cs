using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 移动碰撞查询：把一条期望位移修正为不穿透障碍的位移。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要抽成接口：</b>移动模拟必须能在无头服务端与 EditMode 测试里运行，
    /// 那些环境里没有 PhysX。因此模拟层只认识这张契约，
    /// 真正的胶囊扫掠由表现层的实现负责。</para>
    ///
    /// <para>接口只回答「从某点走某段位移，实际能走多少」，
    /// 不关心障碍是什么、有几个：滑动、迭代、贴合这些细节全部属于实现。</para>
    /// </remarks>
    public interface IMovementCollisionWorld
    {
        /// <summary>
        /// 把期望位移修正为实际可执行的位移。
        /// </summary>
        /// <param name="from">起点（水平面坐标）。</param>
        /// <param name="delta">期望位移（水平面）。</param>
        /// <param name="radius">移动体半径（米）。</param>
        /// <param name="resolved">
        /// 修正后的实际位移。**任何情况下都必须赋值**：
        /// 没有碰到障碍时等于 <paramref name="delta"/>，被完全挡住时为零。
        /// </param>
        /// <returns>位移被障碍修正过返回 true。</returns>
        bool TryResolveMove(Vector2F from, Vector2F delta, float radius, out Vector2F resolved);
    }
}
