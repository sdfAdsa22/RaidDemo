using System;
using System.Collections.Generic;
using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 服务器侧的移动权威世界：为每名玩家持有一份模拟器，按固定步长推进并产出快照。
    /// </summary>
    /// <remarks>
    /// <para><b>它在联机链路里的位置：</b>客户端把输入发上来 → 本类按 60 Hz 固定步长执行 →
    /// 以 20 Hz 产出快照下发给所有客户端；客户端拿到快照后与自己的预测对账。</para>
    ///
    /// <para><b>为什么用固定步长而不是跟随帧率：</b>服务器帧率会波动，而「同样的输入在不同帧率下
    /// 得到同样的结果」是预测能成立的前提。固定步长把仿真与渲染解耦，客户端才能用同样的步长复现。</para>
    ///
    /// <para><b>输入的应用策略（P1 的简化）：</b>每个固定步取「最近收到的输入」执行，
    /// 这一步没有新输入时沿用上一条——与单机语义一致（松开按键不会立刻停）。
    /// 按时间戳重排、队列按序消费属于更完整的做法，等出现明显的公平性问题再上
    /// （设计文档第 11 节的降级策略）。</para>
    ///
    /// <para>本类不依赖 Unity 与网络库：NGO 那一层只负责把字节搬进搬出。</para>
    /// </remarks>
    public sealed partial class MovementServerWorld
    {
        /// <summary>服务器仿真步长：60 Hz。</summary>
        public const float FixedStepSeconds = 1f / 60f;

        /// <summary>快照下发间隔：20 Hz。</summary>
        public const float SnapshotIntervalSeconds = 1f / 20f;

        /// <summary>
        /// 单次推进最多执行的固定步数。
        /// </summary>
        /// <remarks>
        /// 服务器卡顿或断点调试之后 deltaTime 可能很大，若不设上限就会一口气补上千步，
        /// 把这一帧拖得更久——那正是「死亡螺旋」。宁可丢弃多余的时间。
        /// </remarks>
        private const int MaxStepsPerAdvance = 8;

        /// <summary>世界里的一个玩家槽位。</summary>
        private sealed class PlayerSlot
        {
            public int PlayerId;
            public PlayerMovementSimulator Simulator;

            /// <summary>
            /// 已接收但尚未被固定步消费的输入，**按序排队**。
            /// </summary>
            /// <remarks>
            /// <para><b>为什么必须是队列而不是"只留最新一条"：</b>客户端按固定 60 Hz 步进，
            /// 帧率低于 60 时一帧会补齐多步、连发多条输入。旧实现只有一个槽位，
            /// 更新的输入直接覆盖旧的——被覆盖的那些输入在客户端**已经算过并记入预测**，
            /// 于是"已处理序号"与"实际执行步数"错开，客户端位置稳定领先服务器一步。
            /// 超过对账容差后客户端每次快照都硬吸附一次，表现就是"移动一卡一卡、时不时瞬移"。</para>
            ///
            /// <para>排队之后每条被接受的输入都恰好被一个固定步消费一次，
            /// "序号 ↔ 步数"重新一一对应（2026-09-14 实测：低帧率客户端每秒被覆盖 12~13 条）。</para>
            /// </remarks>
            public readonly Queue<PlayerMoveIntent> PendingInputs = new Queue<PlayerMoveIntent>(8);

            /// <summary>队列里最新一条输入的序号（= 已接收的最新序号）。用于拒绝乱序包。</summary>
            public uint NewestQueuedSequence;

            /// <summary>上一条被消费的输入。没有新输入时继续沿用，避免「松手即停」。</summary>
            public PlayerMoveIntent AppliedInput;

            /// <summary>已经处理到的输入序号。用于拒绝重复与乱序输入。</summary>
            public uint LastProcessedSequence;

            /// <summary>
            /// 被丢弃的输入条数（诊断计数）：队列溢出时拒收新输入，或收到重复 / 乱序包。
            /// </summary>
            /// <remarks>
            /// <para>正常游玩时它应当**恒为 0**：队列容量够大，重复 / 乱序包本身就该被拒。
            /// 它一旦开始增长，说明对端在灌输入（丢包重传、脚本刷包或时间被拉快），
            /// 而"丢输入"正是让客户端与服务器位置错开一步的来源，因此必须可见。</para>
            /// </remarks>
            public int DroppedInputs;
        }

        private readonly PlayerMovementProfile m_Profile;
        private readonly Func<int, IMovementCollisionWorld> m_CollisionFactory;
        private readonly List<PlayerSlot> m_Slots = new List<PlayerSlot>();

        private float m_StepAccumulator;
        private float m_SnapshotAccumulator;
        private double m_SimulationTime;
        private bool m_SnapshotDue;

        /// <summary>
        /// 创建服务器世界。
        /// </summary>
        /// <param name="profile">移动配置。为 null 时使用默认值。</param>
        /// <param name="collisionFactory">
        /// 按玩家创建碰撞世界的工厂。为 null 时不做碰撞修正（灰盒与纯逻辑用例）。
        /// </param>
        public MovementServerWorld(
            PlayerMovementProfile profile = null,
            Func<int, IMovementCollisionWorld> collisionFactory = null)
        {
            m_Profile = profile ?? new PlayerMovementProfile();
            m_CollisionFactory = collisionFactory;
        }

        /// <summary>当前在线玩家数。</summary>
        public int PlayerCount => m_Slots.Count;

        /// <summary>已推进的仿真时间（秒）。它是快照时间戳的唯一来源。</summary>
        public double SimulationTime => m_SimulationTime;

        /// <summary>
        /// 本世界使用的移动参数。
        /// </summary>
        /// <remarks>
        /// 服务器侧 AI 的脚步噪音判定要用它（奔跑阈值必须与玩家实际使用的阈值一致，
        /// 否则"服务器认为他在走、客户端觉得他在跑"）。
        /// </remarks>
        public PlayerMovementProfile Profile => m_Profile;

        /// <summary>是否已有 20 Hz 的快照等待取走。</summary>
        public bool SnapshotDue => m_SnapshotDue;

        /// <summary>
        /// 加入一名玩家。
        /// </summary>
        /// <param name="playerId">玩家标识（与网络连接一一对应）。</param>
        /// <param name="spawnPosition">出生点。</param>
        /// <param name="facing">初始朝向。</param>
        /// <param name="error">失败原因。</param>
        public bool TryAddPlayer(int playerId, Vector2F spawnPosition, Vector2F facing, out string error)
        {
            error = null;

            if (Find(playerId) != null)
            {
                error = $"玩家 {playerId} 已在世界内。";
                return false;
            }

            var collision = m_CollisionFactory?.Invoke(playerId);
            m_Slots.Add(new PlayerSlot
            {
                PlayerId = playerId,
                Simulator = new PlayerMovementSimulator(m_Profile, spawnPosition, facing, collision),
            });

            return true;
        }

        /// <summary>移除一名玩家（掉线或退出）。</summary>
        public bool RemovePlayer(int playerId)
        {
            var slot = Find(playerId);
            if (slot == null)
            {
                return false;
            }

            m_Slots.Remove(slot);
            return true;
        }

        /// <summary>
        /// 把一名玩家直接放到指定位置（重开战局时用）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="position">目标位置。</param>
        /// <param name="facing">目标朝向。</param>
        /// <returns>玩家在世界内时返回 true。</returns>
        /// <remarks>
        /// <para>这是**唯一**允许绕过移动模拟写位置的入口，因此单独命名并写明用途：
        /// 正常游玩的位移一律走输入 → 模拟 → 快照这条链，只有"重开一局"这种重置语义
        /// 才需要把玩家直接搬回去（它的合法性来自服务器自己的决定，而不是某个客户端的请求）。</para>
        /// </remarks>
        public bool TryTeleport(int playerId, Vector2F position, Vector2F facing = default)
        {
            var slot = Find(playerId);
            if (slot == null)
            {
                return false;
            }

            var look = facing.IsNearlyZero ? Vector2F.Up : facing;
            slot.Simulator.Reset(position, look);
            // 传送把玩家搬到了别处：排队中的输入描述的是"旧位置上的操作"，
            // 继续执行它们会让角色从新位置按旧输入乱走，因此一并丢弃。
            slot.PendingInputs.Clear();
            slot.NewestQueuedSequence = slot.LastProcessedSequence;
            return true;
        }

        /// <summary>世界内是否包含该玩家。</summary>
        public bool ContainsPlayer(int playerId)
        {
            return Find(playerId) != null;
        }

        /// <summary>把当前玩家标识写入给定列表（先清空）。</summary>
        public void GetPlayerIds(List<int> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var slot in m_Slots)
            {
                results.Add(slot.PlayerId);
            }
        }

        /// <summary>
        /// 按固定步长推进世界。
        /// </summary>
        /// <param name="deltaTime">自上次推进以来的真实时间（秒）。</param>
        /// <returns>本次实际执行的固定步数。</returns>
        public int Advance(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return 0;
            }

            m_StepAccumulator += deltaTime;
            m_SnapshotAccumulator += deltaTime;

            var steps = 0;
            while (m_StepAccumulator >= FixedStepSeconds && steps < MaxStepsPerAdvance)
            {
                m_StepAccumulator -= FixedStepSeconds;
                steps++;
                StepOnce();
            }

            if (m_SnapshotAccumulator >= SnapshotIntervalSeconds)
            {
                // 只置一次标记：客户端按固定节奏收到快照即可，补发没有意义。
                m_SnapshotDue = true;
                m_SnapshotAccumulator -= SnapshotIntervalSeconds;
            }

            return steps;
        }

        /// <summary>取单个玩家的当前快照。</summary>
        public bool TryGetSnapshot(int playerId, out PlayerSnapshot snapshot)
        {
            var slot = Find(playerId);
            if (slot == null)
            {
                snapshot = default;
                return false;
            }

            snapshot = new PlayerSnapshot(
                slot.PlayerId,
                slot.LastProcessedSequence,
                m_SimulationTime,
                slot.Simulator.CaptureSnapshot());

            return true;
        }

        /// <summary>
        /// 取走全部玩家的快照（按 20 Hz 节拍）。
        /// </summary>
        /// <param name="results">结果列表，会先被清空。</param>
        /// <returns>写入的快照条数；本次不到节拍时返回 0。</returns>
        /// <remarks>取走之后节拍标记被清除，避免同一批快照被重复下发。</remarks>
        /// <summary>
        /// 若到达快照节拍，取出一批玩家快照。
        /// </summary>
        /// <param name="results">输出列表；到达节拍时会被重填（没有玩家时是一个空列表）。</param>
        /// <returns>本帧到达快照节拍返回 true；未到节拍返回 false。</returns>
        /// <remarks>
        /// <para><b>为什么用 bool 而不是返回玩家数：</b>调用方必须能区分"没到节拍"与"到了节拍但没有玩家"。
        /// 战局结束后所有玩家都会被移出世界，而快照通道同时承担着"服务器还活着"的心跳职责——
        /// 若因为"没有玩家"而完全不发包，客户端会在 NGO 的连接超时（约 10 秒）后自行断开，
        /// 表现是"结算之后所有人都掉线、重连也进不来"（2026-09-14 联机基础问题修复的定位结论）。</para>
        /// </remarks>
        public bool TryCaptureSnapshots(List<PlayerSnapshot> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            if (!m_SnapshotDue)
            {
                return false;
            }

            m_SnapshotDue = false;

            foreach (var slot in m_Slots)
            {
                results.Add(new PlayerSnapshot(
                    slot.PlayerId,
                    slot.LastProcessedSequence,
                    m_SimulationTime,
                    slot.Simulator.CaptureSnapshot()));
            }

            return true;
        }

        /// <summary>推进一个固定步：先消费新输入，再让每名玩家的模拟器走一步。</summary>
        private void StepOnce()
        {
            foreach (var slot in m_Slots)
            {
                if (slot.PendingInputs.Count > 0)
                {
                    // 每个固定步只消费一条：排队的输入按序、一条不多一条不少地被执行，
                    // "已处理序号"因此始终等于"已执行的输入步数"。
                    slot.AppliedInput = slot.PendingInputs.Dequeue();
                    slot.LastProcessedSequence = slot.AppliedInput.Sequence;
                }

                slot.Simulator.Step(
                    slot.AppliedInput.MoveDirection,
                    slot.AppliedInput.LookDirection,
                    FixedStepSeconds,
                    slot.AppliedInput.WantsToSprint);
            }

            m_SimulationTime += FixedStepSeconds;
        }

        /// <summary>按标识查找槽位；线性扫描对 2~4 人规模完全够用。</summary>
        private PlayerSlot Find(int playerId)
        {
            foreach (var slot in m_Slots)
            {
                if (slot.PlayerId == playerId)
                {
                    return slot;
                }
            }

            return null;
        }
    }
}
