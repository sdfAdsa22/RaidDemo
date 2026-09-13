using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 移动模拟的**全量**状态快照：不只是位置与朝向，还包括复现后续步进所需的一切。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不能只用 <see cref="PlayerMoveState"/>：</b>体力恢复延迟是靠一个内部计时器
    /// （距上次奔跑结束的时间）实现的，它不在对外暴露的状态里。回滚重放时若不还原这个计时器，
    /// 重放出来的体力曲线会与服务器不同——症状是"回滚之后跑得更久"，而位置看起来还是对的，
    /// 这类分歧极难定位。</para>
    ///
    /// <para><b>用途有三处：</b>客户端预测的历史记录、服务器快照的载入、以及回滚重放的起点。
    /// 三者都要求"把模拟器恢复到某个精确时刻"，因此快照必须完整而不是近似。</para>
    ///
    /// <para>本类型是值类型且不含引用，可以安全地按值传递与复制。</para>
    /// </remarks>
    public struct PlayerMovementSnapshot
    {
        /// <summary>对外可见的移动状态（位置、朝向、速度、体力、状态位）。</summary>
        public PlayerMoveState State;

        /// <summary>
        /// 距上次奔跑结束的时间（秒）。负值表示尚未开始计时，与模拟器内部语义一致。
        /// </summary>
        public float TimeSinceSprintEnd;

        /// <summary>位置（平面投影），供误差比较与插值使用。</summary>
        public Vector2F Position => State.Position;

        /// <summary>朝向（单位向量）。</summary>
        public Vector2F Facing => State.Facing;

        public override string ToString()
        {
            return $"Snapshot({State}, SprintEnd={TimeSinceSprintEnd:F2})";
        }
    }
}
