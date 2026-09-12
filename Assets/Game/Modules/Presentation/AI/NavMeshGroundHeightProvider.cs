using RaidDemo.AI;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 基于导航网格的地面高度采样：与 <see cref="EnemyAgentView"/> 落地用的是同一套数据。
    /// </summary>
    /// <remarks>
    /// <para><b>搜索半径必须覆盖地图的最大高差加余量。</b>采样点固定取 y=0，
    /// 半径给小了就采不到谷底附近的网格，采样失败会返回 0，单位于是被摆到 y=0 的空中
    /// ——而"浮空"与"站在高处"在俯视角下几乎看不出区别，属于会静默出错的一类问题。</para>
    ///
    /// <para>当前地形是下沉盆地：谷底 y=-6 米、外围等高线 y=0，最大高差 6 米；
    /// 半径取 12 米 = 6 米高差 + 6 米余量。以后把地形做得更深，这个值必须同步放大。</para>
    ///
    /// <para>采不到（例如单位被挤到网格外）时返回 0，退化成灰盒平地的行为；
    /// 这个回退是刻意保留的，让没有导航网格的旧场景仍然能跑起来。</para>
    /// </remarks>
    public sealed class NavMeshGroundHeightProvider : IGroundHeightProvider
    {
        /// <summary>
        /// 采样搜索半径（米）。必须 ≥ 地图最大高差 + 余量，
        /// 且必须与 <see cref="EnemyAgentView"/> 里的同名常量保持一致：
        /// 两处一个用于 AI 判定与调试图形、一个用于单位落地，取不同值会出现
        /// "调试视角锥站在地上、敌人却浮在空中"这种自相矛盾的现象。
        /// </summary>
        private const float SampleRadiusMeters = 12f;

        public float SampleHeight(Vector2F position)
        {
            var probe = new Vector3(position.X, 0f, position.Y);
            return NavMesh.SamplePosition(probe, out var hit, SampleRadiusMeters, NavMesh.AllAreas)
                ? hit.position.y
                : 0f;
        }
    }
}
