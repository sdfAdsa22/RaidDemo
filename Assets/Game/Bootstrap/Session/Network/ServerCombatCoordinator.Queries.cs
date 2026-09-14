namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器战斗协调者的"查询"部分：把权威状态读出来供下行使用。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>主文件已接近 400 行上限（工程规范检查见 PathRulesTests）。
    /// 拆分按内容而不是按行数硬切：这里只有只读查询——不改变任何战斗状态；
    /// 参战、输入与推进仍留在主文件。</para>
    /// </remarks>
    public sealed partial class ServerCombatCoordinator
    {
        /// <summary>
        /// 取某名玩家当前弹匣里的弹药数。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="ammo">剩余弹药；玩家未参战或没有武器时返回 false。</param>
        /// <remarks>
        /// 事件下行（开火 / 换弹）用它把"服务器扣完弹之后的数字"带给客户端。
        /// 客户端本地不推进武器，不拿到这个值，HUD 的弹匣数永远不会变
        /// （2026-09-14 联机基础问题修复的定位结论）。
        /// </remarks>
        public bool TryGetMagazineAmmo(int playerId, out int ammo)
        {
            ammo = 0;

            if (!m_Participants.TryGetValue(playerId, out var participant))
            {
                return false;
            }

            var runtime = participant.Controller != null ? participant.Controller.Runtime : null;
            if (runtime == null)
            {
                return false;
            }

            ammo = runtime.MagazineAmmo;
            return true;
        }
    }
}
