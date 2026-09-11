using System.Collections.Generic;
using RaidDemo.AI;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 用 Unity 的导航网格实现寻路能力。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么实现放在表现层而不是 AI 模块：</b>AI 模块要能在 EditMode 测试与
    /// 无头服务端里运行，因此它只认识 <see cref="IPathfindingService"/> 这个接口。
    /// 与 <c>PhysicsHitProbe</c> 实现战斗层的 <c>IHitProbe</c> 是同一个做法：
    /// 引擎能力集中在表现层，逻辑层只依赖契约。</para>
    ///
    /// <para><b>坐标换算只在这里发生：</b>逻辑层使用二维平面（<c>X</c> 与 <c>Y</c>），
    /// 世界使用 XZ 平面。两套坐标的桥接必须只有一处实现，
    /// 否则一旦某处写反，症状是"AI 往垂直于目标的方向走"——看起来像状态机出了问题。</para>
    ///
    /// <para><b>高度采样：</b>M5 的装卸平台让地图第一次出现高低差，而逻辑层给出的
    /// 始终是二维坐标。若直接把 y 固定为 0 去算路径，平台上的点在导航网格之外，
    /// 寻路会整体失败——表现为「AI 站在台下一动不动」。因此这里先对起点与终点
    /// 各自做一次 <c>NavMesh.SamplePosition</c>，把平面点吸附到最近的导航网格表面
    /// （含高度），再计算路径。采样半径取 2.5 米，刚好覆盖 1.2 米高的台面。</para>
    /// </remarks>
    public sealed class NavMeshPathfindingService : IPathfindingService
    {
        /// <summary>所有导航区域都能走。</summary>
        private const int AreaMask = NavMesh.AllAreas;

        /// <summary>
        /// 平面点吸附到导航网格的最大搜索半径（米）。
        /// </summary>
        /// <remarks>
        /// 该值必须大于地图上的最大高差，否则站在高台上的目标点会吸附失败。
        /// 当前地图最大高差是装卸平台的 1.2 米，取 2.5 留出一倍余量。
        /// </remarks>
        private const float SampleRadiusMeters = 2.5f;

        /// <summary>
        /// 复用的路径对象。
        /// </summary>
        /// <remarks>
        /// <c>NavMesh.CalculatePath</c> 要求传入一个 <see cref="NavMeshPath"/>。
        /// 每个 AI 每几帧调用一次寻路，若每次都新建一个就是持续的托管分配，
        /// 最终表现为周期性的帧率抖动。
        /// </remarks>
        private readonly NavMeshPath m_Path = new NavMeshPath();

        /// <summary>成功计算出路径的次数。用于调试与性能观测。</summary>
        public int SuccessCount { get; private set; }

        /// <summary>寻路失败的次数。</summary>
        public int FailureCount { get; private set; }

        /// <inheritdoc />
        public bool TryFindPath(Vector2F from, Vector2F to, List<Vector2F> waypoints)
        {
            if (waypoints == null)
            {
                return false;
            }

            waypoints.Clear();

            // 吸附失败时退回原始平面点：宁可让 CalculatePath 去报失败，
            // 也不要把起点悄悄挪到别处——那会让 AI 突然出现在玩家看不见的位置。
            var start = SampleOntoNavMesh(ToWorld(from));
            var end = SampleOntoNavMesh(ToWorld(to));

            if (!NavMesh.CalculatePath(start, end, AreaMask, m_Path))
            {
                FailureCount++;
                return false;
            }

            if (m_Path.status == NavMeshPathStatus.PathInvalid || m_Path.corners.Length < 2)
            {
                // PathInvalid：两点之间根本无法计算路径（例如起点不在导航网格上）。
                // 角落数少于 2：只有起点，没有可走的下一段。
                FailureCount++;
                return false;
            }

            // 跳过 corners[0]：它是起点本身，不是要前往的路径点。
            // 把它也交给移动执行器会让 AI 先"走向自己"，白白浪费一段时间。
            for (var i = 1; i < m_Path.corners.Length; i++)
            {
                waypoints.Add(ToPlane(m_Path.corners[i]));
            }

            // PathPartial 也接受：终点不可达时，已经算出的前半段路径仍然能让 AI
            // 朝目标方向推进。直接放弃会让 AI 站在原地，看起来像卡死。
            SuccessCount++;
            return true;
        }

        /// <summary>平面坐标 → 世界坐标（y 固定为地面高度 0）。</summary>
        private static Vector3 ToWorld(Vector2F point)
        {
            return new Vector3(point.X, 0f, point.Y);
        }

        /// <summary>
        /// 把一个世界坐标点吸附到最近的导航网格表面。
        /// </summary>
        /// <param name="point">原始点。</param>
        /// <returns>吸附后的点；附近没有导航网格时返回原点。</returns>
        private static Vector3 SampleOntoNavMesh(Vector3 point)
        {
            return NavMesh.SamplePosition(point, out var hit, SampleRadiusMeters, AreaMask)
                ? hit.position
                : point;
        }

        /// <summary>世界坐标 → 平面坐标。</summary>
        private static Vector2F ToPlane(Vector3 point)
        {
            return new Vector2F(point.x, point.z);
        }
    }
}
