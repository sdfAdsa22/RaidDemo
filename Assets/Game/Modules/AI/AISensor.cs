using RaidDemo.Combat;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.AI
{
    /// <summary>
    /// 感知判定的纯函数集合：遮挡与听觉范围。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么复用战斗层的 <see cref="IHitProbe"/> 而不是新开一个接口：</b>
    /// "从 A 点向 B 点打一条射线看看中间有没有东西"是这个项目里唯一需要的空间查询，
    /// 射击与视线用的是同一种能力。再定义一套 <c>ISightProbe</c> 只会让表现层
    /// 维护两份几乎相同的实现，并且迟早出现"射击能穿透、视线被挡住"这种不一致。</para>
    ///
    /// <para>本类不持有任何状态，因此可以逐个函数单独测试。</para>
    /// </remarks>
    public static class AISensor
    {
        /// <summary>
        /// 判断从眼睛到目标中心的连线是否畅通。
        /// </summary>
        /// <param name="probe">射线能力，可为 null（视为完全看不见）。</param>
        /// <param name="eyePosition">观察者眼睛的世界坐标。</param>
        /// <param name="targetCenter">目标中心的世界坐标。</param>
        /// <param name="targetId">目标在战斗层中的单位标识。</param>
        /// <param name="maxDistance">视距上限（米）。</param>
        /// <returns>第一个被击中的物体就是目标时返回 true。</returns>
        /// <remarks>
        /// <para><b>判定是"第一个命中的就是目标"，而不是"射线打到了目标"。</b>
        /// 后者会让掩体后面的 AI 隔着墙看见玩家：射线穿过墙继续前进，仍然能打到玩家。</para>
        /// <para><b>射线什么也没打到时返回 false。</b>理论上目标自带碰撞体，打不到说明
        /// 场景装配有问题。此时保守地当作"看不见"：看不见顶多让 AI 迟钝一点，
        /// 而误判成看得见会让掩体形同虚设。</para>
        /// </remarks>
        public static bool HasLineOfSight(
            IHitProbe probe,
            Vector3 eyePosition,
            Vector3 targetCenter,
            int targetId,
            float maxDistance)
        {
            if (probe == null || targetId == 0 || maxDistance <= 0f)
            {
                return false;
            }

            var toTarget = targetCenter - eyePosition;
            var distance = toTarget.magnitude;
            if (distance > maxDistance || distance < 1e-4f)
            {
                // 距离下限为 1e-4：与目标中心完全重合时方向无法归一化，
                // 这种情况只可能出现在调试传送，当成"看不见"处理更安全。
                return false;
            }

            var direction = toTarget / distance;
            if (!probe.TryRaycast(eyePosition, direction, distance, out var hit))
            {
                return false;
            }

            return hit.TargetId == targetId;
        }

        /// <summary>
        /// 判断某个噪音是否落在可听范围内。
        /// </summary>
        /// <param name="listenerPosition">听者位置。</param>
        /// <param name="noisePosition">噪音位置。</param>
        /// <param name="radiusMeters">这次噪音的可听半径（米），取自 <see cref="NoiseEvent.RadiusMeters"/>。</param>
        /// <remarks>声音不做遮挡判定，理由见 <see cref="NoiseEvent"/> 的注释。</remarks>
        public static bool CanHear(
            Vector2F listenerPosition,
            Vector2F noisePosition,
            float radiusMeters)
        {
            if (radiusMeters <= 0f)
            {
                return false;
            }

            return Vector2F.SqrDistance(listenerPosition, noisePosition) <= radiusMeters * radiusMeters;
        }

        /// <summary>由平面位置与眼高计算眼睛的世界坐标。</summary>
        /// <param name="position">平面位置。</param>
        /// <param name="eyeHeight">眼高（米）。</param>
        public static Vector3 ToEyePosition(Vector2F position, float eyeHeight)
        {
            return ToEyePosition(position, eyeHeight, 0f);
        }

        /// <summary>由平面位置、眼高与脚下地面高度计算眼睛的世界坐标。</summary>
        /// <param name="position">平面位置。</param>
        /// <param name="eyeHeight">眼高（米）。</param>
        /// <param name="groundHeight">脚下的地面高度（米），由场景侧的高度采样提供。</param>
        public static Vector3 ToEyePosition(Vector2F position, float eyeHeight, float groundHeight)
        {
            return new Vector3(position.X, groundHeight + eyeHeight, position.Y);
        }

        /// <summary>把世界坐标沿 Y 轴抬起指定高度（用于目标中心跟随其脚下的地面高度）。</summary>
        public static Vector3 LiftByHeight(Vector3 position, float height)
        {
            return new Vector3(position.x, position.y + height, position.z);
        }

    }
}
