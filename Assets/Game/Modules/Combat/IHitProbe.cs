using UnityEngine;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 一次射线检测的命中结果。
    /// </summary>
    /// <remarks>
    /// <see cref="TargetCenter"/> 是暴击判定需要的唯一额外信息：
    /// 只报告"打中了"不足以判断打在哪，还需要知道目标中心在哪才能算出偏移。
    /// </remarks>
    public readonly struct HitInfo
    {
        /// <summary>创建命中结果。</summary>
        /// <param name="targetId">命中的可受击目标标识，0 表示只打中了环境。</param>
        /// <param name="point">命中点世界坐标。</param>
        /// <param name="targetCenter">目标中心的世界坐标；没有目标时为零向量。</param>
        /// <param name="distance">沿射线的距离（米）。</param>
        public HitInfo(int targetId, Vector3 point, Vector3 targetCenter, float distance)
        {
            TargetId = targetId;
            Point = point;
            TargetCenter = targetCenter;
            Distance = distance;
        }

        /// <summary>命中的可受击目标标识，0 表示只打中了环境。</summary>
        public int TargetId { get; }

        /// <summary>命中点世界坐标。</summary>
        public Vector3 Point { get; }

        /// <summary>目标中心的世界坐标；没有目标时为零向量。</summary>
        public Vector3 TargetCenter { get; }

        /// <summary>沿射线的距离（米）。</summary>
        public float Distance { get; }

        /// <summary>是否命中了可受击目标。</summary>
        public bool HasTarget
        {
            get { return TargetId != 0; }
        }
    }

    /// <summary>
    /// 射线检测能力。
    /// </summary>
    /// <remarks>
    /// <para>这是战斗层与引擎之间**唯一的**接触面（主文档 5.6 节约束三：引擎能力通过接口注入）。</para>
    /// <para>把射线收敛到一个接口，换来的是战斗规则可以完全脱离场景测试：
    /// 测试能精确指定"打中了哪个目标、打在多高、距离多远"，
    /// 而这些条件在真实场景里几乎无法稳定构造。</para>
    /// <para>真实实现是表现层的物理射线；测试实现返回预先编排好的命中结果。</para>
    /// </remarks>
    public interface IHitProbe
    {
        /// <summary>
        /// 从起点沿方向投射一条射线。
        /// </summary>
        /// <param name="origin">射线起点（世界坐标）。</param>
        /// <param name="direction">射线方向，不必归一化。</param>
        /// <param name="maxDistance">最大距离（米）。</param>
        /// <param name="hit">命中结果。</param>
        /// <returns>射线打到任何碰撞体时返回 true。</returns>
        bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit);
    }
}
