using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.AI
{
    /// <summary>
    /// AI 的当前目标（当前版本就是玩家）。
    /// </summary>
    /// <remarks>
    /// <para>带上三维中心点，是因为视线判定需要一条真实的三维射线：
    /// 只给平面坐标的话，射线只能贴着地面打，会被地面的碰撞体挡住，
    /// 表现为"AI 永远看不见人"。</para>
    ///
    /// <para>坐标是每帧由启动层写入的**快照**而不是引用：AI 逻辑不持有任何场景对象，
    /// 目标消失（阵亡、撤离）时只需要把 <see cref="IsAlive"/> 置为 false。</para>
    /// </remarks>
    public readonly struct AiTargetInfo
    {
        /// <summary>创建一个目标快照。</summary>
        /// <param name="combatantId">目标在战斗层中的单位标识，0 表示没有目标。</param>
        /// <param name="position">目标的平面位置。</param>
        /// <param name="centerWorld">目标中心的世界坐标（视线射线的终点）。</param>
        /// <param name="isAlive">目标是否存活。</param>
        public AiTargetInfo(int combatantId, Vector2F position, Vector3 centerWorld, bool isAlive)
        {
            CombatantId = combatantId;
            Position = position;
            CenterWorld = centerWorld;
            IsAlive = isAlive;
        }

        /// <summary>目标在战斗层中的单位标识。</summary>
        public int CombatantId { get; }

        /// <summary>目标的平面位置。</summary>
        public Vector2F Position { get; }

        /// <summary>目标中心的世界坐标。</summary>
        public Vector3 CenterWorld { get; }

        /// <summary>目标是否存活。</summary>
        public bool IsAlive { get; }

        /// <summary>是否存在可交互的目标（标识非 0 且存活）。</summary>
        public bool Exists
        {
            get { return CombatantId != 0 && IsAlive; }
        }

        /// <summary>没有目标。</summary>
        public static AiTargetInfo None
        {
            get { return new AiTargetInfo(0, Vector2F.Zero, Vector3.zero, false); }
        }
    }

    /// <summary>
    /// 一帧的感知输入。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是可变的类而不是结构体：</b>它每帧被覆写一次，然后被多个状态读取。
    /// 若做成结构体并通过属性暴露，状态里对它赋值会悄悄作用在副本上，
    /// 症状是"状态明明写了意图，AI 却毫无反应"——这类 bug 排查代价极高。
    /// 用引用类型把这种可能性直接消掉。</para>
    ///
    /// <para>它只承载**输入**，不承载结论：状态读它，意图写在 <see cref="AiIntent"/> 上。</para>
    /// </remarks>
    public sealed class AiPerceptionSnapshot
    {
        /// <summary>当前时刻（秒），由 AI 调度器维护的单调时钟。</summary>
        public float Time;

        /// <summary>当前目标快照。</summary>
        public AiTargetInfo Target;

        /// <summary>本帧是否同时满足"在视野锥内"与"无遮挡"。</summary>
        public bool SeesTarget;

        /// <summary>本帧是否收到了新的噪音刺激。</summary>
        public bool HeardNoise;

        /// <summary>噪音位置（<see cref="HeardNoise"/> 为 true 时有效）。</summary>
        public Vector2F NoisePosition;

        /// <summary>噪音档位。</summary>
        public MovementNoiseTier NoiseTier;

        /// <summary>自上一帧以来是否挨过打。</summary>
        public bool WasDamaged;

        /// <summary>攻击者位置（<see cref="WasDamaged"/> 为 true 时有效）。</summary>
        public Vector2F DamageSourcePosition;

        /// <summary>攻击者标识。</summary>
        public int DamageSourceId;

        /// <summary>把一帧的瞬时输入清空，准备接收新的一帧。</summary>
        /// <remarks>
        /// 注意 <see cref="Target"/> 不清空：它是持续状态，由启动层每帧写入，
        /// 而"听到噪音""挨了一枪"是一次性事件，读过即失效。
        /// </remarks>
        public void ClearTransient()
        {
            HeardNoise = false;
            NoisePosition = Vector2F.Zero;
            NoiseTier = MovementNoiseTier.Silent;
            WasDamaged = false;
            DamageSourcePosition = Vector2F.Zero;
            DamageSourceId = 0;
        }
    }

    /// <summary>
    /// 一帧的行为意图：状态写、Agent 执行。
    /// </summary>
    /// <remarks>
    /// <para>状态不直接移动、不直接开枪，只写"想去哪、想朝哪、想不想开火"。
    /// 这样同一套状态可以在完全不同的执行器上复用（本地模拟、服务端权威模拟、
    /// 甚至录制回放），也让状态测试只需要断言意图，不必模拟物理。</para>
    ///
    /// <para>可变类，理由同 <see cref="AiPerceptionSnapshot"/>。</para>
    /// </remarks>
    public sealed class AiIntent
    {
        /// <summary>本帧想要前往的位置（平面坐标）。</summary>
        public Vector2F MoveGoal;

        /// <summary>是否存在移动目标。</summary>
        public bool HasMoveGoal;

        /// <summary>想要面向的方向。为零向量时表示"面向移动方向"。</summary>
        public Vector2F Facing;

        /// <summary>本帧是否想要开火。</summary>
        public bool WantsToFire;

        /// <summary>清空全部意图，准备让状态重新写入。</summary>
        public void Clear()
        {
            MoveGoal = Vector2F.Zero;
            HasMoveGoal = false;
            Facing = Vector2F.Zero;
            WantsToFire = false;
        }

        public override string ToString()
        {
            var move = HasMoveGoal ? MoveGoal.ToString() : "无";
            return $"AiIntent(移动={move}, 朝向={Facing}, 开火={WantsToFire})";
        }
    }
}
