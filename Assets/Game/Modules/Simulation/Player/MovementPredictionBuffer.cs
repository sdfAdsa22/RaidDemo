using System;
using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 客户端预测历史：记录「我发出去的每一个输入」与「我据此预测出的结果」，用于和服务器快照对账。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么问题：</b>客户端为了手感会先本地跑一遍移动（预测），服务器稍后回权威结果。
    /// 两者一旦不一致（延迟、丢包、被别的玩家推挤、碰撞判定差异），就必须把客户端拉回权威状态，
    /// 并把「还没被服务器处理过的输入」重新跑一遍——否则玩家会看到角色被硬拽回几帧前的位置。</para>
    ///
    /// <para><b>为什么保留整个输入而不只是位置：</b>重放的输入必须是"当时真正按下的操作"，
    /// 而不是"当前的操作"。若用当前输入重放，玩家松手瞬间角色会继续往前冲一截。</para>
    ///
    /// <para><b>容量：</b>按 60 Hz 记录 128 条约等于 2 秒的输入。超出容量时覆盖最旧的一条——
    /// 高延迟下这会让最老的输入失去对账能力，但不会再影响手感（那些输入早已被服务器确认）。</para>
    ///
    /// <para>本类不依赖 Unity，也不依赖网络库，可在 EditMode 测试中完整验证。</para>
    /// </remarks>
    public sealed class MovementPredictionBuffer
    {
        /// <summary>一条预测记录：输入、步长与预测结果。</summary>
        private struct Entry
        {
            public uint Sequence;
            public PlayerMoveIntent Input;
            public PlayerMovementSnapshot Predicted;
            public float DeltaTime;
        }

        private readonly Entry[] m_Entries;
        private int m_Head;
        private int m_Count;

        /// <summary>
        /// 创建预测历史缓冲。
        /// </summary>
        /// <param name="capacity">容量（条）。默认 128 条，按 60 Hz 约 2 秒。</param>
        public MovementPredictionBuffer(int capacity = 128)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量至少为 1。");
            }

            m_Entries = new Entry[capacity];
        }

        /// <summary>当前未被服务器确认的输入条数。</summary>
        public int PendingCount => m_Count;

        /// <summary>容量（条）。</summary>
        public int Capacity => m_Entries.Length;

        /// <summary>最后一条记录的序号；从未记录过时为 0。</summary>
        public uint LastRecordedSequence
        {
            get { return m_Count == 0 ? 0u : m_Entries[Index(m_Count - 1)].Sequence; }
        }

        /// <summary>最旧一条记录的序号；为空时为 0。</summary>
        public uint OldestSequence
        {
            get { return m_Count == 0 ? 0u : m_Entries[m_Head].Sequence; }
        }

        /// <summary>
        /// 记录一次「已发送给服务器」的输入及其预测结果。
        /// </summary>
        /// <param name="input">发送出去的输入（含序号）。</param>
        /// <param name="deltaTime">本次预测使用的步长，重放时要原样复用。</param>
        /// <param name="predictedAfterStep">执行完这条输入之后的模拟快照。</param>
        /// <remarks>
        /// 序号必须单调递增；乱序或重复的记录会被拒绝，避免历史里出现无法排序的条目。
        /// </remarks>
        public void Record(in PlayerMoveIntent input, float deltaTime, in PlayerMovementSnapshot predictedAfterStep)
        {
            if (m_Count > 0 && input.Sequence <= LastRecordedSequence)
            {
                return;
            }

            if (m_Count == m_Entries.Length)
            {
                // 覆盖最旧的一条：头指针前移，计数不变。
                m_Head = (m_Head + 1) % m_Entries.Length;
                m_Count--;
            }

            m_Entries[Index(m_Count)] = new Entry
            {
                Sequence = input.Sequence,
                Input = input,
                Predicted = predictedAfterStep,
                DeltaTime = deltaTime,
            };

            m_Count++;
        }

        /// <summary>清空历史。断线重连或传送之后调用。</summary>
        public void Clear()
        {
            m_Head = 0;
            m_Count = 0;
        }

        /// <summary>
        /// 用服务器快照对账。
        /// </summary>
        /// <param name="acknowledgedSequence">服务器已处理到的输入序号。</param>
        /// <param name="authoritativeState">服务器在该序号上的权威状态。</param>
        /// <param name="positionTolerance">可接受的平面位置误差（米）。低于它就不回滚。</param>
        /// <returns>对账结果。</returns>
        /// <remarks>
        /// <para><b>为什么要有容差：</b>浮点误差、物理求解顺序的细微差别会让两边有几个毫米级的差异，
        /// 每次都回滚等于让玩家一直看到微小的抖动。只有超过容差才值得付一次重放的代价。</para>
        ///
        /// <para>找到基线之后，基线及更早的记录都会从历史里移除——它们已经被服务器确认，不再需要重放。</para>
        /// </remarks>
        public ReconciliationResult Reconcile(
            uint acknowledgedSequence,
            in PlayerMovementSnapshot authoritativeState,
            float positionTolerance)
        {
            var index = IndexOfSequence(acknowledgedSequence);
            if (index < 0)
            {
                // 找不到基线有两种情况：
                // ① 序号比历史里最老的还旧 —— 说明对账来得太晚，保留历史等下一次；
                // ② 序号比最新的还新 —— 说明客户端记录过的输入已被覆盖且服务器已处理完，历史可以直接丢弃。
                if (m_Count > 0 && acknowledgedSequence > LastRecordedSequence)
                {
                    Clear();
                }

                return new ReconciliationResult(false, false, 0f);
            }

            var entry = m_Entries[index];
            var error = Vector2F.Distance(entry.Predicted.Position, authoritativeState.Position);

            // 丢弃基线及更早的记录：它们已经完成使命。
            var dropCount = OffsetOf(index) + 1;
            m_Head = (m_Head + dropCount) % m_Entries.Length;
            m_Count -= dropCount;

            return new ReconciliationResult(true, error > positionTolerance, error);
        }

        /// <summary>
        /// 回滚重放：把模拟器恢复到权威状态，再按序重跑尚未确认的输入。
        /// </summary>
        /// <param name="simulator">客户端自己的预测模拟器。</param>
        /// <param name="authoritativeBase">服务器在确认序号处的权威快照，作为重放起点。</param>
        /// <returns>重放的输入条数。</returns>
        /// <remarks>
        /// 重放结束后，每条记录里保存的预测结果都会被更新为新的预测值，
        /// 这样下一次对账比较的才是"重放之后的预测"，而不是过期的旧预测。
        /// </remarks>
        public int Replay(PlayerMovementSimulator simulator, in PlayerMovementSnapshot authoritativeBase)
        {
            if (simulator == null)
            {
                throw new ArgumentNullException(nameof(simulator));
            }

            simulator.RestoreSnapshot(authoritativeBase);

            for (var i = 0; i < m_Count; i++)
            {
                ref var entry = ref m_Entries[Index(i)];
                simulator.Step(
                    entry.Input.MoveDirection,
                    entry.Input.LookDirection,
                    entry.DeltaTime,
                    entry.Input.WantsToSprint);

                entry.Predicted = simulator.CaptureSnapshot();
            }

            return m_Count;
        }

        /// <summary>取第 offset 条（0 为最旧）在底层数组中的下标。</summary>
        private int Index(int offset)
        {
            return (m_Head + offset) % m_Entries.Length;
        }

        /// <summary>取底层数组下标对应的逻辑偏移；不在有效范围内时返回 -1。</summary>
        private int OffsetOf(int index)
        {
            for (var offset = 0; offset < m_Count; offset++)
            {
                if (Index(offset) == index)
                {
                    return offset;
                }
            }

            return -1;
        }

        /// <summary>按序号查找底层数组下标；找不到返回 -1。</summary>
        private int IndexOfSequence(uint sequence)
        {
            for (var offset = 0; offset < m_Count; offset++)
            {
                var index = Index(offset);
                if (m_Entries[index].Sequence == sequence)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// 一次对账的结论。
    /// </summary>
    public readonly struct ReconciliationResult
    {
        public ReconciliationResult(bool hasBaseline, bool needsRollback, float positionError)
        {
            HasBaseline = hasBaseline;
            NeedsRollback = needsRollback;
            PositionError = positionError;
        }

        /// <summary>是否找到了与服务器序号对应的本地预测记录。</summary>
        public bool HasBaseline { get; }

        /// <summary>误差是否超过容差、需要回滚重放。</summary>
        public bool NeedsRollback { get; }

        /// <summary>平面位置误差（米）。没有基线时为 0。</summary>
        public float PositionError { get; }

        public override string ToString()
        {
            return $"对账(基线={HasBaseline}, 需回滚={NeedsRollback}, 误差={PositionError:F3} m)";
        }
    }
}
