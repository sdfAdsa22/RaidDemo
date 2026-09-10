using System;

namespace RaidDemo.Kernel
{
    /// <summary>
    /// 时间服务接口。
    /// </summary>
    /// <remarks>
    /// <para>业务代码不直接读取 Time.deltaTime，而是通过本接口间接获取，原因有两个：</para>
    /// <list type="number">
    /// <item><description>单元测试可以注入假时钟并手动推进时间。例如验证巡逻 AI 是否在 5 秒后转入搜索状态，
    /// 测试只需把时钟推进 5 秒，而不必真的等待 5 秒或启动游戏。</description></item>
    /// <item><description>联机时由服务端以固定步长推进时间，客户端使用本地时间做表现。
    /// 两端共用同一套时间接口，业务代码无需区分自己运行在哪一侧。</description></item>
    /// </list>
    /// </remarks>
    public interface ITimeService
    {
        /// <summary>上一帧到当前帧的时间增量（秒），受时间缩放影响。</summary>
        float DeltaTime { get; }

        /// <summary>不受时间缩放影响的时间增量（秒）。用于暂停菜单等不应被冻结的动画。</summary>
        float UnscaledDeltaTime { get; }

        /// <summary>固定步长的时间增量（秒）。物理与确定性逻辑应使用它。</summary>
        float FixedDeltaTime { get; }

        /// <summary>自游戏开始以来累计的游戏时间（秒），受时间缩放影响。</summary>
        double GameTime { get; }

        /// <summary>自游戏开始以来累计的真实时间（秒），不受时间缩放影响。</summary>
        double RealTime { get; }

        /// <summary>时间缩放倍率。1 为正常速度，0 为完全暂停。</summary>
        float TimeScale { get; set; }

        /// <summary>当前是否处于暂停状态。</summary>
        bool IsPaused { get; }
    }

    /// <summary>
    /// 基于 Unity 时间系统的时间服务实现。
    /// </summary>
    /// <remarks>
    /// 本类不缓存 Time.deltaTime：Unity 会在每帧更新这些值，缓存反而需要额外的同步逻辑，
    /// 而直接读取的开销可以忽略。
    /// </remarks>
    public sealed class UnityTimeService : ITimeService
    {
        /// <summary>
        /// 允许的时间缩放上限。
        /// 限制它是因为过大的缩放会让单个 deltaTime 超过物理系统的穿透阈值，
        /// 导致快速移动的物体穿过墙体。3 倍是调试加速的常用上限。
        /// </summary>
        private const float MaxTimeScale = 3f;

        private float m_TimeScale = 1f;

        /// <inheritdoc />
        public float DeltaTime => UnityEngine.Time.deltaTime;

        /// <inheritdoc />
        public float UnscaledDeltaTime => UnityEngine.Time.unscaledDeltaTime;

        /// <inheritdoc />
        public float FixedDeltaTime => UnityEngine.Time.fixedDeltaTime;

        /// <inheritdoc />
        public double GameTime => UnityEngine.Time.timeAsDouble;

        /// <inheritdoc />
        public double RealTime => UnityEngine.Time.realtimeSinceStartupAsDouble;

        /// <inheritdoc />
        public float TimeScale
        {
            get => m_TimeScale;
            set
            {
                // 负值会让时间倒流，在含状态机的逻辑中会产生难以排查的异常行为，因此直接归零。
                m_TimeScale = Math.Clamp(value, 0f, MaxTimeScale);
                UnityEngine.Time.timeScale = m_TimeScale;
            }
        }

        /// <inheritdoc />
        public bool IsPaused => m_TimeScale <= 0f;

        /// <summary>暂停游戏（等价于把时间缩放设为 0）。</summary>
        public void Pause()
        {
            TimeScale = 0f;
        }

        /// <summary>恢复游戏到正常速度。</summary>
        public void Resume()
        {
            TimeScale = 1f;
        }
    }

    /// <summary>
    /// 可手动推进的时钟，用于单元测试与服务端固定步长驱动。
    /// </summary>
    /// <remarks>
    /// <para>在测试中的典型用法：创建时钟，然后反复调用 <see cref="Advance"/> 推进时间，
    /// 断言被测逻辑在预期时刻发生了状态变化。</para>
    ///
    /// <para>在服务端的用法：服务端不依赖真实帧率，而是按固定的 Tick 间隔调用 Advance，
    /// 从而保证不同机器上的模拟结果一致。</para>
    /// </remarks>
    public sealed class ManualTimeService : ITimeService
    {
        private float m_TimeScale = 1f;

        /// <summary>
        /// 创建手动时钟。
        /// </summary>
        /// <param name="step">
        /// 每次 <see cref="Advance"/> 推进的秒数。默认 1/60 秒，与服务端目标 Tick 频率一致。
        /// </param>
        public ManualTimeService(float step = 1f / 60f)
        {
            if (step <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(step), step, "步长必须大于 0。");
            }

            Step = step;
            FixedDeltaTime = step;
        }

        /// <summary>每次推进的固定步长（秒）。</summary>
        public float Step { get; }

        /// <inheritdoc />
        public float DeltaTime { get; private set; }

        /// <inheritdoc />
        public float UnscaledDeltaTime => DeltaTime;

        /// <inheritdoc />
        public float FixedDeltaTime { get; }

        /// <inheritdoc />
        public double GameTime { get; private set; }

        /// <inheritdoc />
        public double RealTime { get; private set; }

        /// <inheritdoc />
        public float TimeScale
        {
            get => m_TimeScale;
            set => m_TimeScale = Math.Clamp(value, 0f, 1f);
        }

        /// <inheritdoc />
        public bool IsPaused => m_TimeScale <= 0f;

        /// <summary>已推进的总帧数。</summary>
        public int FrameCount { get; private set; }

        /// <summary>推进一帧。暂停状态下帧计数仍会增加，但游戏时间不变。</summary>
        public void Advance()
        {
            FrameCount++;
            var scaled = Step * m_TimeScale;
            DeltaTime = scaled;
            GameTime += scaled;
            RealTime += Step;
        }

        /// <summary>连续推进若干帧。</summary>
        /// <param name="frames">帧数。</param>
        public void Advance(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Advance();
            }
        }

        /// <summary>推进指定的游戏时间，自动换算为帧数（不足一帧时按一帧处理）。</summary>
        /// <param name="seconds">要推进的秒数。</param>
        public void AdvanceSeconds(float seconds)
        {
            var frames = (int)(seconds / Step);
            Advance(frames > 0 ? frames : 1);
        }
    }
}
