using RaidDemo.Inventory;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器侧"这份装备操作该落在哪一份随身装备上"的路由规则。
    /// </summary>
    /// <remarks>
    /// <para>抽成纯函数是为了能被 EditMode 用例直接钉住：这条规则出错的症状
    /// （安全屋里换好装备、进图却空手）在实机日志里非常绕，而规则本身只有
    /// "世界类型 + 两份 loadout"这三个输入。</para>
    /// </remarks>
    public static class ServerLoadoutRouting
    {
        /// <summary>
        /// 选择背包命令 / 装备镜像使用的那份随身装备。
        /// </summary>
        /// <param name="worldKind">服务器当前托管的世界。</param>
        /// <param name="profileLoadout">账号档案里的随身装备（进图配发用的就是这一份）。</param>
        /// <param name="combatLoadout">战斗单位当前持有的随身装备。</param>
        /// <remarks>
        /// <para><b>安全屋必须用账号档案那一份（U-100）：</b>安全屋里玩家的战斗单位是
        /// <c>AddPlayerToCombat(null)</c> 临时配发的默认套（靶场用），改它不会进档案、
        /// 也不会带进战局；而进图配发读的是档案。两边不是同一个对象时，表现就是
        /// "安全屋里装备显示得好好的，一进图空手"。</para>
        ///
        /// <para>档案缺失时退回战斗单位那份：宁可让命令通道能建立，
        /// 也不要因为一次读档失败把整个背包界面卡死。</para>
        /// </remarks>
        public static PlayerLoadout ResolveCommandLoadout(
            ServerWorldKind worldKind,
            PlayerLoadout profileLoadout,
            PlayerLoadout combatLoadout)
        {
            if (worldKind == ServerWorldKind.SafeHouse && profileLoadout != null)
            {
                return profileLoadout;
            }

            return combatLoadout ?? profileLoadout;
        }
    }
}
