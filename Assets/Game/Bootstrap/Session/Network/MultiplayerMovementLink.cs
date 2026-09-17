using System;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.Simulation;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机客户端的一帧意图：移动 + 战斗边沿事件（没有战斗的场景传默认值即可）。
    /// </summary>
    /// <remarks>
    /// 把"这一帧想干什么"收在一个结构体里传给链路，而不是让链路自己去各处读字段：
    /// 战局与安全屋的输入来源本来就不同（一个有扳机、一个没有），
    /// 由各自的装配根把意图收集好再交下来，链路本身就不需要知道场景里有什么。
    /// </remarks>
    internal struct MovementLinkIntent
    {
        /// <summary>平面移动意图（按键合成，未归一化）。</summary>
        public Vector2F Move;

        /// <summary>朝向（瞄准方向）。</summary>
        public Vector2F Look;

        /// <summary>是否按住奔跑。</summary>
        public bool Sprint;

        /// <summary>是否按住扳机（战局用；安全屋恒为 false）。</summary>
        public bool TriggerHeld;

        /// <summary>是否按住"扶起队友"（战局用）。</summary>
        public bool ReviveHeld;

        /// <summary>本机是否处于不可操控状态（倒地、结算等）：只发保活零输入。</summary>
        public bool NoControl;
    }

    /// <summary>
    /// 联机移动链路：固定步预测 → 输入上行 → 快照下行 → 本机对账 / 远端插值。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么抽成独立类（P4.5-b）：</b>这套链路原本长在 <c>SceneBootstrap</c> 里，
    /// 而共享安全屋需要一模一样的链路（同样的 60 Hz 步长、同样的预测与对账、同样的保活规则）。
    /// 复制一份是最省事的做法，但那意味着"移动手感的每一条规则都有两处实现"——
    /// P-45（冲刺一步的容差）、P-48（冻结时的保活）这类修复以后要改两次，而漏掉一次就是
    /// 只在安全屋（或只在战局）出现的怪现象。因此链路整体下沉到这里，两个场景各持有一份实例。</para>
    ///
    /// <para><b>通道归属是静态的：</b>网络连接由大厅会话跨场景持有，而"移动快照"这个处理器名
    /// 两端只有一个。场景切换的真实顺序是「新场景 Awake（注册）→ 旧场景 OnDestroy（退订）」，
    /// 旧场景若无条件退订，会把新场景刚注册的处理器删掉——症状是进图后队友与敌人全部不动。
    /// 因此退订前必须确认"我是不是当前持有者"（与 P4 在战局侧的修法同一条规则）。</para>
    /// </remarks>
    internal sealed partial class MultiplayerMovementLink
    {
        /// <summary>预测与服务器的共同步长：60 Hz。</summary>
        public const float FixedStep = 1f / 60f;

        /// <summary>单帧最多补算的固定步数，防止卡顿后追帧雪崩。</summary>
        private const int MaxStepsPerFrame = 4;

        /// <summary>上行进度日志的间隔（包）。约 10 秒一条。</summary>
        private const int ProgressLogInterval = 600;

        /// <summary>当前真正持有移动通道的链路（跨场景注册的归属标记）。</summary>
        private static MultiplayerMovementLink s_ChannelOwner;

        private readonly SessionScope m_Session;
        private readonly PlayerMoveCommandHandler m_MoveHandler;
        private readonly PlayerMovementSimulator m_Simulator;
        private readonly PlayerMotor m_Motor;
        private readonly PresentationCatalog m_Catalog;
        private readonly Func<string> m_CharacterIdProvider;
        private readonly bool m_AutoWalk;

        private NetworkManager m_Network;
        private MovementPredictionBuffer m_Prediction;
        private float m_StepAccumulator;
        private bool m_HandlerRegistered;

        /// <summary>
        /// 是否有待上报的换弹请求。
        /// </summary>
        /// <remarks>
        /// 换弹是**边沿事件**，而输入采集发生在每帧、上行发生在固定步里：
        /// 只记当帧状态的话，恰好落在两个固定步之间的那次按键会被丢掉（M2 踩过）。
        /// 因此在这里锁存，由真正发出去的那一步清掉。
        /// </remarks>
        private bool m_ReloadPending;

        /// <summary>
        /// 创建链路。
        /// </summary>
        /// <param name="session">会话（日志与事件总线）。</param>
        /// <param name="moveHandler">本地预测用的移动处理器（与服务器同一套模拟规则）。</param>
        /// <param name="motor">本地玩家表现层（跟随 / 瞬移）。</param>
        /// <param name="catalog">表现层资产目录（远端玩家外观）。</param>
        /// <param name="characterIdProvider">本机选择的角色标识（远端先用同一套外观）。</param>
        /// <param name="autoWalk">是否开启验收日志（<c>-autowalk</c> 时周期性打印远端位置）。</param>
        public MultiplayerMovementLink(
            SessionScope session,
            PlayerMoveCommandHandler moveHandler,
            PlayerMotor motor,
            PresentationCatalog catalog,
            Func<string> characterIdProvider,
            bool autoWalk)
        {
            m_Session = session;
            m_MoveHandler = moveHandler;
            m_Simulator = moveHandler != null ? moveHandler.Simulator : null;
            m_Motor = motor;
            m_Catalog = catalog;
            m_CharacterIdProvider = characterIdProvider;
            m_AutoWalk = autoWalk;
        }

        /// <summary>通道挂上之后触发（战局用它注册战斗 / 容器 / 敌人通道、初始化血量）。</summary>
        public event Action Attached;

        /// <summary>与服务器断开时触发（战局用它清掉敌人视图）。</summary>
        public event Action Disconnected;

        /// <summary>通道是否已经挂上（网络非空即视为已接管）。</summary>
        public bool IsAttached
        {
            get { return m_Network != null; }
        }

        /// <summary>本实例是不是移动通道的当前持有者（跨场景退订保护）。</summary>
        public bool IsChannelOwner
        {
            get { return ReferenceEquals(s_ChannelOwner, this); }
        }

        /// <summary>本机在服务器上的玩家编号；未接上时为 -1。</summary>
        public int LocalPlayerId { get; private set; } = -1;

        /// <summary>已上行的输入包总数（诊断用，见 SceneBootstrap 的同名说明）。</summary>
        public int SentInputCount { get; private set; }

        /// <summary>最后一次"上行停滞"的原因（诊断用；只记第一次）。</summary>
        public string LastInputStallReason { get; private set; } = string.Empty;

        /// <summary>因对账超差而回滚重放的次数（诊断与验收用）。</summary>
        public int RollbackCount { get; private set; }

        /// <summary>已建立的远端玩家视图数量（诊断与验收用）。</summary>
        public int RemoteViewCount
        {
            get { return m_RemoteViews.Count; }
        }

        /// <summary>请求换弹；由装配根在采集到按键时调用一次。</summary>
        public void RequestReload()
        {
            m_ReloadPending = true;
        }

        /// <summary>
        /// 接管一条已经建立的连接：登记本地玩家编号并订阅快照。
        /// </summary>
        /// <param name="network">大厅会话持有的网络管理器。</param>
        /// <param name="localPlayerId">本机在服务器上的玩家编号。</param>
        public void Attach(NetworkManager network, int localPlayerId)
        {
            m_Network = network;
            LocalPlayerId = localPlayerId;
            m_Prediction = new MovementPredictionBuffer();
            m_StepAccumulator = 0f;

            // 认领通道所有权：只有本实例负责退订（旧场景的销毁回调会被这道判断挡掉）。
            s_ChannelOwner = this;

            if (!m_HandlerRegistered && m_Network != null && m_Network.CustomMessagingManager != null)
            {
                // 同名重复注册由 NGO 覆盖（并打一条 Warning），因此"新场景先注册"是安全的；
                // 真正危险的是旧场景随后的退订，那由 IsChannelOwner 挡住。
                m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                    MovementNetworkChannel.SnapshotMessageName,
                    OnSnapshotBatch);
                m_HandlerRegistered = true;
            }

            m_Session?.Log.Info($"[联机] 移动链路已接管连接（玩家标识 {localPlayerId}）。");
            Attached?.Invoke();
        }

        /// <summary>
        /// 退订通道并释放远端视图。
        /// </summary>
        /// <remarks>
        /// 不是当前持有者时只清自己的状态、**不动处理器**：那可能是新场景刚注册的那一个。
        /// </remarks>
        public void Detach()
        {
            if (m_Network != null && m_HandlerRegistered && IsChannelOwner)
            {
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    MovementNetworkChannel.SnapshotMessageName);
                s_ChannelOwner = null;
            }

            m_HandlerRegistered = false;
            m_Network = null;
            m_Prediction = null;
            m_StepAccumulator = 0f;

            ClearRemoteViews();
        }

        /// <summary>
        /// 每帧推进：固定步预测 + 上行输入 + 远端插值。
        /// </summary>
        /// <param name="deltaTime">本帧的缩放后时间（表现层用）。</param>
        /// <param name="networkDeltaTime">本帧的未缩放时间（网络节拍用）。</param>
        /// <param name="intent">本帧意图。</param>
        /// <remarks>
        /// <para><b>上行节拍用未缩放时间：</b>时间被冻结时（结算 / 暂停 / 角色选择）也要继续发包保活，
        /// 否则会在 NGO 的连接超时（约 10 秒）后被判掉线——那是 2026-09-14 排查出的
        /// "结算之后所有人掉线"的直接原因。</para>
        ///
        /// <para><b>卡顿之后不补时间：</b>一帧最多补 <see cref="MaxStepsPerFrame"/> 步，
        /// 多出来的时间直接丢掉。把一次场景加载卡顿攒下的半秒在几帧里补完，会瞬间连发几十条输入，
        /// 服务器消费不过来 → 队列积压 → 两端位置错开（P-45）。</para>
        /// </remarks>
        public void Tick(float deltaTime, float networkDeltaTime, in MovementLinkIntent intent)
        {
            if (m_Network == null)
            {
                return;
            }

            if (!m_Network.IsConnectedClient)
            {
                NoteInputStall("网络未连接");
                return;
            }

            m_StepAccumulator += networkDeltaTime;

            var steps = 0;
            while (m_StepAccumulator >= FixedStep && steps < MaxStepsPerFrame)
            {
                m_StepAccumulator -= FixedStep;
                steps++;
                StepAndSendInput(intent);
            }

            // 累积器超过一秒的步长说明节拍推进异常（正常每帧只攒一两步）。
            if (m_StepAccumulator > 1f)
            {
                NoteInputStall("节拍积压");
            }

            var maxDebt = FixedStep * MaxStepsPerFrame;
            if (m_StepAccumulator > maxDebt)
            {
                m_StepAccumulator = maxDebt;
            }

            UpdateRemoteViews(deltaTime);
        }

        /// <summary>
        /// 推进一个固定步：先本地预测，再把这条输入发给服务器。
        /// </summary>
        /// <remarks>顺序不能反：先算出"这条输入的结果"再记录预测，才能保证记录的是该输入对应的状态。</remarks>
        private void StepAndSendInput(in MovementLinkIntent intent)
        {
            if (m_MoveHandler == null || m_Simulator == null || m_Prediction == null)
            {
                NoteInputStall("移动处理器缺失");
                return;
            }

            // 序号取自进程级发号器（见 PlayerInputSequence 的说明）：
            // 换场景后新链路必须接着旧链路的号继续数，否则服务器会把新链路的前几百条输入
            // 当"重复包"整片丢弃，表现为进图几秒后闪回出生点（U-98）。
            var sequence = PlayerInputSequence.Next();

            // 时间被冻结（结算 / 暂停 / 角色选择把 timeScale 压成 0）或本机不可操控（倒地）时发零输入：
            // 此时发包的唯一目的是保活，界面上残留的按键状态绝不能变成服务器上的移动或开火。
            var frozen = Time.timeScale <= 0f;
            var noControl = intent.NoControl || frozen;

            var moveIntent = new PlayerMoveIntent(
                LocalPlayerId,
                noControl ? Vector2F.Zero : intent.Move,
                intent.Look,
                noControl ? false : intent.Sprint,
                sequence,
                Time.timeAsDouble);

            m_MoveHandler.Tick(FixedStep);
            m_Prediction.Record(moveIntent, FixedStep, m_Simulator.CaptureSnapshot());

            SendInputToServer(moveIntent, intent, frozen);
        }

        /// <summary>
        /// 把一条输入发给服务器。
        /// </summary>
        /// <remarks>用命名消息而不是 RPC：客户端没有对应的玩家网络对象，通道是进程级的。</remarks>
        private void SendInputToServer(in PlayerMoveIntent intent, in MovementLinkIntent linkIntent, bool frozen)
        {
            var messaging = m_Network != null ? m_Network.CustomMessagingManager : null;
            if (messaging == null)
            {
                NoteInputStall("消息通道缺失");
                return;
            }

            var message = new PlayerInputMessage
            {
                Move = new Vector2(intent.MoveDirection.X, intent.MoveDirection.Y),
                Look = new Vector2(intent.LookDirection.X, intent.LookDirection.Y),
                Sprint = intent.WantsToSprint,

                // 冻结期间不带任何战斗意图：结算 / 暂停时按下的键不该变成服务器的开火或换弹请求。
                TriggerHeld = !frozen && linkIntent.TriggerHeld,
                ReloadRequested = !frozen && m_ReloadPending,
                ReviveHeld = !frozen && linkIntent.ReviveHeld,
                Sequence = intent.Sequence,
                Timestamp = intent.Timestamp,
            };

            // 边沿事件发出去就清掉，避免同一次按键在接下来的几个固定步里被重复上报。
            m_ReloadPending = false;

            using (var writer = new FastBufferWriter(48, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                messaging.SendNamedMessage(
                    MovementNetworkChannel.InputMessageName,
                    NetworkManager.ServerClientId,
                    writer);
            }

            // 发送成功才计数：它衡量的是"真正上行的流量"，采样两次即可判断丢线方向。
            SentInputCount++;
            if (SentInputCount % ProgressLogInterval == 0)
            {
                m_Session?.Log.Info($"[联机] 上行正常：已发 {SentInputCount} 包。");
            }
        }

        /// <summary>
        /// 记录一次上行停滞；同一原因只记一次。
        /// </summary>
        /// <remarks>
        /// 用 Warning 而不是 Verbose：联机会话在编辑器里跑的是默认 Info 级，
        /// Verbose 会被直接过滤掉——而"上行停在哪条分支"恰恰要能在实机日志里看见。
        /// </remarks>
        private void NoteInputStall(string reason)
        {
            if (LastInputStallReason == reason)
            {
                return;
            }

            LastInputStallReason = reason;
            m_Session?.Log.Warning($"[联机] 上行停滞：{reason}（已发 {SentInputCount} 包）。");
        }
    }
}
