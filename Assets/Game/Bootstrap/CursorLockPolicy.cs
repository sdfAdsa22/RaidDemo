namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 光标锁定策略。
    /// </summary>
    /// <remarks>
    /// <para>这条规则曾经分散在安全屋与战局两处，条件写法不同：
    /// 一处只看"背包有没有开"，另一处只看"玩家死没死"。
    /// 结果是主菜单与结算界面这些**需要鼠标的流程状态**被当成战局操作状态，
    /// 每一帧都把光标锁回去。</para>
    ///
    /// <para>现在只保留一个判断：<b>只有既没有界面、当前流程也不需要鼠标时，才锁定光标。</b>
    /// 主菜单与结算属于"需要鼠标"的状态，永远不锁；背包、商人、地图面板属于"界面打开"，
    /// 打开期间也不锁。</para>
    /// </remarks>
    public static class CursorLockPolicy
    {
        /// <summary>判断当前是否应该锁定并隐藏光标。</summary>
        /// <param name="uiOpen">是否有需要鼠标的界面正在显示。</param>
        /// <param name="stateNeedsMouse">当前流程状态是否本身就需要鼠标（主菜单 / 结算）。</param>
        public static bool ShouldLockCursor(bool uiOpen, bool stateNeedsMouse)
        {
            return !uiOpen && !stateNeedsMouse;
        }
    }
}
