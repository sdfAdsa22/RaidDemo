using System.Collections.Generic;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using RaidDemo.Simulation;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的移动权威部分：生成玩家对象、消费输入、推进世界、广播快照。
    /// </summary>
    /// <remarks>
    /// <para><b>它把三层接在一起：</b>网络层（收到谁的什么输入）→ 模拟层（权威世界怎么推进）
    /// → 网络层（把结果广播出去）。每一层都各自独立可测，这里只做搬运与生命周期管理。</para>
    ///
    /// <para><b>玩家对象的作用：</b>一是承载玩家身份（<see cref="PlayerNetworkAgent"/>），
    /// 二是给移动碰撞提供一个"从哪出发做胶囊扫掠"的载体。服务器不渲染它，
    /// 但它的 Transform 必须跟着权威位置走，否则碰撞判定会从错误的位置发出。</para>
    ///
    /// <para><b>P1 的已知简化：</b>玩家之间不做碰撞（P1 的玩家对象不带碰撞体），
    /// 因此两名玩家可以走到同一格上。玩家互推属于 P3 的交互范围。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>相邻出生点的间距（米），避免所有人叠在同一格。</summary>
        private const float SpawnSpacing = 1.6f;

        /// <summary>移动体高度（米）。与场景生成器里的胶囊保持一致。</summary>
        private const float BodyHeight = 1.8f;

        /// <summary>移动体半径（米）。与客户端玩家的胶囊保持一致，命中判定才一致。</summary>
        private const float BodyRadius = 0.4f;

        /// <summary>碰撞扫掠的安全边距（米）。</summary>
        private const float CollisionSkin = 0.02f;

        private MovementServerWorld m_World;
        private readonly List<PlayerSnapshot> m_SnapshotBuffer = new List<PlayerSnapshot>();
        private readonly Dictionary<int, Transform> m_PlayerBodies = new Dictionary<int, Transform>();
        private readonly Dictionary<int, GameObject> m_PlayerColliders = new Dictionary<int, GameObject>();
        private readonly HashSet<int> m_InputLogged = new HashSet<int>();

        /// <summary>每名玩家上一次采样到的地面高度（探测无状态，但"没命中就沿用上次"要有人记）。</summary>
        private readonly Dictionary<int, float> m_PlayerGroundHeights = new Dictionary<int, float>();

        /// <summary>服务器侧的移动权威世界。供测试与调试读取。</summary>
        public MovementServerWorld World => m_World;

        /// <summary>建立权威世界并接上连接事件。</summary>
        private void InitializeMovement()
        {
            m_World = new MovementServerWorld(collisionFactory: CreateCollisionWorld);

            // 接入事件由大厅处理（P4）：连上不等于进图，只有开局才把人放进权威世界。
            m_Network.OnClientDisconnectCallback += OnClientDisconnected;

            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                MovementNetworkChannel.InputMessageName,
                OnInputMessageReceived);

            // 容器内容请求通道（服务器主动推的那次可能早于客户端就绪，见 P-20）。
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                ContainerNetworkChannel.RequestMessageName,
                OnContainerContentsRequested);

            // 背包操作上行（P3-2）与重开战局请求（P3 的"战局可重开"）。
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                ContainerNetworkChannel.CommandMessageName,
                OnInventoryMoveCommandReceived);
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                ContainerNetworkChannel.RestartMessageName,
                OnRaidRestartRequested);
            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                ContainerNetworkChannel.EquipCommandMessageName,
                OnInventoryEquipCommandReceived);

            m_Session.Log.Info("[服务器] 移动权威世界已就绪（60 Hz 仿真 / 20 Hz 快照）。");

            if (!string.IsNullOrEmpty(m_Options.MapSceneName))
            {
                LoadMapScene(m_Options.MapSceneName);
            }
        }

        /// <summary>断开连接事件并丢弃权威世界。</summary>
        private void ShutdownMovement()
        {
            if (m_Network != null)
            {
                m_Network.OnClientDisconnectCallback -= OnClientDisconnected;

                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    MovementNetworkChannel.InputMessageName);
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    ContainerNetworkChannel.RequestMessageName);
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    ContainerNetworkChannel.CommandMessageName);
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    ContainerNetworkChannel.RestartMessageName);
                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    ContainerNetworkChannel.EquipCommandMessageName);
            }

            m_PlayerBodies.Clear();
            m_PlayerColliders.Clear();
            m_PlayerGroundHeights.Clear();
            m_SnapshotBuffer.Clear();
            ShutdownAi();
            ShutdownCombat();
            m_World = null;
        }

        /// <summary>收到客户端输入的命名消息。</summary>
        private void OnInputMessageReceived(ulong senderId, FastBufferReader reader)
        {
            var message = default(PlayerInputMessage);
            reader.ReadValueSafe(out message);
            HandleClientInput(senderId, message);
        }

        /// <summary>
        /// 加载服务器侧地图场景。
        /// </summary>
        /// <remarks>
        /// 服务器需要地图的碰撞体（以及 P2 起的导航数据），但它不装配客户端世界——
        /// 场景里的装配根会在服务器模式下自行退出（见 <see cref="ServerMode"/>）。
        /// </remarks>
        private void LoadMapScene(string sceneName)
        {
            if (SceneManager.GetActiveScene().name == sceneName)
            {
                return;
            }

            m_Session.Log.Info($"[服务器] 加载地图场景：{sceneName}");
            SceneManager.LoadScene(sceneName);
        }

        /// <summary>
        /// 处理一条来自客户端的输入。
        /// </summary>
        /// <remarks>
        /// 被拒绝是常态（重复包、乱序包），因此用 Verbose 级别记录，避免正常网络上刷屏。
        /// </remarks>
        internal void HandleClientInput(ulong clientId, in PlayerInputMessage message)
        {
            if (m_World == null)
            {
                return;
            }

            var intent = new PlayerMoveIntent(
                (int)clientId,
                new Vector2F(message.Move.x, message.Move.y),
                new Vector2F(message.Look.x, message.Look.y),
                message.Sprint,
                message.Sequence,
                message.Timestamp);

            // 倒地的人不能移动、也不能开枪：只接受"扶起队友"这一个意图。
            SetReviveHeld((int)clientId, message.ReviveHeld);
            if (IsPlayerDowned((int)clientId))
            {
                return;
            }

            if (m_InputLogged.Add((int)clientId) && m_Session != null
                && m_Session.Log.IsEnabled(RaidDemo.Kernel.LogLevel.Verbose))
            {
                // 联机排障时，"上行消息里到底有什么"是第一手证据：
                // 它能一次区分"客户端没发""发的是空值"与"发了但服务器没按预期处理"。
                m_Session.Log.Verbose(
                    $"[服务器] 收到玩家 {(int)clientId} 的首条输入：" +
                    $"移动=({message.Move.x:F2},{message.Move.y:F2}) " +
                    $"朝向=({message.Look.x:F2},{message.Look.y:F2}) " +
                    $"奔跑={message.Sprint} 扳机={message.TriggerHeld} 换弹={message.ReloadRequested} 序号={message.Sequence}");
            }

            if (!m_World.TrySubmitInput(intent, out var rejection))
            {
                m_Session?.Log.Verbose($"[服务器] {rejection}");
            }

            // 同一条输入同时驱动移动与战斗：朝向既是"看哪"也是"打哪"。
            ApplyCombatInput((int)clientId, message, new Vector2F(message.Look.x, message.Look.y));
        }

        /// <summary>每帧推进：仿真 → 同步载体位置 → 到节拍就广播快照。</summary>
        private void TickMovement(float deltaTime)
        {
            if (m_World == null)
            {
                return;
            }

            m_World.Advance(deltaTime);
            TickCombat(deltaTime);
            SyncPlayerTransforms();

            if (m_World.CaptureSnapshots(m_SnapshotBuffer) > 0)
            {
                BroadcastSnapshots();

                // 敌人快照与玩家快照同一节拍：两者在客户端是同一帧被插值的，
                // 节拍不同会让"敌人打中了队友"这类事件在两边的画面上错开半拍。
                BroadcastEnemySnapshots();
            }
        }

        /// <summary>
        /// 把权威位置写回玩家对象的 Transform。
        /// </summary>
        /// <remarks>
        /// <para>碰撞扫掠从 Transform 出发，因此它必须与模拟位置一致。</para>
        ///
        /// <para><b>高度每帧重采：</b>模拟层只有平面坐标，而"这个位置的地面有多高"属于场景信息。
        /// 地图是下沉盆地（谷底 -6、平台 -4.8、塬面 0），写死高度会让胶囊浮在地面上方 6 米——
        /// 于是服务器认为玩家可以穿过集装箱，子弹也会从掩体上方飞过。
        /// 这里用与玩家表现层同一套规则的地面探测，把胶囊始终贴在真实地面上。</para>
        /// </remarks>
        private void SyncPlayerTransforms()
        {
            foreach (var pair in m_PlayerBodies)
            {
                var body = pair.Value;
                if (body == null || !m_World.TryGetSnapshot(pair.Key, out var snapshot))
                {
                    continue;
                }

                var position = snapshot.State.Position;
                var plane = new Vector2F(position.X, position.Y);
                var ground = GroundProbe.SampleGroundHeight(
                    plane,
                    body.position.y,
                    m_PlayerGroundHeights.TryGetValue(pair.Key, out var previous) ? previous : body.position.y,
                    body);

                m_PlayerGroundHeights[pair.Key] = ground;
                body.position = new Vector3(position.X, ground, position.Y);
            }
        }

        /// <summary>把这一批快照广播给所有客户端。</summary>
        private void BroadcastSnapshots()
        {
            var manager = m_Network;
            if (manager == null || manager.CustomMessagingManager == null)
            {
                return;
            }

            // 没有客户端时直接返回：NGO 在无接收者时发送命名消息会抛异常。
            if (manager.ConnectedClientsIds.Count == 0)
            {
                return;
            }

            var players = new PlayerStateMessage[m_SnapshotBuffer.Count];
            for (var i = 0; i < players.Length; i++)
            {
                players[i] = PlayerStateMessage.From(m_SnapshotBuffer[i]);
            }

            var batch = new PlayerSnapshotBatchMessage
            {
                ServerTime = m_World.SimulationTime,
                Players = players,
            };

            using (var writer = new FastBufferWriter(64 + (players.Length * 64), Allocator.Temp))
            {
                writer.WriteValueSafe(batch);
                manager.CustomMessagingManager.SendNamedMessageToAll(
                    MovementNetworkChannel.SnapshotMessageName,
                    writer);
            }
        }

        /// <summary>
        /// 为玩家创建一个"载体"对象。
        /// </summary>
        /// <remarks>
        /// 它只是一个普通 GameObject：服务器不渲染任何东西，但移动碰撞需要一个
        /// "从哪出发做胶囊扫掠"的坐标载体。P2 引入玩家网络对象后，这个载体就是网络对象的根节点。
        /// </remarks>
        private void CreatePlayerBody(int playerId)
        {
            var position = SpawnPositionFor(playerId);

            // 出生高度取导航网格采样：此时服务器已经烘焙过导航数据，它是"地面在哪"最省事的权威答案。
            // 找不到导航数据时退回 0 米；随后的每帧地面探测会把胶囊修正到真实地面。
            var ground = NavMeshGroundSampler.TrySample(position, out var sampled) ? sampled : 0f;

            // 已经有载体就**复用**它，只把位置与地面高度更新一遍：
            // 重复创建会留下一个"没人认领的胶囊"——它还带着旧的目标编号待在场景里，
            // 射线会先撞到它、把命中判定引到一个已经退场的战斗单位上（P4 踩过：见排障手册 P-34）。
            if (m_PlayerColliders.TryGetValue(playerId, out var existing) && existing != null)
            {
                existing.transform.position = new Vector3(position.X, ground, position.Y);
                m_PlayerBodies[playerId] = existing.transform;
                m_PlayerGroundHeights[playerId] = ground;
                return;
            }

            var instance = new GameObject($"ServerPlayerBody_{playerId}");
            instance.transform.position = new Vector3(position.X, ground, position.Y);
            m_PlayerGroundHeights[playerId] = ground;

            // 高度对齐是"静默出错"的重灾区：胶囊浮在地面上方时，服务器照常接受输入、
            // 照常广播快照，只有"子弹穿掩体""玩家穿集装箱"这类间接症状。
            // 因此每次有人加入都留一条 Info 痕迹，让这个数字在任何构建里都查得到。
            m_Session?.Log.Info(
                $"[服务器] 玩家 {playerId} 的碰撞载体就位：平面 ({position.X:F1}, {position.Y:F1})，"
                + $"地面高度 {ground:F2} 米。");

            // 形状与客户端角色一致：命中判定用的是同一套胶囊尺寸，
            // 否则"客户端觉得打中了、服务器觉得没中"会变成常态。
            var collider = instance.AddComponent<CapsuleCollider>();
            collider.height = BodyHeight;
            collider.radius = BodyRadius;
            collider.center = new Vector3(0f, BodyHeight * 0.5f, 0f);

            // 放到单位层：命中射线的遮罩按层过滤，放错层就永远打不中。
            PhysicsLayers.ApplyUnitLayer(instance);

            m_PlayerBodies[playerId] = instance.transform;
            m_PlayerColliders[playerId] = instance;
        }

        /// <summary>
        /// <summary>
        /// 为玩家创建碰撞世界。
        /// </summary>
        /// <remarks>
        /// 找不到载体时返回 null：移动会退化为"无碰撞"，但服务器仍然可用——
        /// 宁可少一个修正，也不要让整台服务器起不来。
        /// </remarks>
        private IMovementCollisionWorld CreateCollisionWorld(int playerId)
        {
            return m_PlayerBodies.TryGetValue(playerId, out var body) && body != null
                ? new PhysicsMovementCollisionService(body, BodyHeight, CollisionSkin)
                : null;
        }
    }
}
