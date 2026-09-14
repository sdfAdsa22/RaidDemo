namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 场景名常量：所有"谁该加载哪张图"的判断都从这里取名字。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须集中：</b>P4.5-b 之后，场景名不再只出现在客户端流程里——
    /// 服务器要托管安全屋、要在"安全屋 ⇄ 战局"之间切换，客户端要跟着服务器的通知切图。
    /// 任何一处拼写不同，表现都是"服务器切过去了、客户端还站在原地"这类极难对齐的故障。</para>
    ///
    /// <para>名字必须与 Build Settings 里的场景名完全一致（见 <c>ProjectSettings/EditorBuildSettings.asset</c>）。</para>
    /// </remarks>
    public static class GameScenes
    {
        /// <summary>安全屋：局外空间，也是联机进房之后的第一站。</summary>
        public const string SafeHouse = "SafeHouse";

        /// <summary>默认战局地图（工业区 + 集装箱仓库）。</summary>
        public const string DefaultRaid = "GreyboxRaid";
    }
}
