using Unity.Netcode;
using UnityEngine;
using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的联机装配：接管大厅会话，把本机玩家的移动交给服务器裁定，并画出队友。
    /// </summary>
    /// <remarks>
    /// <para><b>P4.5-b：联机首站是共享安全屋。</b>单机时安全屋是一个自给自足的小世界
    /// （本机内嵌服务器 + 本地模拟）；联机时它必须变成"服务器托管的一间屋子"——
    /// 我走到哪里由服务器说了算，队友的位置来自服务器快照。</para>
    ///
    /// <para><b>为什么复用战局的移动链路：</b>安全屋没有战斗、没有搜刮、没有 AI，
    /// 但它有**移动**——而移动恰恰是联机里规则最多的一块（固定步、预测、对账、保活）。
    /// 两个场景共用 <see cref="MultiplayerMovementLink"/>，手感与容差只可能有一份实现。</para>
    ///
    /// <para><b>刻意不联机的部分（§25.3 已定稿的边界）：</b>仓库、商人、背包内容在共享仓库
    /// 落地（P5）之前仍是各自的本机进度；靶子与衣柜走本地表现层，不参与服务器判定；
    /// 玩家自己的装备也在 P5 才同步给服务器。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        /// <summary>共享安全屋的移动链路（复用战局那一套：固定步预测 + 上行 + 快照对账）。</summary>
        private MultiplayerMovementLink m_MovementLink;

        /// <summary>大厅会话持有的网络管理器（跨场景存活，这里只是借用）。</summary>
        private NetworkManager m_NetworkClient;

        /// <summary>
        /// 共享的容器链路（P5）：背包 / 弹药挂 / 共享仓库 / 装备槽全部由服务器权威。
        /// </summary>
        /// <remarks>
        /// 与战局用的是同一个类：安全屋里的"把枪从仓库拖进主武器槽"和战局里的"把战利品拖进背包"
        /// 是同一种命令、同一套放置规则、同一条上行通道。
        /// </remarks>
        private MultiplayerContainerLink m_ContainerLink;

        /// <summary>
        /// 商店 / 任务链路（P5.5）：购买、出售、接任务与领奖都改为上行，由服务器执行。
        /// </summary>
        private MultiplayerMerchantLink m_MerchantLink;

        /// <summary>本机是否处于联机安全屋模式。</summary>
        public bool IsMultiplayerSafeHouse
        {
            get { return m_MovementLink != null; }
        }

        /// <summary>
        /// 本进程是否以联机客户端身份运行。
        /// </summary>
        /// <remarks>
        /// 两条来源都要认：命令行 <c>-connect</c>（<see cref="ClientMode"/>）与大厅界面建立的会话
        /// （<see cref="MultiplayerClientSession"/>）。用"进程级"判断而不是"网络是否连上"，
        /// 是因为装配发生在连接建立之前——用后者会把联机进程当成单机来装配。
        /// </remarks>
        private static bool IsMultiplayerProcess
        {
            get { return ClientMode.IsActive || MultiplayerClientSession.IsActive; }
        }

        /// <summary>
        /// 每帧检查：联机进程是否该接上共享世界（会话可能晚于本场景 Awake 建立）。
        /// </summary>
        private void EnsureMultiplayerAttached()
        {
            if (m_MovementLink != null || !IsMultiplayerProcess)
            {
                return;
            }

            InitializeMultiplayerIfNeeded();
        }

        /// <summary>
        /// 若本进程是联机客户端，则把安全屋接进大厅会话。
        /// </summary>
        /// <returns>接管成功（即处于联机模式）返回 true；单机返回 false。</returns>
        /// <remarks>
        /// <para>调用时机：主文件 <c>Awake</c> 的装配末尾——此时移动处理器、玩家表现与相机都已就绪，
        /// 链路才能立刻接管"谁是权威"这件事。</para>
        ///
        /// <para><b>与单机的分叉点：</b>单机在 <c>Awake</c> 里创建"本机内嵌服务器"会话并自己做权威；
        /// 联机的会话与连接早在大厅阶段就建立了，安全屋只是借用它，并把自己的移动交给服务器。</para>
        /// </remarks>
        private bool InitializeMultiplayerIfNeeded()
        {
            var session = MultiplayerClientSession.Current;
            if (session == null || session.Network == null)
            {
                return false;
            }

            var autoWalk = ClientMode.Options != null && ClientMode.Options.AutoWalk;
            if (autoWalk && m_InputCollector != null)
            {
                // 验收辅助：无头客户端也要在安全屋里走动，才能证明"共享安全屋的移动是服务器裁定的"。
                m_InputCollector.UseScriptedInput = true;
                Debug.Log("[联机] 安全屋已开启自动行走（-autowalk）。");
            }

            m_MovementLink = new MultiplayerMovementLink(
                m_Session,
                m_MoveHandler,
                m_PlayerMotor,
                m_PresentationCatalog,
                ResolveLocalCharacterId,
                autoWalk);

            m_ContainerLink = new MultiplayerContainerLink(
                m_Session, m_Registry, m_ItemCatalog, m_EventBus, m_Loadout);

            m_MerchantLink = new MultiplayerMerchantLink(m_Session, m_Progress, m_EventBus);

            // 本地处理器已在 InitializeInventory 里注册过：这里用"只上行"的版本覆盖它们，
            // 界面的乐观更新照旧，真正的结果由服务器回发的容器内容纠正（U-75 的规则）。
            m_ContainerLink.InstallCommandHandlers(m_CommandRouter);

            // P5.5：商店与任务同理——单机那套 handler 直接改本地账本，
            // 联机时必须换成"只上行"的版本（金币与共享仓库都在服务器上）。
            m_MerchantLink.InstallCommandHandlers(m_CommandRouter);

            m_NetworkClient = session.Network;
            m_NetworkClient.OnClientDisconnectCallback += OnSafeHouseDisconnected;

            if (m_NetworkClient.IsConnectedClient)
            {
                AttachSafeHouseToServer(session.LocalClientId);
            }
            else
            {
                m_NetworkClient.OnClientConnectedCallback += OnSafeHouseConnected;
                Debug.Log("[联机] 安全屋已就绪，等待大厅会话连接完成。");
            }

            return true;
        }

        /// <summary>退订连接回调并释放移动链路（场景销毁时调用）。</summary>
        private void DetachMultiplayer()
        {
            if (m_NetworkClient != null)
            {
                m_NetworkClient.OnClientConnectedCallback -= OnSafeHouseConnected;
                m_NetworkClient.OnClientDisconnectCallback -= OnSafeHouseDisconnected;
            }

            m_MovementLink?.Detach();
            m_MovementLink = null;

            m_ContainerLink?.Detach();
            m_ContainerLink = null;

            m_MerchantLink?.Detach();
            m_MerchantLink = null;

            m_NetworkClient = null;
        }

        /// <summary>连上服务器之后挂上移动链路。</summary>
        private void OnSafeHouseConnected(ulong clientId)
        {
            AttachSafeHouseToServer((int)clientId);
        }

        /// <summary>挂上移动链路：登记本机编号并订阅快照。</summary>
        private void AttachSafeHouseToServer(int playerId)
        {
            Debug.Log($"[联机] 安全屋已接管连接，玩家标识 {playerId}。");
            AlignSafeHouseSpawnToServerLayout(playerId);
            m_MovementLink?.Attach(m_NetworkClient, playerId);
            m_ContainerLink?.Attach(m_NetworkClient);
            m_MerchantLink?.Attach(m_NetworkClient);
        }

        /// <summary>
        /// 进屋前把本地预测位置对齐到服务器将要采用的安全屋出生排布。
        /// </summary>
        /// <remarks>
        /// 与战局是同一条规则、同一类缺陷（M13-22）：四个人在服务器上以 1.4 米间距左右摊开，
        /// 客户端若从场景的单点出生等快照，就会有 0.7~2.1 米的可见硬吸附。
        /// </remarks>
        private void AlignSafeHouseSpawnToServerLayout(int playerId)
        {
            if (m_MoveHandler == null || m_PlayerMotor == null)
            {
                return;
            }

            var spawn = PlayerSpawnLayout.SafeHousePosition(
                new Vector2F(m_PlayerSpawnPosition.x, m_PlayerSpawnPosition.y),
                playerId,
                LobbyLimits.MaxPlayers);

            var snapshot = m_MoveHandler.Simulator.CaptureSnapshot();
            snapshot.State.Position = spawn;
            m_MoveHandler.Simulator.RestoreSnapshot(snapshot);

            var facing = new Vector2(snapshot.State.Facing.X, snapshot.State.Facing.Y);
            m_PlayerMotor.SnapTo(new Vector2(spawn.X, spawn.Y), facing);
            m_CameraController?.SetTarget(m_PlayerMotor.transform, snap: true);
        }

        /// <summary>与服务器断开：清掉远端玩家，避免留下不会动的假队友。</summary>
        private void OnSafeHouseDisconnected(ulong clientId)
        {
            Debug.LogWarning($"[联机] 安全屋与服务器断开（clientId={clientId}）。");
            m_MovementLink?.NotifyDisconnected();
        }

        /// <summary>
        /// 联机安全屋的每帧推进：固定步预测 + 上行输入 + 远端插值。
        /// </summary>
        /// <param name="deltaTime">缩放后的帧时间（表现层插值用）。</param>
        /// <param name="networkDeltaTime">未缩放的帧时间（网络节拍用）。</param>
        /// <remarks>
        /// 与战局完全同一条链路：本地模拟只在固定步里推进（<see cref="MultiplayerMovementLink"/> 内部），
        /// 因此这里**不能**再调用一次 <c>m_MoveHandler.Tick(Time.deltaTime)</c>——
        /// 那样本地会比服务器多走一段路，表现是"一直被往回拉"。
        /// </remarks>
        private void TickMultiplayerSafeHouse(float deltaTime, float networkDeltaTime)
        {
            if (m_MovementLink == null)
            {
                return;
            }

            m_MovementLink.Tick(deltaTime, networkDeltaTime, new MovementLinkIntent
            {
                Move = m_PendingMove,
                Look = m_PendingLook,
                Sprint = m_PendingSprint,

                // 安全屋没有战斗上报：试枪与靶子仍走本地表现层（§25.3）。
                TriggerHeld = false,
                ReviveHeld = false,
                NoControl = false,
            });

            // P5.5 验收辅助：-autotrade 时按节拍派发购买 / 出售 / 接任务。
            TickAutoTrade();

            UpdateMultiplayerRoomBadge();
        }

        /// <summary>
        /// 房间角标：房间名 + 人数 + 自己是不是房主。
        /// </summary>
        /// <remarks>
        /// 联机时房间界面会收起（§25.3），玩家需要一个小小的"我还在房间里"的凭据；
        /// 同时它承担门禁提示：只有房主能出击，且必须全员都在屋里。
        /// </remarks>
        private void UpdateMultiplayerRoomBadge()
        {
            var session = MultiplayerClientSession.Current;
            if (session == null || !session.SelfInRoom)
            {
                m_Ui?.HideRoomBadge();
                return;
            }

            m_Ui?.ShowRoomBadge(
                session.RoomName,
                session.RoomMembers.Count,
                LobbyLimits.MaxPlayers,
                session.IsHost,
                session.RoomPhase != LobbyPhase.Waiting);
        }

        /// <summary>本机选择的角色标识（远端玩家先用同一套外观）。</summary>
        private string ResolveLocalCharacterId()
        {
            var flow = m_Flow != null ? m_Flow : RaidFlowController.Ensure();
            return flow.Progress != null ? flow.Progress.SelectedCharacterId : null;
        }

        /// <summary>
        /// 出口被确认出击时调用：联机走服务器的门禁与开局，单机直接加载地图。
        /// </summary>
        /// <returns>已把请求交给服务器（或已开始单机出击）返回 true。</returns>
        private bool TryDeployFromSafeHouse()
        {
            if (!IsMultiplayerSafeHouse)
            {
                RaidFlowController.Ensure().StartRaid();
                return true;
            }

            var session = MultiplayerClientSession.Current;
            if (session == null)
            {
                return false;
            }

            if (!session.IsHost)
            {
                m_Ui?.ShowHint("只有房主可以选择出击地图。");
                return false;
            }

            if (session.RoomPhase != LobbyPhase.Waiting)
            {
                m_Ui?.ShowHint("队友还在战局中，等他们回到安全屋。");
                return false;
            }

            // 地图名交给服务器：服务器据此切换自己托管的世界，并广播给所有成员。
            session.StartRaid(GameScenes.DefaultRaid);
            return true;
        }
    }
}
