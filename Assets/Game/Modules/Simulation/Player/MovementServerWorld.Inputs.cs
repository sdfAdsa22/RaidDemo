using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 服务器移动世界的输入通道：接收客户端输入、排队、拒绝重复与乱序。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一份文件：</b>"输入怎么进来"与"世界怎么推进"是两件独立的事，
    /// 而"输入被丢掉"恰恰是最难从现象倒推的原因（P-45：客户端低帧率时一帧补多步，
    /// 服务器每个固定步只消费一条，旧实现直接覆盖待处理输入 → 客户端位置稳定领先一步）。</para>
    /// </remarks>
    public sealed partial class MovementServerWorld
    {
        /// <summary>
        /// 每名玩家最多积压多少条未消费输入。
        /// </summary>
        /// <remarks>
        /// 32 条按 60 Hz 约等于半秒：正常网络抖动（一帧补两三条）远到不了这个量；
        /// 真被灌满时宁可拒收**新**输入，也不丢队列里已有的——已经排队的那些输入
        /// 与客户端记过的预测一一对应，丢掉它们会让两端的位置重新错开。
        /// </remarks>
        private const int MaxPendingInputs = 32;

        /// <summary>
        /// 接收一条客户端输入。
        /// </summary>
        /// <param name="intent">输入内容。</param>
        /// <param name="rejection">被拒绝时的原因；接受时为 null。</param>
        /// <remarks>
        /// <para>序号必须严格大于"已处理序号"与"队列里最新序号"：重复包与乱序包在这里丢掉，
        /// 否则一次重传就能让角色倒退回去。</para>
        ///
        /// <para>被接受的输入**入队**而不是覆盖旧输入：客户端按固定 60 Hz 步进，
        /// 帧率低于 60 时一帧会补齐多步、连发多条输入；覆盖实现会让"已处理序号"
        /// 与"实际执行步数"错开（P-45）。</para>
        /// </remarks>
        public bool TrySubmitInput(in PlayerMoveIntent intent, out string rejection)
        {
            rejection = null;

            var slot = Find(intent.PlayerId);
            if (slot == null)
            {
                rejection = $"玩家 {intent.PlayerId} 不在世界内，输入被丢弃。";
                return false;
            }

            if (intent.Sequence <= slot.LastProcessedSequence)
            {
                rejection = $"输入序号 {intent.Sequence} 不大于已处理序号 {slot.LastProcessedSequence}，按重复包丢弃。";
                slot.DroppedInputs++;
                return false;
            }

            // 比"已接收的最新一条"还旧的一律按乱序包拒绝，
            // 否则重放的旧包会让角色先按新输入走、再退回旧输入走。
            if (slot.PendingInputs.Count > 0 && intent.Sequence <= slot.NewestQueuedSequence)
            {
                rejection = $"输入序号 {intent.Sequence} 不晚于待处理序号 {slot.NewestQueuedSequence}，按重复或乱序包丢弃。";
                slot.DroppedInputs++;
                return false;
            }

            if (slot.PendingInputs.Count >= MaxPendingInputs)
            {
                // 队列满：拒收这一条（而不是丢掉队首）。丢掉队首会让已经排队的输入
                // 与客户端的预测编号错开，正是"一步偏差"的来源。
                slot.DroppedInputs++;
                rejection =
                    $"玩家 {intent.PlayerId} 的待处理输入已达上限 {MaxPendingInputs} 条，"
                    + $"输入序号 {intent.Sequence} 被拒收。";
                return false;
            }

            slot.PendingInputs.Enqueue(intent);
            slot.NewestQueuedSequence = intent.Sequence;
            return true;
        }

        /// <summary>
        /// 取一名玩家的输入诊断计数（排障面板与 Verbose 日志使用）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="droppedInputs">被丢弃的输入条数（溢出 / 重复 / 乱序）。</param>
        /// <param name="pendingInputs">当前积压未消费的输入条数。</param>
        /// <returns>玩家在世界内返回 true。</returns>
        /// <remarks>
        /// 两个计数各有含义：<paramref name="droppedInputs"/> 必须是 0（丢输入会让两端位置错开）；
        /// <paramref name="pendingInputs"/> 正常应稳定在个位数（等于"还没被固定步消费的输入"）。
        /// </remarks>
        public bool TryGetInputDiagnostics(int playerId, out int droppedInputs, out int pendingInputs)
        {
            var slot = Find(playerId);
            if (slot == null)
            {
                droppedInputs = 0;
                pendingInputs = 0;
                return false;
            }

            droppedInputs = slot.DroppedInputs;
            pendingInputs = slot.PendingInputs.Count;
            return true;
        }
    }
}
