using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 移动动画的播放速率换算：把"实际移动速度"翻译成"动画应以多少倍速播放"。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它（A-02）：</b>动画剪辑的设计速度是固定的——一个循环对应固定步频。
    /// 实际速度高于设计速度时脚会打滑，低于时像在原地迈碎步。本项目的速度不是常量：
    /// 玩家受负重修正（超载时最低只有 0.4 倍），敌人有巡逻 2 / 警戒 2.5 / 调查 3 /
    /// 交战 3.2 / 撤退 4.2 五档。播放倍率必须跟着速度连续变化，否则只能靠
    /// "把移动速度调到与动画一致"来掩盖问题，而那会反过来限制玩法调参。</para>
    ///
    /// <para><b>设计速度怎么来的：</b>一个走路或跑步循环迈两步，步幅与脚步音效共用
    /// <see cref="FootstepCadence"/> 的常量（走路 0.95 米、奔跑 1.55 米），因此
    /// <c>设计速度 = 2 × 步幅 ÷ 剪辑长度</c>。剪辑长度在构建角色资产时读取，
    /// 算好的设计速度写进 <see cref="LocomotionAnimationBinding"/>，运行时只做一次除法，
    /// 不需要每帧去查动画控制器。</para>
    ///
    /// <para><b>为什么限幅：</b>下限防止超载时慢成定格，上限防止速度异常
    /// （被击退、传送、位置修正）时动作快进到看不清。速度低于站定阈值时直接返回 1，
    /// 让状态机自己切回待机，不产生"慢动作收尾"。</para>
    /// </remarks>
    public static class LocomotionAnimationRate
    {
        /// <summary>播放倍率下限。</summary>
        public const float MinRate = 0.5f;

        /// <summary>播放倍率上限。</summary>
        public const float MaxRate = 2f;

        /// <summary>
        /// 低于该速度视为站定（米/秒）。与动画控制器里 Idle↔Walk 的切换阈值保持一致：
        /// 状态已经要切回待机时，再让走路剪辑以 0.1 倍速播放只会看起来像时间变慢。
        /// </summary>
        public const float IdleSpeedThreshold = 0.2f;

        /// <summary>一个动画循环包含的步数：左脚一步 + 右脚一步。</summary>
        public const int StepsPerCycle = 2;

        /// <summary>
        /// 由剪辑长度与单步步幅推算设计速度（米/秒）。
        /// </summary>
        /// <param name="clip">移动状态的循环剪辑。</param>
        /// <param name="strideMeters">单步步幅（米）；与脚步节拍器共用同一份常量。</param>
        /// <returns>设计速度；参数非法时返回 0，调用方据此退回"不缩放"。</returns>
        public static float DesignSpeed(AnimationClip clip, float strideMeters)
        {
            if (clip == null || clip.length <= 0.0001f || strideMeters <= 0.0001f)
            {
                return 0f;
            }

            return StepsPerCycle * strideMeters / clip.length;
        }

        /// <summary>
        /// 计算移动动画的播放倍率。
        /// </summary>
        /// <param name="speedMetersPerSecond">当前实际移动速度（米/秒）。</param>
        /// <param name="designSpeedMetersPerSecond">当前状态剪辑的设计速度（米/秒）。</param>
        /// <returns>
        /// 播放倍率。速度为站定或设计速度不可用时返回 1（不缩放），
        /// 这样即使忘记给预制体写参数，表现也与 A-02 之前一致，不会更糟。
        /// </returns>
        public static float Calculate(float speedMetersPerSecond, float designSpeedMetersPerSecond)
        {
            if (speedMetersPerSecond < IdleSpeedThreshold || designSpeedMetersPerSecond <= 0.0001f)
            {
                return 1f;
            }

            return Mathf.Clamp(speedMetersPerSecond / designSpeedMetersPerSecond, MinRate, MaxRate);
        }
    }
}
