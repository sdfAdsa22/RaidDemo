using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 地面高度探测：给一个平面位置，返回脚下可站立面的高度。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么问题：</b>模拟层只有平面坐标（XZ），而"这个位置的地面有多高"是场景信息。
    /// 客户端由 <see cref="PlayerMotor"/> 在移动时自己探测；服务器没有表现层的马达，
    /// 但它的碰撞胶囊与子弹起点同样需要用**真实地面高度**，否则整套权威判定会悬在半空。</para>
    ///
    /// <para><b>为什么服务器不能沿用"地面高度 = 0"：</b>M7 起地图是下沉盆地，
    /// 谷底在 -6 米、装卸平台在 -4.8 米、塬面在 0 米。
    /// 服务器若把胶囊放在 0 米，它的碰撞与射线就整体浮在谷底上方 6 米：
    /// 玩家会"穿过"集装箱（服务器认为那里没有障碍）、子弹会从掩体上方飞过，
    /// 而客户端的画面一切正常——两端只在命中结果上对不上，极难定位。</para>
    ///
    /// <para><b>与 <see cref="PlayerMotor"/> 的采样规则保持一致：</b>同样的遮罩
    /// （<see cref="PhysicsLayers.GroundProbeMask"/>，排除单位层）、同样的"朝上才算地面"判据、
    /// 同样的抬升上限。区别只在于本类是**无状态的一次性采样**：服务器每帧都从当前权威位置重新采样，
    /// 不需要像马达那样维护"上次站多高"的增量状态。</para>
    /// </remarks>
    public static class GroundProbe
    {
        /// <summary>探针起点相对参考高度的抬升量（米）。与玩家马达的取值一致。</summary>
        public const float ProbeHeight = 8f;

        /// <summary>允许单次抬升的最大高度（米）。比台阶高一点，但拦得住"突然被抬到平台上"。</summary>
        public const float MaxStepUpHeight = 0.35f;

        /// <summary>可站立面的法线判据：竖直墙面被射线擦到时不算地面。</summary>
        private const float MinimumStandableNormalY = 0.5f;

        /// <summary>
        /// 采样某个平面位置的地面高度。
        /// </summary>
        /// <param name="plane">平面位置（世界 XZ）。</param>
        /// <param name="referenceHeight">探针参考高度：通常是单位当前位置的高度。</param>
        /// <param name="lastGroundHeight">上一次的采样结果；本次没命中时沿用它，避免单位掉进没有碰撞体的缝隙。</param>
        /// <param name="self">单位自己的根节点，用于排除自身的碰撞体；没有可传 null。</param>
        /// <returns>地面高度（米）。</returns>
        public static float SampleGroundHeight(
            Vector2F plane,
            float referenceHeight,
            float lastGroundHeight,
            Transform self = null)
        {
            var origin = new Vector3(
                plane.X,
                Mathf.Max(referenceHeight, lastGroundHeight) + ProbeHeight,
                plane.Y);

            if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var hit,
                    ProbeHeight * 2f,
                    PhysicsLayers.GroundProbeMask,
                    QueryTriggerInteraction.Ignore))
            {
                return lastGroundHeight;
            }

            if (self != null && (hit.collider.transform == self || hit.collider.transform.IsChildOf(self)))
            {
                return lastGroundHeight;
            }

            // 只接受朝上的可站立面，且不允许"一步登天"：坡道与台面衔接处会同时命中两个面，
            // 向下射线取的是较高的那个——这正是马达里"取最高命中点"的效果。
            if (hit.normal.y < MinimumStandableNormalY
                || hit.point.y > lastGroundHeight + MaxStepUpHeight)
            {
                return lastGroundHeight;
            }

            return hit.point.y;
        }
    }
}
