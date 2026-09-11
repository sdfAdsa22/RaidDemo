using System;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 角度运算的小工具：转向、夹角、容差判定。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须处理"绕圈"：</b>角度是环形的，350 度与 10 度之间只差 20 度，
    /// 但直接相减会得到 340 度。用错一次的症状是"AI 在接近正前方时突然转一大圈"
    /// 或者"瞄准判定永远不成立"——两种都很难从现象反推到角度处理上。</para>
    ///
    /// <para>本类只做数学，不持有状态，因此可以逐条测试。</para>
    /// </remarks>
    public static class AiAngles
    {
        /// <summary>把方向向量转成角度（度）。零向量返回 0。</summary>
        public static float ToDegrees(Vector2F direction)
        {
            return direction.IsNearlyZero ? 0f : direction.ToDegrees();
        }

        /// <summary>
        /// 返回从 <paramref name="fromDegrees"/> 转到 <paramref name="toDegrees"/> 的最短有向差值。
        /// </summary>
        /// <returns>结果落在 [-180, 180] 区间内。</returns>
        public static float DeltaDegrees(float fromDegrees, float toDegrees)
        {
            var delta = (toDegrees - fromDegrees) % 360f;
            if (delta > 180f)
            {
                delta -= 360f;
            }
            else if (delta < -180f)
            {
                delta += 360f;
            }

            return delta;
        }

        /// <summary>
        /// 以不超过 <paramref name="maxDeltaDegrees"/> 的步长把朝向转向目标。
        /// </summary>
        /// <param name="currentDegrees">当前朝向（度）。</param>
        /// <param name="targetDegrees">目标朝向（度）。</param>
        /// <param name="maxDeltaDegrees">本次允许转过的最大角度（度）。</param>
        public static float StepTowards(float currentDegrees, float targetDegrees, float maxDeltaDegrees)
        {
            if (maxDeltaDegrees <= 0f)
            {
                return currentDegrees;
            }

            var delta = DeltaDegrees(currentDegrees, targetDegrees);
            if (delta > maxDeltaDegrees)
            {
                delta = maxDeltaDegrees;
            }
            else if (delta < -maxDeltaDegrees)
            {
                delta = -maxDeltaDegrees;
            }

            return Normalize(currentDegrees + delta);
        }

        /// <summary>两个朝向之间的夹角是否在容差之内（忽略方向，取绝对值）。</summary>
        public static bool IsWithinTolerance(float currentDegrees, float targetDegrees, float toleranceDegrees)
        {
            return Math.Abs(DeltaDegrees(currentDegrees, targetDegrees)) <= toleranceDegrees;
        }

        /// <summary>把角度规范到 [0, 360) 区间。</summary>
        public static float Normalize(float degrees)
        {
            var normalized = degrees % 360f;
            if (normalized < 0f)
            {
                normalized += 360f;
            }

            return normalized;
        }
    }
}
