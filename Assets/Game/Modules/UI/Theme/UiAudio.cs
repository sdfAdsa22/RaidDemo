namespace RaidDemo.UI
{
    /// <summary>
    /// 界面音效的语义分类。
    /// </summary>
    /// <remarks>
    /// 界面代码只说"这是一次确认"或"这是一次面板打开"，不持有任何 <c>AudioClip</c>。
    /// 具体播放哪条剪辑由启动层的音效导演决定，这样换素材不需要改几十个界面文件。
    /// </remarks>
    public enum UiCue
    {
        /// <summary>普通按钮点击。</summary>
        Click = 0,

        /// <summary>面板打开。</summary>
        PanelOpen = 1,

        /// <summary>面板关闭。</summary>
        PanelClose = 2,

        /// <summary>页签切换。</summary>
        TabSwitch = 3,

        /// <summary>确认、出击、购买成功等重要正向操作。</summary>
        Confirm = 4,

        /// <summary>取消、关闭菜单等中性回退操作。</summary>
        Cancel = 5,

        /// <summary>点击未开放内容、操作被拒绝。</summary>
        Locked = 6,

        /// <summary>购买成功。</summary>
        Buy = 7,
    }

    /// <summary>
    /// 界面音效的接收端。
    /// </summary>
    /// <remarks>
    /// UI 程序集不能引用 Bootstrap，因此这里只定义"谁来播"的抽象；
    /// 启动层的 <c>GameAudioDirector</c> 在绑定音频服务时把自己注册成接收端。
    /// </remarks>
    public interface IUiAudioSink
    {
        /// <summary>播放一个界面音效。</summary>
        /// <param name="cue">音效语义。</param>
        void PlayUiCue(UiCue cue);
    }

    /// <summary>
    /// 界面音效的静态出口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是静态入口：</b>界面在运行时由代码构建，按钮、拖拽、页签分散在十几个类里；
    /// 如果每个类都通过构造函数注入音效服务，会把纯布局代码全部染上依赖。
    /// 静态入口只保留一个接收端引用，装配层设一次，界面代码只写 <c>UiAudio.Play(...)</c>。</para>
    /// <para>接收端为空时静默跳过：主菜单、测试场景或音频服务尚未绑定时，
    /// 界面仍然要能正常打开与点击，不能因为缺音频而报错。</para>
    /// </remarks>
    public static class UiAudio
    {
        private static IUiAudioSink s_Sink;

        /// <summary>设置当前场景的界面音效接收端。</summary>
        public static void SetSink(IUiAudioSink sink)
        {
            s_Sink = sink;
        }

        /// <summary>
        /// 清除接收端。
        /// </summary>
        /// <remarks>只有传入的实例仍是当前接收端时才清除，避免旧场景销毁时误伤新场景。</remarks>
        public static void ClearSink(IUiAudioSink sink)
        {
            if (object.ReferenceEquals(s_Sink, sink))
            {
                s_Sink = null;
            }
        }

        /// <summary>播放一个界面音效；没有接收端时静默跳过。</summary>
        public static void Play(UiCue cue)
        {
            s_Sink?.PlayUiCue(cue);
        }
    }
}
