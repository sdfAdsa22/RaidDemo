namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 被服务器强制移出房间时的判定规则（纯逻辑，可单测）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独抽出来：</b>"要不要把玩家甩回主菜单"这件事有两个入口——
    /// ① 服务器直接发来的 <see cref="LobbyError.KickedOut"/> 结果；
    /// ② 房间广播显示"房间已经没了、自己也不在名单里"（解散房间时结果消息可能先到，也可能广播先到）。
    /// 两个入口必须给出同一个结论，否则会出现"一个人回主菜单、另一个人卡在战局里"的分裂行为。
    /// 判定只看三个输入，因此可以在编辑模式里把每种组合都钉住。</para>
    /// </remarks>
    public static class MultiplayerKickRules
    {
        /// <summary>
        /// 收到房间广播时，是否需要强制返回（解散房间的战局兜底）。
        /// </summary>
        /// <param name="selfInRoom">自己在最新名单里吗。</param>
        /// <param name="roomPhase">房间阶段（<see cref="LobbyPhase.Empty"/> 表示房间已经不存在）。</param>
        /// <param name="phase">客户端自身阶段。</param>
        /// <returns>需要强制返回时为 true。</returns>
        /// <remarks>
        /// 只在"自己在战局里、房间已经没了"时成立：
        /// 大厅里的正常退房走的是既有 LeaveRoom 路径（阶段还会被界面逻辑对齐），
        /// 战局里的玩家没有那条路径，不处理就会永远停在战局场景里。
        /// </remarks>
        public static bool ShouldForceOutFromRaid(bool selfInRoom, LobbyPhase roomPhase, MultiplayerClientPhase phase)
        {
            return !selfInRoom
                   && roomPhase == LobbyPhase.Empty
                   && phase == MultiplayerClientPhase.InRaid;
        }
    }
}
