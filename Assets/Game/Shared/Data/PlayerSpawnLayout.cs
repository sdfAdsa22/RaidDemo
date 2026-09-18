namespace RaidDemo.Shared
{
    /// <summary>
    /// 联机出生点的排布规则：服务器按玩家编号错开出生点，客户端必须在接管连接前算出同一位置。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-22"一进屋 / 一进图被拉一下"。服务器为了避免所有玩家
    /// 叠在同一格，会按玩家编号把出生点错开；客户端如果仍站在场景里的单个基准出生点上等
    /// 第一帧权威快照，就会与服务器差出 0.7~4.8 米，对账只能硬吸附，玩家看到的就是一次可见回滚。</para>
    ///
    /// <para><b>为什么放在 Shared：</b>规则必须只有一份实现。写在服务器里、客户端再抄一遍，
    /// 下次改间距或改基准点时两侧就会分叉——这类"只是挪了一下坐标"的改动，表现却是一次瞬移。
    /// Shared 被服务器与两个场景的客户端共同引用，正好是它的归属层。</para>
    /// </remarks>
    public static class PlayerSpawnLayout
    {
        /// <summary>战局出生点间距（米）：玩家按编号先横向四列、再换行的网格排开。</summary>
        public const float RaidSpacing = 1.6f;

        /// <summary>安全屋出生点间距（米）：比玩家胶囊直径（0.8 米）宽，避免移动扫掠互推。</summary>
        public const float SafeHouseSpacing = 1.4f;

        /// <summary>
        /// 战局出生位置：以基准点为原点，按编号先横后纵排成网格。
        /// </summary>
        /// <param name="basePosition">出生基准点；服务器的常量与客户端场景字段必须一致。</param>
        /// <param name="playerId">玩家编号；负数按 0 处理。</param>
        public static Vector2F RaidPosition(Vector2F basePosition, int playerId)
        {
            var index = playerId < 0 ? 0 : playerId;
            return new Vector2F(
                basePosition.X + ((index % 4) * RaidSpacing),
                basePosition.Y + ((index / 4) * RaidSpacing));
        }

        /// <summary>
        /// 安全屋出生位置：以基准点为中轴左右摊开，四人时为 -2.1 / -0.7 / +0.7 / +2.1 米。
        /// </summary>
        /// <param name="basePosition">安全屋场景里的出生点。</param>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="maxPlayers">房间人数上限，决定中轴位置。</param>
        public static Vector2F SafeHousePosition(Vector2F basePosition, int playerId, int maxPlayers)
        {
            var limit = maxPlayers < 1 ? 1 : maxPlayers;
            var index = playerId < 0 ? 0 : (playerId > limit - 1 ? limit - 1 : playerId);
            var center = (limit - 1) * 0.5f;
            var offset = (index - center) * SafeHouseSpacing;
            return new Vector2F(basePosition.X + offset, basePosition.Y);
        }
    }
}
