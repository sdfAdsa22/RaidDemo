using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 把换弹失败的结果码翻译成给玩家看的一句话。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>换弹失败的三种情况（没武器、弹匣满、没有匹配弹药）
    /// 在旧版本里是"按 R 毫无反应"——战斗层返回了明确的失败码，但没有一个界面读它。
    /// 玩家因此无法区分"游戏卡了"和"弹挂里没有 5.45"。</para>
    ///
    /// <para>翻译放在装配层（而不是战斗层或界面层）：战斗层不认识弹药口径的显示名与背包结构，
    /// 界面层又不该知道命令结果码；装配层同时认识两边的东西，正是翻译该待的地方。</para>
    /// </remarks>
    internal static class ReloadFeedback
    {
        /// <summary>
        /// 生成提示文案。
        /// </summary>
        /// <param name="resultCode">命令结果码，见 <see cref="CommandCodes"/>。</param>
        /// <param name="caliberId">当前武器的口径，可为空。</param>
        /// <param name="loadout">角色携带物，用于统计背包里的同口径弹药。</param>
        /// <returns>提示文案；不需要提示时返回 null。</returns>
        public static string BuildMessage(string resultCode, string caliberId, PlayerLoadout loadout)
        {
            switch (resultCode)
            {
                case CommandCodes.CombatNoWeapon:
                    return "手里没有武器";

                case CommandCodes.CombatMagazineFull:
                    return "弹匣是满的";

                case CommandCodes.CombatNoAmmo:
                    var display = CaliberPalette.GetDisplayName(caliberId);
                    var inBackpack = loadout != null
                        ? AmmoReserve.CountAvailable(loadout.Backpack, caliberId)
                        : 0;
                    return inBackpack > 0
                        ? $"弹药挂里没有 {display} 弹药（背包里有 {inBackpack} 发，先搬进弹药挂）"
                        : $"没有 {display} 弹药";

                default:
                    // 其它失败（正在换弹、已阵亡……）在屏幕上已经有对应状态，不重复提示。
                    return null;
            }
        }
    }
}
