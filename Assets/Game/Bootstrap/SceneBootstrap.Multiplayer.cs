using System.Collections.Generic;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.Simulation;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局装配根的联机客户端部分：上行输入、下行快照、本地预测与远端插值。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么客户端也要按固定步长推进：</b>预测能成立的前提是"两边用同样的步长算同样的输入"。
    /// 若客户端按帧间隔推进、服务器按 1/60 秒推进，两条轨迹会持续分叉，
    /// 表现为角色一直在被轻微回拉。</para>
    ///
    /// <para><b>输入与步长一一对应：</b>每个固定步产生一条输入并记录一条预测，
    /// 序号随步递增。服务器的输入序号就是按这个节奏消费的，因此对账时能精确对上。</para>
    ///
    /// <para><b>远端玩家的表现：</b>快照进插值缓冲，渲染时取"服务器估计时间 − 100 ms"的位置，
    /// 因此队友看起来比真实位置晚一点点，但轨迹是连续平滑的。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>预测与服务器的共同步长：60 Hz。</summary>
        private const float NetworkFixedStep = 1f / 60f;

        /// <summary>单帧最多补算的固定步数，防止卡顿后追帧雪崩。</summary>
        private const int MaxNetworkStepsPerFrame = 4;

        /// <summary>位置误差超过该值（米）才回滚重放。</summary>
        private const float ReconciliationTolerance = 0.08f;

        private NetworkManager m_NetworkClient;
        private UnityTransport m_NetworkTransport;
        private MovementPredictionBuffer m_Prediction;
        private float m_NetworkStepAccumulator;
        private uint m_NetworkSequence;
        private int m_LocalPlayerId = -1;
        private bool m_NetworkSnapshotHandlerRegistered;
        private bool m_AutoWalk;
        private double m_NextNetworkReportTime;

        /// <summary>是否处于联机客户端模式。</summary>
        public bool IsMultiplayerClient => ClientMode.IsActive && m_NetworkClient != null;

        /// <summary>本机玩家的标识；未连接时为 -1。</summary>
        public int LocalNetworkPlayerId => m_LocalPlayerId;

        /// <summary>
        /// 若以联机客户端启动，则建立网络连接与预测缓冲。
        /// </summary>
        /// <remarks>
        /// 在装配末尾调用：此时移动模拟器、玩家表现与事件订阅都已就绪，
        /// 网络层只需要接管"谁是权威"这件事。
        /// </remarks>
        private void InitializeMultiplayerClientIfNeeded()
        {
            if (!ClientMode.IsActive)
            {
                return;
            }

            m_Prediction = new MovementPredictionBuffer();
            m_AutoWalk = ClientMode.Options != null && ClientMode.Options.AutoWalk;

            if (m_AutoWalk && m_InputCollector != null)
            {
                // 验收辅助：让角色自己绕圈走，远端才看得到"有人在动"。
                m_InputCollector.UseScriptedInput = true;
                Debug.Log("[联机] 已开启自动行走（-autowalk），用于无头验收。");
            }

            var host = new GameObject("NetworkClient");
            DontDestroyOnLoad(host);

            m_NetworkClient = host.AddComponent<NetworkManager>();
            m_NetworkTransport = host.AddComponent<UnityTransport>();

            // 与服务器对称：verbose 时打开 NGO 开发者日志，便于定位连接问题。
            m_NetworkClient.LogLevel = ClientMode.Options != null
                                       && ClientMode.Options.MinimumLogLevel == Kernel.LogLevel.Verbose
                ? Unity.Netcode.LogLevel.Developer
                : Unity.Netcode.LogLevel.Normal;

            var (address, port) = SplitAddress(ClientMode.Address);
            m_NetworkTransport.SetConnectionData(address, port);

            // 与服务器共用同一份配置工厂：任何一处差异都会让 NGO 在握手阶段断开连接。
            m_NetworkClient.NetworkConfig = NetworkConfigFactory.Create(m_NetworkTransport);

            m_NetworkClient.OnClientConnectedCallback += OnNetworkConnected;
            m_NetworkClient.OnClientDisconnectCallback += OnNetworkDisconnected;

            if (!m_NetworkClient.StartClient())
            {
                Debug.LogError($"[联机] 连接失败：{address}:{port}");
                return;
            }

            Debug.Log($"[联机] 正在连接 {address}:{port} …");
        }

        /// <summary>
        /// 连接成功：记录本机玩家标识并订阅快照。
        /// </summary>
        /// <param name="clientId">本机在服务器上的连接标识（等于自己的玩家标识）。</param>
        private void OnNetworkConnected(ulong clientId)
        {
            m_LocalPlayerId = (int)clientId;
            Debug.Log($"[联机] 已连接，玩家标识 {m_LocalPlayerId}。");

            if (m_NetworkSnapshotHandlerRegistered || m_NetworkClient.CustomMessagingManager == null)
            {
                return;
            }

            m_NetworkClient.CustomMessagingManager.RegisterNamedMessageHandler(
                MovementNetworkChannel.SnapshotMessageName,
                OnSnapshotBatch);
            m_NetworkSnapshotHandlerRegistered = true;

            RegisterCombatChannel();
            RegisterEnemyChannel();
            RegisterContainerChannel();
            RegisterRaidOutcomeChannel();

            // 联机里血量由服务器说了算，因此连上就先按满血显示一次：
            // 否则在挨第一枪之前，界面上的血量是"本地战斗世界"的默认值（联机里根本没建）。
            ApplyLocalHealthFromServer(ServerCombatCoordinator.DefaultMaxHealth, true);
        }

        /// <summary>连接断开：清理远端视图，避免留下不会动的假队友。</summary>
        private void OnNetworkDisconnected(ulong clientId)
        {
            Debug.LogWarning($"[联机] 与服务器断开（clientId={clientId}）。");

            ClearRemoteViews();
            ClearRemoteEnemyViews();
        }

        /// <summary>
        /// 联机客户端的每帧推进：固定步预测 + 上行输入 + 远端插值。
        /// </summary>
        private void TickMultiplayerClient(float deltaTime)
        {
            ReportNetworkState();

            if (m_NetworkClient == null || !m_NetworkClient.IsConnectedClient)
            {
                return;
            }

            m_NetworkStepAccumulator += deltaTime;

            if (m_AutoWalk)
            {
                UpdateAutoWalkInput();
            }

            var steps = 0;
            while (m_NetworkStepAccumulator >= NetworkFixedStep && steps < MaxNetworkStepsPerFrame)
            {
                m_NetworkStepAccumulator -= NetworkFixedStep;
                steps++;
                StepAndSendInput();
            }

            UpdateRemoteViews(deltaTime);
            UpdateRemoteEnemyViews(deltaTime);
        }

        /// <summary>
        /// 推进一个固定步：先本地预测，再把这条输入发给服务器。
        /// </summary>
        /// <remarks>
        /// 顺序不能反：先算出"这条输入的结果"再记录预测，才能保证记录的是该输入对应的状态。
        /// </remarks>
        private void StepAndSendInput()
        {
            if (m_MoveHandler == null)
            {
                return;
            }

            m_NetworkSequence++;

            var intent = new PlayerMoveIntent(
                m_LocalPlayerId,
                m_PendingMoveIntent,
                m_PendingLookDirection,
                m_PendingWantsToSprint,
                m_NetworkSequence,
                Time.timeAsDouble);

            m_MoveHandler.Tick(NetworkFixedStep);
            m_Prediction.Record(intent, NetworkFixedStep, Movement.CaptureSnapshot());

            SendInputToServer(intent);
        }

        /// <summary>
        /// 把这一条输入发给服务器。
        /// </summary>
        /// <remarks>
        /// 用命名消息而不是 RPC：P1 还没有玩家网络对象（见 <see cref="MovementNetworkChannel"/> 的说明）。
        /// 连接已断时这里什么都不做——每帧的连接状态检查会负责收尾。
        /// </remarks>
        private void SendInputToServer(in PlayerMoveIntent intent)
        {
            var messaging = m_NetworkClient != null ? m_NetworkClient.CustomMessagingManager : null;
            if (messaging == null)
            {
                return;
            }

            var message = new PlayerInputMessage
            {
                Move = new Vector2(intent.MoveDirection.X, intent.MoveDirection.Y),
                Look = new Vector2(intent.LookDirection.X, intent.LookDirection.Y),
                Sprint = intent.WantsToSprint,
                TriggerHeld = m_NetworkTriggerHeld,

                // 换弹是边沿事件：发出去之后就清掉，避免同一次按键被重复上报。
                ReloadRequested = m_NetworkReloadRequested,
                Sequence = intent.Sequence,
                Timestamp = intent.Timestamp,
            };

            m_NetworkReloadRequested = false;

            using (var writer = new FastBufferWriter(48, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                messaging.SendNamedMessage(
                    MovementNetworkChannel.InputMessageName,
                    NetworkManager.ServerClientId,
                    writer);
            }
        }

        /// <summary>
        /// 收到一批快照：本机玩家对账，其他玩家进插值缓冲。
        /// </summary>
        private void OnSnapshotBatch(ulong senderId, FastBufferReader reader)
        {
            var batch = default(PlayerSnapshotBatchMessage);
            reader.ReadValueSafe(out batch);

            if (batch.Players == null)
            {
                return;
            }

            NoteServerClock(batch.ServerTime);

            foreach (var entry in batch.Players)
            {
                if (entry.PlayerId == m_LocalPlayerId)
                {
                    ApplyAuthoritativeSnapshot(entry);
                    continue;
                }

                PushRemoteSnapshot(entry, batch.ServerTime);
            }
        }

        /// <summary>
        /// 用服务器快照校正本机预测。
        /// </summary>
        /// <remarks>
        /// 误差在容差内就什么都不做——每次都回滚会让玩家看到持续的微小抖动。
        /// 超过容差才回滚重放，并把修正后的位置立刻贴到表现上（否则要等下一帧才追上）。
        /// </remarks>
        private void ApplyAuthoritativeSnapshot(in PlayerStateMessage entry)
        {
            var authoritative = entry.ToMovementSnapshot();
            var result = m_Prediction.Reconcile(entry.Sequence, authoritative, ReconciliationTolerance);

            if (!result.NeedsRollback)
            {
                return;
            }

            m_Prediction.Replay(Movement, authoritative);

            var state = Movement.State;
            m_PlayerMotor?.SnapTo(
                new Vector2(state.Position.X, state.Position.Y),
                new Vector2(state.Facing.X, state.Facing.Y));

            Debug.Log(
                $"[联机] 回滚重放：误差 {result.PositionError:F3} 米，" +
                $"重放 {m_Prediction.PendingCount} 条输入（seq={entry.Sequence}）。");
        }

        /// <summary>
        /// 拆分"主机[:端口]"。
        /// </summary>
        /// <remarks>
        /// 只写主机时用 <c>-port</c> 的值（默认 7777）；显式写了端口则以地址里的为准。
        /// 这样"本机联机"只需一个 <c>-connect 127.0.0.1</c>。
        /// </remarks>
        private (string Address, ushort Port) SplitAddress(string raw)
        {
            var text = string.IsNullOrEmpty(raw) ? "127.0.0.1" : raw;
            var separator = text.LastIndexOf(':');

            if (separator > 0 && ushort.TryParse(text.Substring(separator + 1), out var explicitPort))
            {
                return (text.Substring(0, separator), explicitPort);
            }

            var fallback = ClientMode.Options != null ? ClientMode.Options.Port : 7777;
            return (text, (ushort)fallback);
        }
    }
}
