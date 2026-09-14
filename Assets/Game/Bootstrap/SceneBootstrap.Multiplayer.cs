using System.Collections.Generic;
using RaidDemo.Presentation;
using Unity.Netcode;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局装配根的联机客户端部分：接管连接、挂战局专属通道、驱动共享的移动链路。
    /// </summary>
    /// <remarks>
    /// <para><b>P4.5-b 起，"移动"不再是战局独有的东西</b>：共享安全屋需要一模一样的
    /// 固定步预测、输入上行、快照对账与远端插值。因此那部分整体下沉到
    /// <see cref="MultiplayerMovementLink"/>，本文件只剩两件事——</para>
    /// <list type="number">
    /// <item><description>把战局专属的通道挂上连接（战斗 / 敌人 / 容器 / 结算 / 生命）；</description></item>
    /// <item><description>把本帧的战局意图（扳机、换弹、救援、倒地状态）交给链路。</description></item>
    /// </list>
    /// <para>这样"移动手感"的规则只有一份实现，安全屋与战局不可能出现两套容差或两条保活规则。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        private NetworkManager m_NetworkClient;

        /// <summary>共享的移动链路（上行输入 / 快照对账 / 远端玩家插值）。</summary>
        private MultiplayerMovementLink m_MovementLink;

        private bool m_AutoWalk;
        private double m_NextNetworkReportTime;

        /// <summary>远端玩家视图为空时的常量空表（避免调用方到处判空）。</summary>
        private static readonly IReadOnlyDictionary<int, RemotePlayerView> s_EmptyRemoteViews =
            new Dictionary<int, RemotePlayerView>();

        /// <summary>是否处于联机客户端模式。</summary>
        public bool IsMultiplayerClient => m_NetworkClient != null;

        /// <summary>已上行的输入包总数（诊断用）。</summary>
        /// <remarks>
        /// 联机排障时"我到底还在不在发包"是最难从现象反推的一件事：
        /// 断线、掉包、界面冻结的表现都是"对面没反应"。把它做成可读计数，
        /// 采样两次就能区分"没在发"与"发了但对面没收到"（2026-09-14 联机基础问题排查）。
        /// </remarks>
        public int SentInputCount => m_MovementLink != null ? m_MovementLink.SentInputCount : 0;

        /// <summary>最后一次"上行停滞"的原因（诊断用）。</summary>
        /// <remarks>
        /// 上行停发有好几个分支（网络未连接 / 移动处理器缺失 / 消息通道缺失），
        /// 现象一模一样都是"对面没反应"，但修法完全不同。这里只记录**第一次**停滞的原因：
        /// 重复刷屏没有价值，而"第一次从哪条分支掉出去"正好是定位需要的全部信息
        /// （2026-09-14 联机掉线排查）。
        /// </remarks>
        public string LastInputStallReason =>
            m_MovementLink != null ? m_MovementLink.LastInputStallReason : string.Empty;

        /// <summary>本局因对账超差而回滚重放的次数（诊断与验收用）。</summary>
        public int RollbackCount => m_MovementLink != null ? m_MovementLink.RollbackCount : 0;

        /// <summary>已建立的远端玩家视图数量（调试与验收用）。</summary>
        public int RemoteViewCount => m_MovementLink != null ? m_MovementLink.RemoteViewCount : 0;

        /// <summary>远端玩家视图表（自瞄与救援脚本按玩家编号查位置）。</summary>
        internal IReadOnlyDictionary<int, RemotePlayerView> RemoteViews =>
            m_MovementLink != null ? m_MovementLink.RemoteViews : s_EmptyRemoteViews;

        /// <summary>
        /// 本进程是否以联机客户端身份运行。
        /// </summary>
        /// <remarks>
        /// <para>两条来源都要认：命令行 <c>-connect</c>（<see cref="ClientMode"/>）与
        /// 大厅界面建立的会话（<see cref="MultiplayerClientSession"/>）。
        /// 单机流程两者都为假。</para>
        ///
        /// <para><b>为什么用"进程级"判断而不是"网络是否连上"：</b>装配顺序上，
        /// 判断"要不要生成 AI、要不要显示主菜单"发生在网络接管之前，
        /// 那时候连接可能还在建立中——用后者会把联机局当成单机局来装配。</para>
        /// </remarks>
        private static bool IsMultiplayerProcess => ClientMode.IsActive || MultiplayerClientSession.IsActive;

        /// <summary>本机玩家的标识；未连接时为 -1。</summary>
        public int LocalNetworkPlayerId => m_MovementLink != null ? m_MovementLink.LocalPlayerId : -1;

        /// <summary>
        /// 若本次运行是联机客户端，则接管大厅会话已经建立的连接，并装配预测缓冲。
        /// </summary>
        /// <remarks>
        /// <para><b>P4 的改动：网络连接不再由战局场景创建。</b>联机流程现在是
        /// 大厅（安全屋场景）→ 服务器通知开局 → 加载地图场景，连接必须先于战局存在、
        /// 并且活过这次场景切换。因此网络管理器由大厅会话持有（跨场景存活），
        /// 战局装配只是<b>借用</b>它，并在这里挂上战局专属的快照与表现通道。</para>
        ///
        /// <para><b>必须在装配末尾调用</b>：此时移动模拟器、玩家表现与事件订阅都已就绪，
        /// 网络层只需要接管"谁是权威"这件事。</para>
        /// </remarks>
        private void InitializeMultiplayerClientIfNeeded()
        {
            var session = MultiplayerClientSession.Current;
            if (session == null || session.Network == null)
            {
                // 既不是联机客户端，也没有会话：单机路径，什么都不用接。
                return;
            }

            m_AutoWalk = ClientMode.Options != null && ClientMode.Options.AutoWalk;

            if (m_AutoWalk && m_InputCollector != null)
            {
                // 验收辅助：让角色自己绕圈走，远端才看得到"有人在动"。
                m_InputCollector.UseScriptedInput = true;
                Debug.Log("[联机] 已开启自动行走（-autowalk），用于无头验收。");
            }

            m_MovementLink = new MultiplayerMovementLink(
                m_Session,
                m_MoveHandler,
                m_PlayerMotor,
                m_PresentationCatalog,
                ResolveLocalCharacterId,
                m_AutoWalk);
            m_MovementLink.Attached += OnMovementLinkAttached;
            m_MovementLink.Disconnected += OnMovementLinkDisconnected;

            m_NetworkClient = session.Network;
            m_NetworkClient.OnClientDisconnectCallback += OnNetworkDisconnected;

            if (m_NetworkClient.IsConnectedClient)
            {
                // 大厅已经连上了：直接挂战局通道，不等下一次连接回调。
                AttachToServer(session.LocalClientId);
                return;
            }

            m_NetworkClient.OnClientConnectedCallback += OnNetworkConnected;
            Debug.Log("[联机] 战局场景已就绪，等待大厅会话连接完成。");
        }

        /// <summary>
        /// 退订战局专属通道与连接回调。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么必须做：</b>网络管理器现在由大厅会话持有、跨场景存活，
        /// 而注册的处理器指向的是**本场景的对象**。场景销毁后若不退订，
        /// 下一局开局时（第二次加载战局场景）会注册不上（同名处理器已存在），
        /// 表现为"第二局看不到队友、看不到敌人、箱子是空的"——而日志里只有一条警告。</para>
        ///
        /// <para>由 <c>SceneBootstrap.OnDestroy</c> 统一调用（一个类只能有一个 OnDestroy）。</para>
        /// </remarks>
        private void DetachFromServer()
        {
            // 只允许"当前持有者"退订：跨场景连接上，旧场景的 OnDestroy 发生在新场景注册之后，
            // 不做这个判断就会把新场景刚注册的处理器全部删掉（见 MultiplayerMovementLink 的说明）。
            if (m_MovementLink == null || !m_MovementLink.IsChannelOwner)
            {
                m_MovementLink = null;
                m_NetworkClient = null;
                return;
            }

            if (m_NetworkClient != null)
            {
                m_NetworkClient.OnClientConnectedCallback -= OnNetworkConnected;
                m_NetworkClient.OnClientDisconnectCallback -= OnNetworkDisconnected;
            }

            m_MovementLink.Detach();
            m_MovementLink = null;

            UnregisterCombatChannel();
            UnregisterEnemyChannel();
            UnregisterContainerChannel();
            UnregisterRaidOutcomeChannel();
            UnregisterLifeChannel();

            m_NetworkClient = null;
        }

        /// <summary>
        /// 连接成功：记录本机玩家标识并订阅快照。
        /// </summary>
        /// <param name="clientId">本机在服务器上的连接标识（等于自己的玩家标识）。</param>
        private void OnNetworkConnected(ulong clientId)
        {
            AttachToServer((int)clientId);
        }

        /// <summary>
        /// 挂上战局通道（连接已建立时调用）。
        /// </summary>
        /// <param name="playerId">本机在服务器上的玩家编号。</param>
        private void AttachToServer(int playerId)
        {
            Debug.Log($"[联机] 已连接，玩家标识 {playerId}。");
            m_MovementLink?.Attach(m_NetworkClient, playerId);
        }

        /// <summary>
        /// 移动链路挂上连接之后：注册战局专属通道，并把本地血量按服务器规则初始化。
        /// </summary>
        private void OnMovementLinkAttached()
        {
            RegisterCombatChannel();
            RegisterEnemyChannel();
            RegisterContainerChannel();
            RegisterRaidOutcomeChannel();
            RegisterLifeChannel();

            // 联机里血量由服务器说了算，因此连上就先按满血显示一次：
            // 否则在挨第一枪之前，界面上的血量是"本地战斗世界"的默认值（联机里根本没建）。
            ApplyLocalHealthFromServer(ServerCombatCoordinator.DefaultMaxHealth, true);
        }

        /// <summary>移动链路断开：清掉敌人视图（玩家视图由链路自己清）。</summary>
        private void OnMovementLinkDisconnected()
        {
            ClearRemoteEnemyViews();
        }

        /// <summary>连接断开：清理远端视图，避免留下不会动的假队友。</summary>
        private void OnNetworkDisconnected(ulong clientId)
        {
            Debug.LogWarning($"[联机] 与服务器断开（clientId={clientId}）。");
            m_MovementLink?.NotifyDisconnected();
        }

        /// <summary>
        /// 联机客户端的每帧推进：固定步预测 + 上行输入 + 远端插值。
        /// </summary>
        private void TickMultiplayerClient(float deltaTime, float networkDeltaTime)
        {
            ReportNetworkState();

            if (m_NetworkClient == null || !m_NetworkClient.IsConnectedClient)
            {
                // 链路自己会记录"上行停滞：网络未连接"，这里只需要推进远端插值，避免画面卡死。
                m_MovementLink?.Tick(deltaTime, networkDeltaTime, BuildMovementLinkIntent());
                UpdateRemoteEnemyViews(deltaTime);
                return;
            }

            CollectReviveIntent();
            TickDownedHud(deltaTime);

            if (m_AutoWalk)
            {
                UpdateAutoWalkInput();
            }

            // 移动链路负责：固定步预测 → 上行 → 本机对账 → 远端玩家插值。
            m_MovementLink?.Tick(deltaTime, networkDeltaTime, BuildMovementLinkIntent());

            UpdateRemoteEnemyViews(deltaTime);
        }

        /// <summary>
        /// 把本帧的战局意图整理给移动链路。
        /// </summary>
        /// <remarks>
        /// 换弹不在这里传：它是边沿事件，由链路线性锁存（见 <see cref="MultiplayerMovementLink.RequestReload"/>）。
        /// 倒地时链路只发保活零输入——本地预测也必须跟着停，
        /// 否则本地位置会一直往前跑，快照每 50 毫秒把它拉回来一次，画面上是"躺着还在抽"。
        /// </remarks>
        private MovementLinkIntent BuildMovementLinkIntent()
        {
            return new MovementLinkIntent
            {
                Move = m_PendingMoveIntent,
                Look = m_PendingLookDirection,
                Sprint = m_PendingWantsToSprint,
                TriggerHeld = m_NetworkTriggerHeld,
                ReviveHeld = m_NetworkReviveHeld,
                NoControl = m_LocalDowned,
            };
        }

        /// <summary>
        /// 本机选择的角色标识：远端玩家先用同一套外观（每名玩家各自的外观属于大厅范畴）。
        /// </summary>
        private string ResolveLocalCharacterId()
        {
            var progress = RaidFlowController.Ensure().Progress;
            return progress != null ? progress.SelectedCharacterId : null;
        }
    }
}
