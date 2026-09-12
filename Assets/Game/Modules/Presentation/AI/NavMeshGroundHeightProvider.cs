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
    /// 采样点取 y=0、搜索半径 4 米，足以覆盖装卸平台（1.2 米）这类高差；
    /// 采不到（例如单位被挤到网格外）时返回 0，退化成灰盒地面的行为。
    /// </remarks>
    public sealed class NavMeshGroundHeightProvider : IGroundHeightProvider
    {
        public float SampleHeight(Vector2F position)
        {
            var probe = new Vector3(position.X, 0f, position.Y);
            return NavMesh.SamplePosition(probe, out var hit, 4f, NavMesh.AllAreas)
                ? hit.position.y
                : 0f;
        }
    }
}
