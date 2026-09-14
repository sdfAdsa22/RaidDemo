using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的移动部分：意图清理与"单机 / 联机"两条推进路径的分派辅助。
    /// </summary>
    /// <remarks>
    /// <para>从主文件拆出来的原因：主文件同时承担"输入、相机、流程状态、界面开合"，
    /// 再放移动辅助就会顶到 400 行上限；而"这一帧怎么推进移动"本来就是可以单独读的一小块。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        /// <summary>
        /// 清空移动意图（打开背包 / 商人 / 说明板时调用）。
        /// </summary>
        /// <remarks>
        /// <para><b>本地模拟与"待上行意图"必须一起清：</b>联机的上行用的是
        /// <c>m_PendingMove</c> 这几个字段，只清本地意图的话，服务器仍会按上一帧的方向移动玩家——
        /// 客户端原地不动、每次快照把它往前拉一下，表现是"翻着背包人还在往前走"。
        /// 战局侧走的是同一条规则（见 <c>SceneBootstrap.ClearLocalMovementIntent</c>）。</para>
        ///
        /// <para>朝向不清：那只是"看着哪"，界面打开时保持朝向不会让角色移动，
        /// 而且枪口继续跟着光标更自然。</para>
        /// </remarks>
        private void ClearMovementIntent()
        {
            m_MoveHandler?.ClearIntent();
            m_PendingMove = Vector2F.Zero;
            m_PendingSprint = false;
        }
    }
}
