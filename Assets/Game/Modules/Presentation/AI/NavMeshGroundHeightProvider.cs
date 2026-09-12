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
    /// <para>真实的采样规则在 <see cref="NavMeshGroundSampler"/> 里：它按「从下往上一层层试」的方式取
    /// 该平面位置**最低的那层**可行走面，因此不会把站在谷底的单位吸附到旁边的平台或坡道上。</para>
    ///
    /// <para>本类只保留 AI 逻辑层需要的契约：采不到（例如单位被挤到网格外、或场景里根本没有导航网格）时
    /// 返回 0，退化成灰盒平地的行为——这个回退让无导航网格的旧场景仍然能跑起来。</para>
    /// </remarks>
    public sealed class NavMeshGroundHeightProvider : IGroundHeightProvider
    {
        public float SampleHeight(Vector2F position)
        {
            return NavMeshGroundSampler.TrySample(position, out var height) ? height : 0f;
        }
    }
}
