using System;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 客户端对账时"多大的位置误差才算错"以及"怎么改"的两条判定。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不能用一个固定米数：</b>一步（1/60 秒）的位移随速度变化。
    /// 走路 3.5 m/s 时一步只有 0.058 米，冲刺 6.5 m/s 时一步是 0.108 米。
    /// 旧实现写死 0.08 米：比走路一步大、比冲刺一步小，于是冲刺时"每一步的时序抖动"
    /// 都会被判成错误，客户端每次快照都硬吸附一次——玩家看到的是一卡一卡、短距离瞬移
    /// （2026-09-14 实测：房主客户端 66 次回滚里 61 次误差正好 0.108 米）。</para>
    ///
    /// <para><b>容差按当前速度的若干步给</b>之后，一步级的时序抖动落在容差内、不再触发修正；
    /// 真正的偏差（被墙挡住、被队友挤开、传送）依旧远超容差。</para>
    ///
    /// <para><b>为什么还要分"吸收"和"瞬移"两种改法：</b>修正必须立刻写进模拟状态，
    /// 否则下一步的预测起点就是错的；但**表现层不必跟着瞬移**：
    /// 小误差交给模型自身的跟随（几帧内平滑贴上），只有大误差与传送才值得一次硬瞬移，
    /// 否则模型会"飞"过一段距离，比抖动更难看。</para>
    ///
    /// <para>本类不依赖 UnityEngine，可在 EditMode 测试里完整覆盖。</para>
    /// </remarks>
    public static class MovementReconciliationTuning
    {
        /// <summary>容差相当于当前速度的多少步。1.5 步足以吃掉时序抖动，又抓得住真偏差。</summary>
        public const float ToleranceSteps = 1.5f;

        /// <summary>容差下限（米）。站着不动时速度接近 0，没有下限会让容差退化成 0。</summary>
        /// <remarks>0.12 米约等于走路两步：静止与慢走时不会因为毫米级差异反复回滚。</remarks>
        public const float MinimumToleranceMeters = 0.12f;

        /// <summary>超过这个误差就直接瞬移（米）：典型来源是传送、重开与卡墙。</summary>
        public const float HardSnapMeters = 0.6f;

        /// <summary>
        /// 按当前速度算出可接受的平面误差。
        /// </summary>
        /// <param name="speedMetersPerSecond">本帧实际速度（<see cref="PlayerMoveState.CurrentSpeed"/>）。</param>
        /// <param name="stepSeconds">固定步长（秒），客户端是 1/60。</param>
        /// <returns>容差（米）。</returns>
        public static float ToleranceFor(float speedMetersPerSecond, float stepSeconds)
        {
            if (speedMetersPerSecond < 0f || stepSeconds <= 0f)
            {
                return MinimumToleranceMeters;
            }

            var oneStep = speedMetersPerSecond * stepSeconds;
            return Math.Max(MinimumToleranceMeters, oneStep * ToleranceSteps);
        }

        /// <summary>
        /// 这次修正要不要直接瞬移（而不是让模型自己平滑贴上去）。
        /// </summary>
        /// <param name="positionError">对账得到的平面误差（米）。</param>
        public static bool RequiresHardSnap(float positionError)
        {
            return positionError >= HardSnapMeters;
        }
    }
}
