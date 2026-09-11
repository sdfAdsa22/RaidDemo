using System.Collections.Generic;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 寻路能力：给两个平面坐标，返回一串可供行走的路径点。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是接口而不是直接调用 NavMesh：</b>NavMesh 是引擎能力，
    /// 一旦在 AI 逻辑里直接调用，AI 决策就无法在 EditMode 测试里运行，
    /// 也无法在无头服务端复用（主文档 5.6 节约束三）。</para>
    ///
    /// <para><b>坐标约定：</b>参数使用共享层的 <see cref="Vector2F"/>，
    /// 与移动模拟保持同一套平面约定——<c>X</c> 对应世界的 <c>x</c>，
    /// <c>Y</c> 对应世界的 <c>z</c>。二维到三维的换算只发生在实现里，
    /// 逻辑层因此完全不知道 Unity 的坐标系长什么样。</para>
    ///
    /// <para><b>失败语义（重要）：</b>返回 false 表示"这次没有可用路径"，
    /// 可能是目标不可达，也可能是实现所在的导航网格还没准备好。
    /// 调用方**必须**在这种情况下降级为直线推进，而不是原地不动：
    /// 灰盒阶段导航网格由运行时烘焙，若因为时序问题暂时查不到路径，
    /// AI 站着不动的现象会被误判成"状态机坏了"，排查成本极高。</para>
    ///
    /// <para><b>列表复用：</b>路径点写入调用方传入的列表而不是新建一个返回，
    /// 因为寻路会被每个 AI 每帧或每隔数帧调用一次，返回新列表会让
    /// 大量短命对象堆在 GC 里，表现为周期性的帧率抖动。</para>
    /// </remarks>
    public interface IPathfindingService
    {
        /// <summary>
        /// 计算从起点到终点的路径。
        /// </summary>
        /// <param name="from">起点（平面坐标）。</param>
        /// <param name="to">终点（平面坐标）。</param>
        /// <param name="waypoints">
        /// 输出路径点列表。实现应先用 <c>Clear()</c> 清空再写入，
        /// 避免上一次的残留路径混进这一次的结果。
        /// </param>
        /// <returns>找到可用路径返回 true，否则返回 false。</returns>
        bool TryFindPath(Vector2F from, Vector2F to, List<Vector2F> waypoints);
    }
}
