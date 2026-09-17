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

        /// <summary>
        /// 与 <see cref="TryRaycast"/> 相同，但把某个受击目标当作"透明"：命中它时继续往后投射。
        /// </summary>
        /// <param name="origin">射线起点（世界坐标）。</param>
        /// <param name="direction">射线方向，不必归一化。</param>
        /// <param name="maxDistance">最大距离（米）。</param>
        /// <param name="ignoredTargetId">
        /// 要跳过的受击目标标识（通常是射手自己的战斗单位编号）；0 表示不跳过任何目标。
        /// </param>
        /// <param name="hit">最近的有效命中结果。</param>
        /// <returns>跳过被忽略目标后仍打到任何碰撞体时返回 true。</returns>
        /// <remarks>
        /// <para><b>为什么需要它：</b>枪口在身体外侧（身前 0.6 米＋枪管长度），当瞄准点落在
        /// 角色附近、尤其侧后方时，"从枪口指向瞄准点"的射线会**穿过自己的身体**。
        /// 物理上命中自己是正确的，玩法上却是一个错误：子弹会停在自己身上，打不到身后的目标
        /// （负责人反馈的"准星靠近玩家时好像打到了自己"）。</para>
        /// <para>把这条语义放在探针层而不是武器层：探针掌握射线的距离与命中顺序，
        /// 能"跳过自己继续投"；武器层只该收到一条有效弹道。</para>
        /// </remarks>
        bool TryRaycastIgnoringTarget(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            int ignoredTargetId,
            out HitInfo hit);
    }
}
