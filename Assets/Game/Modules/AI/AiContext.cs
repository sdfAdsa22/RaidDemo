using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 状态共享的上下文：自我、记忆、参数，以及本帧的输入与输出。
    /// </summary>
    /// <remarks>
    /// <para>状态之间不互相调用、也不互相持有引用，所有共享信息都从这里取。
    /// 这让"某个状态引用了另一个状态的私有字段"这种耦合不可能发生，
    /// 也让新增状态只需要拿到上下文即可。</para>
    ///
    /// <para>快照与意图都直接透传自 <see cref="AiAgent"/>，是同一个对象实例，
    /// 因此状态里的写入会立刻生效（详见 <see cref="AiIntent"/> 关于引用类型的说明）。</para>
    /// </remarks>
    public sealed class AiContext
    {
        /// <summary>创建上下文。</summary>
        /// <param name="self">本 AI 的运行时状态。</param>
        /// <param name="memory">感知记忆。</param>
        /// <param name="profile">感知与行为参数。</param>
        public AiContext(AiAgent self, AISensesMemory memory, AIPerceptionProfile profile)
        {
            Self = self;
            Memory = memory;
            Profile = profile;
        }

        /// <summary>本 AI 的运行时状态。</summary>
        public AiAgent Self { get; }

        /// <summary>感知记忆。</summary>
        public AISensesMemory Memory { get; }

        /// <summary>感知与行为参数。</summary>
        public AIPerceptionProfile Profile { get; }

        /// <summary>本帧的感知输入。</summary>
        public AiPerceptionSnapshot Snapshot
        {
            get { return Self.Snapshot; }
        }

        /// <summary>本帧的行为意图（状态写入这里）。</summary>
        public AiIntent Intent
        {
            get { return Self.Intent; }
        }

        /// <summary>记忆是否仍然新鲜。</summary>
        public bool HasFreshMemory
        {
            get { return Memory != null && Memory.IsFresh(Snapshot.Time, Profile.MemorySeconds); }
        }

        /// <summary>
        /// 解析"威胁在哪"：看得见就用实时位置，看不见就用最后已知位置。
        /// </summary>
        /// <param name="position">威胁位置。</param>
        /// <returns>存在可用的威胁位置时返回 true。</returns>
        /// <remarks>
        /// 交战与撤退都需要这个判断，而两者的差别只在"是否新鲜"：
        /// 交战允许追向陈旧记忆（追到一半会超时转为调查），撤退必须立刻远离。
        /// </remarks>
        public bool TryResolveThreatPosition(out Vector2F position)
        {
            if (Snapshot.SeesTarget)
            {
                position = Snapshot.Target.Position;
                return true;
            }

            if (Memory != null && Memory.HasMemory)
            {
                position = Memory.LastKnownPosition;
                return true;
            }

            position = Vector2F.Zero;
            return false;
        }

        /// <summary>记录一次目标位置到记忆里。</summary>
        public void RememberThreat(Vector2F position)
        {
            Memory?.Remember(position, Snapshot.Time);
        }
    }
}
