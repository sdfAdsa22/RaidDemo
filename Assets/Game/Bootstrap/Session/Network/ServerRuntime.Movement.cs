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

        /// <summary>服务器侧的移动权威世界。供测试与调试读取。</summary>
        public MovementServerWorld World => m_World;

        /// <summary>建立权威世界并接上连接事件。</summary>
        private void InitializeMovement()
        {
            m_World = new MovementServerWorld(collisionFactory: CreateCollisionWorld);

            m_Network.OnClientConnectedCallback += OnClientConnected;
            m_Network.OnClientDisconnectCallback += OnClientDisconnected;

            m_Network.CustomMessagingManager.RegisterNamedMessageHandler(
                MovementNetworkChannel.InputMessageName,
                OnInputMessageReceived);

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
                m_Network.OnClientConnectedCallback -= OnClientConnected;
                m_Network.OnClientDisconnectCallback -= OnClientDisconnected;

                m_Network.CustomMessagingManager?.UnregisterNamedMessageHandler(
                    MovementNetworkChannel.InputMessageName);
            }

            m_PlayerBodies.Clear();
            m_PlayerColliders.Clear();
            m_SnapshotBuffer.Clear();
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

        /// <summary>有新客户端接入：生成玩家对象并把它加入权威世界。</summary>
        private void OnClientConnected(ulong clientId)
        {
            if (m_World == null)
            {
                m_Session?.Log.Warning($"[服务器] 权威世界未就绪，连接 {clientId} 未加入。");
                return;
            }

            var playerId = (int)clientId;
            CreatePlayerBody(playerId);

            var position = SpawnPositionFor(playerId);
            if (!m_World.TryAddPlayer(playerId, position, Vector2F.Up, out var error))
            {
                m_Session?.Log.Warning($"[服务器] 玩家 {playerId} 加入权威世界失败：{error}");
                return;
            }

            m_Session?.Log.Info($"[服务器] 玩家 {playerId} 已加入（在线 {m_World.PlayerCount} 人）。");
            AddPlayerToCombat(playerId);
            BindPlayerHitTarget(playerId);
        }

        /// <summary>客户端断开：从权威世界移除。</summary>
        private void OnClientDisconnected(ulong clientId)
        {
            var playerId = (int)clientId;
            m_PlayerBodies.Remove(playerId);
            RemovePlayerFromCombat(playerId);

            if (m_World != null && m_World.RemovePlayer(playerId))
            {
                m_Session?.Log.Info($"[服务器] 玩家 {playerId} 已移除（在线 {m_World.PlayerCount} 人）。");
            }
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
            }
        }

        /// <summary>
        /// 把权威位置写回玩家对象的 Transform。
        /// </summary>
        /// <remarks>
        /// 碰撞扫掠从 Transform 出发，因此它必须与模拟位置一致；
        /// 高度保持不变（俯视角下移动只发生在水平面）。
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
                body.position = new Vector3(position.X, body.position.y, position.Y);
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
            var instance = new GameObject($"ServerPlayerBody_{playerId}");
            var position = SpawnPositionFor(playerId);
            instance.transform.position = new Vector3(position.X, 0f, position.Y);

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
        /// 按玩家标识排布出生点。
        /// </summary>
        /// <remarks>
        /// 每个人都叠在地图原点会让第一帧看起来像只有一个角色。P1 用一圈小队列把玩家排开，
        /// 真正的出生点由战局配置决定（P3 的生成管理）。
        /// </remarks>
        private static Vector2F SpawnPositionFor(int playerId)
        {
            var index = playerId < 0 ? 0 : playerId;
            return new Vector2F((index % 4) * SpawnSpacing, (index / 4) * SpawnSpacing);
        }

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
