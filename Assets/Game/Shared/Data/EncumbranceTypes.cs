using System;

namespace RaidDemo.Shared
{
    /// <summary>
    /// 负重状态。由"当前总负重 ÷ 承载上限"落在哪个区间决定。
    /// </summary>
    /// <remarks>
    /// <para>放在共享层而不是背包层，是因为它要被两个方向同时使用：
    /// 背包层负责**判定**（我现在算轻装还是超重），移动层负责**执行**（所以速度与体力怎么变）。
    /// 联机时服务端与客户端读的是同一份定义。</para>
    ///
    /// <para>三段式而不是"超重/未超重"两段式：若只在 100% 处砍一刀，
    /// 玩家在 99% 与 101% 之间的体验会断裂，感觉像踩到陷阱而不是自己做错了选择。
    /// 70% 处先拿走奔跑能力，是在装包阶段就给玩家一个预警信号。</para>
    /// </remarks>
    public enum EncumbranceState
    {
        /// <summary>轻装。负重比低于 0.70，行动完全不受影响。</summary>
        Light = 0,

        /// <summary>重装。负重比在 0.70 到 1.00 之间：不能奔跑，体力恢复减半。</summary>
        Heavy,

        /// <summary>超重。负重比超过 1.00：不能奔跑、体力不恢复，且越重走得越慢。</summary>
        Overloaded,
    }

    /// <summary>
    /// 一组作用于移动配置的修正倍率。
    /// </summary>
    /// <remarks>
    /// <para>这是背包系统与移动系统之间的**唯一数据接口**。背包层只产出这组倍率，
    /// 移动层只消费它，两边都不知道对方的存在，装配代码在启动层把它们接起来。
    /// 好处是移动逻辑可以完全脱离背包测试，背包逻辑也可以完全脱离移动测试。</para>
    ///
    /// <para>做成只读结构体而不是可变类：它每帧都会被重算，用结构体避免堆分配。</para>
    /// </remarks>
    public readonly struct MovementModifiers
    {
        /// <summary>创建一组移动修正。</summary>
        /// <param name="speedMultiplier">速度倍率。负值按 0 处理。</param>
        /// <param name="staminaRegenMultiplier">体力恢复倍率，0 表示完全不恢复。负值按 0 处理。</param>
        /// <param name="canSprint">是否允许奔跑。为 false 时模拟层直接忽略奔跑意图。</param>
        public MovementModifiers(float speedMultiplier, float staminaRegenMultiplier, bool canSprint)
        {
            // 负数在这里没有物理意义，且会让模拟层出现"倒退"这种荒唐结果。
            // 在数据边界上兜底比在模拟层里到处防御要便宜得多。
            SpeedMultiplier = speedMultiplier < 0f ? 0f : speedMultiplier;
            StaminaRegenMultiplier = staminaRegenMultiplier < 0f ? 0f : staminaRegenMultiplier;
            CanSprint = canSprint;
        }

        /// <summary>速度倍率，乘进步行与奔跑速度。</summary>
        public float SpeedMultiplier { get; }

        /// <summary>体力恢复倍率，0 表示完全不恢复。</summary>
        public float StaminaRegenMultiplier { get; }

        /// <summary>是否允许奔跑。</summary>
        public bool CanSprint { get; }

        /// <summary>无修正：速度与体力恢复均为 1.0 倍，允许奔跑。</summary>
        public static MovementModifiers Default
        {
            get { return new MovementModifiers(1f, 1f, true); }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "MovementModifiers(speed={0:F2}, regen={1:F2}, sprint={2})",
                SpeedMultiplier,
                StaminaRegenMultiplier,
                CanSprint);
        }
    }
}
