using RaidDemo.Shared;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的口径可读性部分：弹药挂标题按当前武器口径动态显示。
    /// </summary>
    /// <remarks>
    /// <para>从布局文件拆出来的原因：布局文件触及 400 行上限，而"弹药挂该放什么弹"
    /// 与"网格怎么排"本来就是两件事。</para>
    /// <para>M8 批次 2 随口径徽标一起加入：换弹只从弹药挂取弹，
    /// 因此标题直接列出当前两把武器需要的口径，是"我该往里放什么"最自然的回答。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>
        /// 刷新弹药挂标题，列出当前两把武器需要的口径。
        /// </summary>
        /// <remarks>
        /// <para>弹药挂是全界面上"玩家该往里放什么"最自然的回答位置：
        /// 标题直接写成「弹药挂 · 5.45 + 9x19」，玩家不需要记哪把枪吃什么弹。</para>
        /// <para>没有装备武器（或武器没有口径）时退回纯「弹药挂」，
        /// 不显示"未知口径"这种像报错的文字。</para>
        /// </remarks>
        private void RefreshAmmoPouchTitle()
        {
            if (m_AmmoPouchView == null)
            {
                return;
            }

            var calibers = new System.Collections.Generic.List<string>(2);
            AddCaliber(calibers, EquipmentSlot.PrimaryWeapon);
            AddCaliber(calibers, EquipmentSlot.SecondaryWeapon);

            var title = calibers.Count > 0
                ? "弹药挂 · " + string.Join(" + ", calibers)
                : "弹药挂";
            m_AmmoPouchView.SetTitle(title);
        }

        /// <summary>把一个装备槽武器的口径加进列表（去重，保持装备槽顺序）。</summary>
        private void AddCaliber(System.Collections.Generic.List<string> calibers, EquipmentSlot slot)
        {
            var caliberId = m_Loadout?.Equipment?.Get(slot)?.Definition?.WeaponStats?.CaliberId;
            if (string.IsNullOrEmpty(caliberId) || calibers.Contains(caliberId))
            {
                return;
            }

            calibers.Add(RaidDemo.Data.CaliberPalette.GetDisplayName(caliberId));
        }

    }
}
