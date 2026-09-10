namespace RaidDemo.Shared
{
    /// <summary>
    /// 装备槽位。
    /// </summary>
    /// <remarks>
    /// <para>放在共享层而不是背包层，是因为装备命令（<c>InventoryEquipIntent</c>）必须携带槽位，
    /// 而命令契约属于共享层。若把枚举留在背包层，共享层就会反向依赖背包层，依赖方向被打破。</para>
    ///
    /// <para>枚举值会被背包层当作数组下标使用，因此显式从 0 开始编号且不允许跳号。</para>
    /// </remarks>
    public enum EquipmentSlot
    {
        /// <summary>主武器。M3 的射击逻辑从这里取武器数据。</summary>
        PrimaryWeapon = 0,

        /// <summary>副武器。</summary>
        SecondaryWeapon,

        /// <summary>头盔。</summary>
        Head,

        /// <summary>护甲。</summary>
        Body,

        /// <summary>背包。决定随身网格的容量，也决定承载上限（M6）。</summary>
        Backpack,
    }
}
