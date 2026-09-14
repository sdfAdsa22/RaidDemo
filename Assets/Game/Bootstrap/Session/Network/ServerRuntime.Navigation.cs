using System.Collections.Generic;
using System.Diagnostics;
using RaidDemo.AI;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的导航部分：把地图碰撞体烘焙成导航网格，供服务器侧 AI 寻路。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么服务器要自己烘焙一次：</b>客户端的那份导航网格是"表现层的便利"，
    /// 而 P2-2 起 AI 只在服务器上跑，权威寻路必须有一份服务器自己的导航数据。
    /// 两端加载的是同一张地图、读的是同一个 <see cref="NavMeshSurface"/> 上的参数，
    /// 因此烘焙结果一致——"两端各烘一次"在这里不会造成行为分歧。</para>
    ///
    /// <para><b>为什么运行时烘焙在无头进程里可行：</b><see cref="NavMeshSurface"/> 收集的是
    /// <b>物理碰撞体</b>而不是渲染网格（见场景生成器的 <c>useGeometry</c> 设置），
    /// 所以不需要模型开启 Read/Write，也不需要任何图形设备——
    /// 无头服务器的 PhysX 已经在跑（玩家的胶囊扫掠就依赖它）。</para>
    ///
    /// <para><b>为什么启动时验证一次：</b>"烘焙成功"与"烘焙出一张能用的网格"是两件事：
    /// 导航网格为空时寻路会全部失败，而 AI 的降级策略是直线推进，
    /// 于是症状是"敌人穿墙走直线"而不是任何报错。启动时算三条真实路径并打到日志里，
    /// 让"导航数据到底有没有生效"变成一眼可见的事实而不是猜测。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 启动自检用的探针路径：起点 → 终点（平面坐标，与 AI 的坐标约定一致）。
        /// </summary>
        /// <remarks>
        /// 三条路径分别覆盖地图上三种不同的地形：谷底堆场的平地、装卸平台的高差、
        /// 厂房内部穿过门洞的绕行。只测平地会让"高度采样坏了"这类问题漏过去。
        /// </remarks>
        private static readonly (Vector2F From, Vector2F To, string Name)[] s_NavigationProbes =
        {
            (new Vector2F(5f, -3f), new Vector2F(20f, -8f), "堆场平地"),
            (new Vector2F(2f, -23f), new Vector2F(-6f, -24f), "装卸平台高差"),
            (new Vector2F(-24f, -8f), new Vector2F(-12f, 5.5f), "厂房内部"),
        };

        private NavMeshSurface m_NavMeshSurface;
        private IPathfindingService m_Pathfinding;
        private bool m_NavigationPending;
        private bool m_NavigationWaitingReported;

        /// <summary>服务器侧的寻路能力。P2-2 起交给 AI 调度器使用；未就绪时为 null。</summary>
        public IPathfindingService Pathfinding => m_Pathfinding;

        /// <summary>服务器侧烘焙出的导航网格宿主（调试与测试可读）。</summary>
        public NavMeshSurface NavigationSurface => m_NavMeshSurface;

        /// <summary>
        /// 标记"导航数据待建立"。
        /// </summary>
        /// <remarks>
        /// <para><b>P4.5-b 起只对战局世界烘焙</b>：安全屋里没有 AI，也没有需要寻路的单位，
        /// 烘一张空网格除了花时间没有任何意义——而"安全屋里为什么有导航数据"本身就是个误导。
        /// 世界切换时由 <c>ServerRuntime.World</c> 在进入战局后调用本方法。</para>
        ///
        /// <para>只登记意图，不在这里烘焙：地图场景是在世界切换里加载的，
        /// 而 <c>SceneManager.LoadScene</c> 在 <c>AfterSceneLoad</c> 回调里发出时**要到本帧稍后才生效**
        /// （见 <see cref="TickNavigation"/> 的说明）。</para>
        /// </remarks>
        private void BeginNavigation()
        {
            if (m_WorldKind != ServerWorldKind.Raid)
            {
                // 安全屋世界：没有 AI，不需要导航。
                m_NavigationPending = false;
                m_NavigationWaitingReported = false;
                return;
            }

            m_NavigationPending = true;
        }

        /// <summary>
        /// 每帧检查：地图场景是否已经成为活动场景，是则烘焙导航网格。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么必须等到"活动场景换过去"：</b>服务器的地图是在
        /// <c>RuntimeInitializeOnLoadMethod(AfterSceneLoad)</c> 里加载的，此时引擎仍在处理首个场景的加载流程，
        /// <c>SceneManager.LoadScene</c> 会被推迟到本帧稍后才真正生效。
        /// 在那之前 <c>FindAnyObjectByType</c> 看到的还是安全屋——所以"刚调用完 LoadScene 就去找地图里的东西"
        /// 必然找不到，而现象是"地图明明加载了，导航网格却是空的"。</para>
        ///
        /// <para>这里不做超时放弃：服务器进程本来就常驻，地图晚一两帧就绪是正常情况。</para>
        /// </remarks>
        private void TickNavigation()
        {
            if (!m_NavigationPending)
            {
                return;
            }

            var mapScene = m_WorldSceneName;
            var active = SceneManager.GetActiveScene().name;
            if (active != mapScene)
            {
                if (!m_NavigationWaitingReported)
                {
                    m_NavigationWaitingReported = true;
                    m_Session?.Log.Verbose(
                        $"[服务器] 等待地图场景生效：当前「{active}」，目标「{mapScene}」。");
                }

                return;
            }

            m_NavigationPending = false;
            BuildNavigation();
        }

        /// <summary>
        /// 烘焙服务器侧导航网格并做一次启动自检。
        /// </summary>
        /// <remarks>
        /// 前提是地图场景已经生效：导航网格只能从场景里的碰撞体烘焙出来，
        /// 地图还没生效时场景里根本没有地面。
        /// </remarks>
        private void BuildNavigation()
        {
            m_NavMeshSurface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (m_NavMeshSurface == null)
            {
                // 不报错、不中断：AI 的寻路契约允许失败并降级为直线推进，
                // 服务器本身（玩家移动 / 战斗）不依赖导航数据，不该因此起不来。
                m_Session?.Log.Warning(
                    $"[服务器] 地图场景「{SceneManager.GetActiveScene().name}」里没有 NavMeshSurface，"
                    + "AI 将退化为直线移动。"
                    + "请执行菜单「RaidDemo → 生成灰盒测试场景」重新生成场景。");
                return;
            }

            var watch = Stopwatch.StartNew();
            m_NavMeshSurface.BuildNavMesh();
            watch.Stop();

            m_Pathfinding = new NavMeshPathfindingService();
            var vertices = CountNavMeshVertices();
            m_Session?.Log.Info(
                $"[服务器] 导航数据已烘焙：三角面顶点 {vertices} 个，耗时 {watch.ElapsedMilliseconds} ms。");

            if (vertices == 0)
            {
                // 空网格是"静默失败"的典型：寻路全部失败、AI 全部走直线，日志里却什么都不缺。
                m_Session?.Log.Error(
                    "[服务器] 导航网格为空：AI 将退化为直线移动。"
                    + "通常是地图里没有任何碰撞体，或 NavMeshSurface 的收集方式配置有误。");
                return;
            }

            ReportNavigationProbes();
        }

        /// <summary>统计当前导航网格的顶点数，作为"网格非空"的判据。</summary>
        private static int CountNavMeshVertices()
        {
            var triangulation = NavMesh.CalculateTriangulation();
            return triangulation.vertices != null ? triangulation.vertices.Length : 0;
        }

        /// <summary>
        /// 用服务器将要使用的寻路实现算几条真实路径，把结果写进日志。
        /// </summary>
        /// <remarks>
        /// 走的是 <see cref="IPathfindingService"/> 而不是直接调 <c>NavMesh</c>：
        /// 这样验证的就不只是"引擎能寻路"，而是"AI 拿到的那条路径确实是通的"。
        /// </remarks>
        private void ReportNavigationProbes()
        {
            var waypoints = new List<Vector2F>();

            for (var i = 0; i < s_NavigationProbes.Length; i++)
            {
                var probe = s_NavigationProbes[i];
                var found = m_Pathfinding.TryFindPath(probe.From, probe.To, waypoints);
                m_Session?.Log.Info(
                    found
                        ? $"[服务器] 导航自检「{probe.Name}」：路径可用，{waypoints.Count} 个途径点。"
                        : $"[服务器] 导航自检「{probe.Name}」：**寻路失败**（该区域不可达或不在导航网格上）。");
            }
        }
    }
}
