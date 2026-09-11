using RaidDemo.AI;
using RaidDemo.Combat;
using RaidDemo.Shared;

namespace RaidDemo.Diagnostics
{
    /// <summary>
    /// AI 没发现目标的原因。
    /// </summary>
    /// <remarks>
    /// <para><b>只画一个视野锥是不够的。</b>扇形只能说明"它看不见"，
    /// 而调试时真正要回答的是**为什么**看不见：是离得太远、方向不对，还是被掩体挡住。
    /// 三者的修法完全不同（调视距 / 调视角 / 换位置或改掩体），
    /// 猜错的代价是反复进播放模式试。</para>
    /// </remarks>
    public enum AiDetectionFailure
    {
        /// <summary>看得见。</summary>
        Visible = 0,

        /// <summary>没有目标：玩家尚未登记，或已阵亡 / 撤离。</summary>
        NoTarget,

        /// <summary>超出视距。</summary>
        OutOfRange,

        /// <summary>在视野锥之外（方向不对）。</summary>
        OutsideViewCone,

        /// <summary>视线被挡住（或射线异常，见 <see cref="AiDetectionDiagnostics"/> 的说明）。</summary>
        Blocked,
    }

    /// <summary>
    /// 一次检测复核的结果。
    /// </summary>
    public readonly struct AiDetectionResult
    {
        /// <summary>创建检测结果。</summary>
        public AiDetectionResult(AiDetectionFailure failure, float distanceMeters, float angleDegrees)
        {
            Failure = failure;
            DistanceMeters = distanceMeters;
            AngleDegrees = angleDegrees;
        }

        /// <summary>失败原因（<see cref="AiDetectionFailure.Visible"/> 表示看得见）。</summary>
        public AiDetectionFailure Failure { get; }

        /// <summary>与目标的平面距离（米）。</summary>
        public float DistanceMeters { get; }

        /// <summary>目标相对朝向的夹角（度，取绝对值）。</summary>
        public float AngleDegrees { get; }

        /// <summary>是否看得见。</summary>
        public bool Sees
        {
            get { return Failure == AiDetectionFailure.Visible; }
        }

        /// <summary>中文短标签，直接显示在界面上。</summary>
        public string Describe()
        {
            switch (Failure)
            {
                case AiDetectionFailure.Visible:
                    return "可见";
                case AiDetectionFailure.NoTarget:
                    return "无目标（未登记或已阵亡）";
                case AiDetectionFailure.OutOfRange:
                    return "超距";
                case AiDetectionFailure.OutsideViewCone:
                    return "在视野锥外";
                default:
                    return "被掩体挡住";
            }
        }

        public override string ToString()
        {
            return $"{Describe()}（{DistanceMeters:F1} 米 / {AngleDegrees:F0}°）";
        }
    }

    /// <summary>
    /// 独立复核"这个 AI 能不能看见玩家"，并给出失败原因。
    /// </summary>
    /// <remarks>
    /// <para><b>复用 AI 自己的判定函数，而不是重写一套。</b>视野锥用
    /// <see cref="AIPerceptionProfile.IsInsideViewCone"/>，遮挡用
    /// <see cref="AISensor.HasLineOfSight"/>——调试层只负责在判定失败时**补上原因**。
    /// 如果调试层自己写一套判定，两边迟早出现"面板说看不见、AI 却在开枪"的矛盾，
    /// 而那种矛盾比没有面板更糟糕。</para>
    ///
    /// <para><b>关于"被掩体挡住"这一档：</b>它包含两种情况——射线打到了别的物体（真被挡住），
    /// 以及射线什么都没打中（场景装配异常，AI 按保守策略也判为看不见）。
    /// 两者对调试者来说都是"视线不通"，因此合并成一档显示。</para>
    /// </remarks>
    public static class AiDetectionDiagnostics
    {
        /// <summary>
        /// 复核一次检测。
        /// </summary>
        /// <param name="target">目标快照。</param>
        /// <param name="observerPosition">观察者的平面位置。</param>
        /// <param name="facing">观察者朝向（单位向量）。</param>
        /// <param name="profile">感知参数。</param>
        /// <param name="probe">射线能力。</param>
        public static AiDetectionResult Evaluate(
            in AiTargetInfo target,
            Vector2F observerPosition,
            Vector2F facing,
            AIPerceptionProfile profile,
            IHitProbe probe)
        {
            if (profile == null)
            {
                return new AiDetectionResult(AiDetectionFailure.NoTarget, 0f, 0f);
            }

            if (!target.Exists)
            {
                return new AiDetectionResult(AiDetectionFailure.NoTarget, 0f, 0f);
            }

            var toTarget = target.Position - observerPosition;
            var distance = toTarget.Magnitude;
            var angle = distance > 1e-4f
                ? System.Math.Abs(AiAngles.DeltaDegrees(
                    AiAngles.ToDegrees(facing),
                    AiAngles.ToDegrees(toTarget)))
                : 0f;

            if (distance > profile.ViewDistanceMeters)
            {
                return new AiDetectionResult(AiDetectionFailure.OutOfRange, distance, angle);
            }

            if (!profile.IsInsideViewCone(observerPosition, facing, target.Position))
            {
                return new AiDetectionResult(AiDetectionFailure.OutsideViewCone, distance, angle);
            }

            var sees = AISensor.HasLineOfSight(
                probe,
                AISensor.ToEyePosition(observerPosition, profile.EyeHeightMeters),
                target.CenterWorld,
                target.CombatantId,
                profile.ViewDistanceMeters);

            return new AiDetectionResult(
                sees ? AiDetectionFailure.Visible : AiDetectionFailure.Blocked,
                distance,
                angle);
        }
    }
}
