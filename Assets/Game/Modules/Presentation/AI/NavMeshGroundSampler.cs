using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 导航网格地面采样：取某个平面位置上**最低的那一层**可行走面。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不能直接用一次大半径 <c>SamplePosition</c>：</b>它返回的是「离采样点最近的面」，
    /// 而采样点的高度是猜的。在有多层地面的地图上（本项目：谷底 -6、装卸平台 -4.8、坡道一路到塬面 0），
    /// 只要单位靠近平台或坡道，平台/坡道的面就会比脚下那层更靠近采样点，
    /// 于是单位被瞬间抬到平台上——表现为「小兵走到箱子旁边就瞬移到上面」。</para>
    ///
    /// <para><b>改成从下往上一层层试：</b>同一条垂线上如果有多层导航面，先命中的那层一定是更低的那层，
    /// 也就是单位真正站着的那层（高处那层不可能悬在低处那层的正上方——本项目没有天桥式结构）。
    /// 每一层用一个很小的搜索半径（1 米）并只探该层附近，因此不会再跨层吸附。</para>
    ///
    /// <para><b>层高表必须跟着地形改：</b>表中的高度是本项目地图的层高（谷底 -6 到塬面 +6，间隔 1.5 米）。
    /// 如果将来把盆地做深或加/减平台，这张表必须同步更新，否则会退化成「找不到地面」。
    /// 间隔取 1.5 米是为了覆盖坡道上的任意高度：坡道是连续的，但采样点是固定的层高，
    /// 层与层之间留 1.5 米时，坡面上任意一点离最近的一层都不会超过 0.75 米，远小于 1 米的搜索半径。</para>
    /// </remarks>
    public static class NavMeshGroundSampler
    {
        /// <summary>每一层地面的名义高度（米），从低到高。</summary>
        private static readonly float[] LayerHeights =
        {
            -6f, -4.5f, -3f, -1.5f, 0f, 1.5f, 3f, 4.5f, 6f
        };

        /// <summary>单层搜索半径（米）。只比层间距的一半大一点，避免跨层吸附。</summary>
        private const float LayerProbeRadius = 1f;

        /// <summary>兜底搜索半径（米）。所有层都探不到时才会用到。</summary>
        private const float FallbackRadiusMeters = 12f;

        /// <summary>
        /// 采样该平面位置的地面高度。
        /// </summary>
        /// <param name="position">平面位置。</param>
        /// <param name="height">地面高度（米）；失败时为 0。</param>
        /// <returns>成功采样返回 true。</returns>
        public static bool TrySample(Vector2F position, out float height)
        {
            for (var i = 0; i < LayerHeights.Length; i++)
            {
                var probe = new Vector3(position.X, LayerHeights[i], position.Y);
                if (NavMesh.SamplePosition(probe, out var hit, LayerProbeRadius, NavMesh.AllAreas))
                {
                    height = hit.position.y;
                    return true;
                }
            }

            // 所有层都没命中：单位可能被挤出导航网格（例如被同伴顶到角落）。
            // 用一次大半径搜索兜底，好过直接判定"脚下没有地面"。
            if (NavMesh.SamplePosition(new Vector3(position.X, 0f, position.Y), out var fallback, FallbackRadiusMeters, NavMesh.AllAreas))
            {
                height = fallback.position.y;
                return true;
            }

            height = 0f;
            return false;
        }
    }
}
