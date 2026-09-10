namespace RaidDemo.Shared
{
    /// <summary>
    /// 玩家移动意图：请求把角色朝某个方向移动。
    /// </summary>
    /// <remarks>
    /// <para>命令表达的是意图而不是结果。这里传递方向与姿态，而不是目标位置。
    /// 具体速度、加速曲线、碰撞推挤，全部由权威侧根据当前状态计算。
    /// 若命令直接携带目标位置，客户端就等于拥有了瞬移能力，服务端将无法信任它。</para>
    ///
    /// <para>方向向量不要求归一化：手柄摇杆的模拟输入依靠长度表达推行力度，
    /// 处理逻辑会自行限制长度上限。</para>
    /// </remarks>
    public readonly struct PlayerMoveIntent : IGameCommand
    {
        /// <summary>命令类型标识。命名规则为 模块.动作。</summary>
        public const string TypeId = "player.move";

        /// <summary>创建移动意图。</summary>
        /// <param name="playerId">发起玩家。</param>
        /// <param name="moveDirection">移动方向，长度原则上不超过 1。</param>
        /// <param name="lookDirection">角色面向方向（单位向量），用于把朝向与移动方向解耦。</param>
        /// <param name="sequence">命令序号。</param>
        /// <param name="timestamp">发起时刻（秒）。</param>
        public PlayerMoveIntent(
            int playerId,
            Vector2F moveDirection,
            Vector2F lookDirection,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            MoveDirection = moveDirection;
            LookDirection = lookDirection;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <inheritdoc />
        public string CommandType => TypeId;

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <summary>移动方向。零向量表示松开移动键。</summary>
        public Vector2F MoveDirection { get; }

        /// <summary>面向方向。零向量表示保持当前朝向不变。</summary>
        public Vector2F LookDirection { get; }

        public override string ToString()
        {
            return $"PlayerMoveIntent(P{PlayerId}, move={MoveDirection}, look={LookDirection}, seq={Sequence})";
        }
    }

    /// <summary>
    /// 玩家开火意图：请求使用当前武器射击一次。
    /// </summary>
    /// <remarks>
    /// <para>开火判定只在服务端执行。客户端发送的是我想开枪，而不是我打中了谁。
    /// 命中判定所需的射线检测、伤害结算、护甲穿透全部发生在权威侧，
    /// 客户端只负责播放表现。</para>
    ///
    /// <para>这条规则消除了射击游戏中最常见的作弊面。如果客户端能自行宣布命中，
    /// 那么任何修改客户端的玩家都可以做到百发百中。</para>
    /// </remarks>
    public readonly struct PlayerFireIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "player.fire";

        /// <summary>创建开火意图。</summary>
        public PlayerFireIntent(
            int playerId,
            Vector2F aimDirection,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            AimDirection = aimDirection;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <inheritdoc />
        public string CommandType => TypeId;

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <summary>
        /// 瞄准方向（单位向量）。
        /// </summary>
        /// <remarks>
        /// 服务端仍会校验该方向与角色当前朝向的夹角是否在允许范围内，
        /// 避免客户端提交一个与实际朝向完全不符的方向来打到视野外的目标。
        /// </remarks>
        public Vector2F AimDirection { get; }

        public override string ToString()
        {
            return $"PlayerFireIntent(P{PlayerId}, aim={AimDirection}, seq={Sequence})";
        }
    }

    /// <summary>
    /// 玩家拾取意图：请求把指定容器中的某个物品放入自己的背包。
    /// </summary>
    /// <remarks>
    /// 命令只携带定位所需的信息（容器标识与格子坐标），不携带物品数据本身。
    /// 服务端据此在权威数据中查找物品并执行规则校验，
    /// 因此客户端无法凭空构造一个物品递给服务端。
    /// </remarks>
    public readonly struct PlayerPickupIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "player.pickup";

        /// <summary>创建拾取意图。</summary>
        public PlayerPickupIntent(
            int playerId,
            int containerId,
            int cellX,
            int cellY,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            ContainerId = containerId;
            CellX = cellX;
            CellY = cellY;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <inheritdoc />
        public string CommandType => TypeId;

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <summary>来源容器的运行时标识。</summary>
        public int ContainerId { get; }

        /// <summary>物品在容器中的格子横坐标。</summary>
        public int CellX { get; }

        /// <summary>物品在容器中的格子纵坐标。</summary>
        public int CellY { get; }

        public override string ToString()
        {
            return $"PlayerPickupIntent(P{PlayerId}, container={ContainerId}, cell=({CellX},{CellY}))";
        }
    }
}
