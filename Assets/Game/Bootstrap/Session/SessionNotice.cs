namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 跨场景传递一次性的会话提示（目前用于"被管理员移出房间"）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>被踢的瞬间玩家可能正在战局里——处理方式是断开联机、重载安全屋回到主菜单，
    /// 而 <c>RaidFlowController</c> 与主菜单都是随场景重建的，本地的提示字段撑不过场景切换。
    /// 静态槽让"设置提示"与"显示提示"可以发生在不同的场景生命周期里。</para>
    ///
    /// <para><b>只显示一次：</b><see cref="Consume"/> 读取即清空，避免玩家正常进出主菜单时旧提示反复出现。</para>
    ///
    /// <para>线程约束：只在主线程使用（与项目其余部分一致）。</para>
    /// </remarks>
    public static class SessionNotice
    {
        private static string s_Pending;

        /// <summary>写入一条待显示提示（覆盖上一条）。</summary>
        /// <param name="text">提示文本；空文本等价于清除。</param>
        public static void Set(string text)
        {
            s_Pending = string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>取出并清空待显示提示；没有时返回 null。</summary>
        public static string Consume()
        {
            var text = s_Pending;
            s_Pending = null;
            return text;
        }
    }
}
