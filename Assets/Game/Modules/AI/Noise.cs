using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 噪音档位：按"噪音源有多吵"给感知系统分档。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么移动噪音按速度分档而不是按姿态分档：</b>本项目对标《逃离鸭科夫》，
    /// 蹲下姿态已在 M1 被砍掉（主文档 7.13 节）。噪音因此改为由速度产生，
    /// 并且刻意与"贪婪循环"绑定：跑得快能躲开危险，但会把敌人引过来。</para>
    ///
    /// <para><b>枪声为什么也在这张表里：</b>它和脚步是同一类东西——
    /// "别人能听见我在这里"。把它们放在同一档位表里之后，
    /// AI 侧完全不需要区分声源类型，只需要一个半径即可（见 <see cref="AIPerceptionProfile.HearingRadiusFor"/>）。</para>
    ///
    /// <para>档位只描述**噪音源有多吵**，不描述"谁能听到"——
    /// 各档对应的实际半径放在 <see cref="AIPerceptionProfile"/> 里，
    /// 属于难度参数，可以随关卡调整而不必改代码。</para>
    /// </remarks>
    public enum NoiseTier
    {
        /// <summary>静止。完全静默，半径 0。</summary>
        Silent = 0,

        /// <summary>步行。噪音半径小，属于安全但推进缓慢的选项。</summary>
        Walk,

        /// <summary>奔跑。噪音半径大，这是本项目噪音系统的核心来源。</summary>
        Sprint,

        /// <summary>负重超载。半径最大：这是贪心带来的额外代价。</summary>
        Overloaded,

        /// <summary>
        /// 枪声。**比任何移动档都吵**：开一枪等于向附近宣告自己的位置。
        /// </summary>
        /// <remarks>
        /// 追加在末尾：数值一旦被序列化，中间插入会让后续成员整体位移。
        /// 本枚举目前只存在于运行时，但保持这个习惯可以避免将来踩坑。
        /// </remarks>
        Gunshot,
    }

    /// <summary>
    /// 一次噪音刺激：谁、在哪个位置、有多吵。
    /// </summary>
    /// <remarks>
    /// <para>它由**产生噪音的一方**发布（玩家的移动与开火、AI 的开火），由 AI 的感知系统消费。
    /// AI 模块不认识背包，也不知道什么叫"负重"，它只认档位——
    /// 于是"超载跑动更吵"这条规则可以通过换一个档位来装配，
    /// 而不需要在 AI 里读取玩家的负重状态。</para>
    ///
    /// <para>噪音不做遮挡判定：声音绕过掩体是玩家能直觉预期的行为，
    /// 而做一套声学遮挡的收益远小于它的复杂度。视野才需要射线遮挡。</para>
    /// </remarks>
    public readonly struct NoiseEvent : IEventEnvelope
    {
        /// <summary>创建一次噪音刺激。</summary>
        /// <param name="sourceId">噪音来源标识（玩家编号或单位标识）。</param>
        /// <param name="position">噪音产生的平面位置。</param>
        /// <param name="tier">噪音档位。</param>
        /// <param name="radiusMeters">
        /// 可听半径（米）。**这个值才是权威**：档位只用于显示与归类，
        /// 而半径允许每个声源各不相同——枪声用武器的射程（手枪比步枪安静），
        /// 脚步用 <see cref="AIPerceptionProfile.HearingRadiusFor"/> 的档位表。
        /// </param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        public NoiseEvent(
            int sourceId,
            Vector2F position,
            NoiseTier tier,
            float radiusMeters,
            double timestamp = 0d)
        {
            SourceId = sourceId;
            Position = position;
            Tier = tier;
            RadiusMeters = radiusMeters < 0f ? 0f : radiusMeters;
            Timestamp = timestamp;
        }

        /// <summary>噪音来源标识。</summary>
        public int SourceId { get; }

        /// <summary>噪音位置（平面坐标）。</summary>
        public Vector2F Position { get; }

        /// <summary>噪音档位。</summary>
        public NoiseTier Tier { get; }

        /// <summary>可听半径（米）。听者与声源的距离不超过它就能听见。</summary>
        public float RadiusMeters { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "ai.noise"; }
        }

        /// <inheritdoc />
        public uint Sequence
        {
            get { return 0u; }
        }

        public override string ToString()
        {
            return $"NoiseEvent(来源={SourceId}, 档位={Tier}, 半径={RadiusMeters:F0}米, 位置={Position})";
        }
    }

    /// <summary>
    /// 把"本帧移动状态"翻译成噪音档位。
    /// </summary>
    /// <remarks>
    /// <para>这条规则刻意做成纯函数并放在 AI 模块：它是"多吵"的定义，
    /// 而"谁能听到"是感知参数。两者分开之后，难度调整不需要动代码逻辑。</para>
    ///
    /// <para>判定顺序是先看是否超载，再看速度是否达到奔跑阈值。这样做的原因：
    /// 超载状态下的速度仍可能低于奔跑阈值（超载本身会减速），
    /// 若先判速度，超载这一档几乎永远不会被命中，罚则形同虚设。</para>
    ///
    /// <para><b>本类只负责"移动产生的噪音"。</b>枪声不是移动，因此不由这里判定——
    /// 它由开火方直接以 <see cref="NoiseTier.Gunshot"/> 档发布。</para>
    /// </remarks>
    public static class MovementNoiseRules
    {
        /// <summary>低于该速度视为静止。</summary>
        public const float MovingSpeedThreshold = 0.05f;

        /// <summary>
        /// 由当前速度与负重状态判定噪音档位。
        /// </summary>
        /// <param name="speed">本帧实际移动速度（单位/秒）。</param>
        /// <param name="sprintSpeedThreshold">
        /// 奔跑判定阈值。由移动配置提供，因此负重减速之后阈值仍与速度同源。
        /// </param>
        /// <param name="isOverloaded">当前是否处于超载状态。</param>
        public static NoiseTier Classify(float speed, float sprintSpeedThreshold, bool isOverloaded)
        {
            if (speed <= MovingSpeedThreshold)
            {
                // 静止优先于超载：站着不动就是静音，超载不会凭空制造声音。
                return NoiseTier.Silent;
            }

            if (isOverloaded)
            {
                return NoiseTier.Overloaded;
            }

            return speed >= sprintSpeedThreshold
                ? NoiseTier.Sprint
                : NoiseTier.Walk;
        }
    }
}
