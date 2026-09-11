namespace RaidDemo.Shared
{
    /// <summary>
    /// 玩家切换武器意图：在主武器与副武器之间切换。
    /// </summary>
    /// <remarks>
    /// <para>命令携带一个方向值（滚轮上滚为 +1、下滚为 -1），而不是"切到第几号武器"。
    /// 当前只有两个武器槽，两个方向的效果相同；保留方向是为了将来扩充到多个槽位时
    /// 不必改命令契约——与 <see cref="IGameCommand"/> 的序号字段同样的思路：
    /// 现在用不到，但改起来很贵。</para>
    /// <para>与其它命令一样，它只表达意图。具体切到哪把、切换是否合法，
    /// 由权威侧根据装备槽的当前内容决定。</para>
    /// </remarks>
    public readonly struct PlayerSwitchWeaponIntent : IGameCommand
    {
        /// <summary>命令类型标识。</summary>
        public const string TypeId = "player.switch_weapon";

        /// <summary>创建切换武器意图。</summary>
        /// <param name="playerId">发起玩家。</param>
        /// <param name="direction">切换方向，+1 为下一个，-1 为上一个。</param>
        /// <param name="sequence">命令序号。</param>
        /// <param name="timestamp">发起时刻（秒）。</param>
        public PlayerSwitchWeaponIntent(
            int playerId,
            int direction = 1,
            uint sequence = 0u,
            double timestamp = 0d)
        {
            PlayerId = playerId;
            Direction = direction >= 0 ? 1 : -1;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <inheritdoc />
        public string CommandType
        {
            get { return TypeId; }
        }

        /// <inheritdoc />
        public int PlayerId { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <summary>切换方向：+1 为下一个，-1 为上一个。</summary>
        public int Direction { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PlayerSwitchWeaponIntent(P{PlayerId}, 方向={Direction})";
        }
    }
}
