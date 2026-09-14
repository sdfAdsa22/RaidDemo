using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器配发的"基础装备"（P5）：新账号的第一套家当，也是没有装备时的兜底。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>P5 之后"带什么进图"由玩家在共享仓库里决定，而新账号的仓库
    /// 与随身装备都是空的——没有这套基础装备，联机里会出现"手里什么都没有、又打不开商店"的死局
    /// （商店的服务器权威化排在 P5.5）。发一把步枪与一叠子弹，循环就能跑起来：
    /// 带出去 → 撤离入库 → 下一局换更好的。</para>
    ///
    /// <para><b>两处使用，一份规格：</b>新账号建号时发一套（见 <c>ServerProfileStore</c>），
    /// 以及参战时没有可用武器时的兜底配发（见 <c>ServerCombatCoordinator</c>）。
    /// 两处共用这里的常量与构造方法，避免"建号发的子弹"与"兜底发的子弹"不是同一种。</para>
    /// </remarks>
    internal static class ServerStarterKit
    {
        /// <summary>基础武器。按稳定 ID 取，不按显示名。</summary>
        public const string DefaultWeaponId = "weapon.rifle.ak74";

        /// <summary>基础弹药。</summary>
        public const string DefaultAmmoId = "ammo.5.45.standard";

        /// <summary>随枪配发的备弹数量。</summary>
        public const int DefaultReserveRounds = 120;

        /// <summary>弹药挂尺寸（与客户端一致：一行五格）。</summary>
        public const int AmmoPouchWidth = 5;
        public const int AmmoPouchHeight = 1;

        /// <summary>没有背包装备时的口袋尺寸（与客户端规则一致）。</summary>
        public const int BackpackWidth = 5;
        public const int BackpackHeight = 5;

        /// <summary>
        /// 构造一整套基础装备（独立的网格与装备槽）。
        /// </summary>
        /// <param name="catalog">物品目录。</param>
        /// <param name="factory">物品工厂。</param>
        /// <returns>装备好的随身装备；目录里缺物品时返回"空装备"而不是 null。</returns>
        public static PlayerLoadout Build(IItemDefinitionLookup catalog, ItemFactory factory)
        {
            var loadout = new PlayerLoadout(
                new InventoryGrid(BackpackWidth, BackpackHeight, "服务端配发背包"),
                new EquipmentLoadout(),
                new InventoryGrid(
                    AmmoPouchWidth, AmmoPouchHeight, "服务端配发弹药挂", acceptedCategory: ItemCategory.Ammo));

            Apply(loadout, catalog, factory);
            return loadout;
        }

        /// <summary>
        /// 把基础装备填进一份已有的随身装备（已装备的槽与已放好的弹药不动）。
        /// </summary>
        /// <param name="loadout">目标随身装备。</param>
        /// <param name="catalog">物品目录。</param>
        /// <param name="factory">物品工厂。</param>
        /// <returns>实际填进去的件数（0 表示目录里缺物品）。</returns>
        /// <remarks>
        /// "已有的不动"是刻意的：老账号重连或重进时不能把玩家自己准备的枪换掉。
        /// </remarks>
        public static int Apply(PlayerLoadout loadout, IItemDefinitionLookup catalog, ItemFactory factory)
        {
            if (loadout?.Equipment == null || catalog == null || factory == null)
            {
                return 0;
            }

            var applied = 0;

            if (loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon) == null
                && catalog.TryGet(DefaultWeaponId, out var weaponDefinition)
                && weaponDefinition.WeaponStats != null)
            {
                loadout.Equipment.Equip(factory.Create(weaponDefinition), EquipmentSlot.PrimaryWeapon);
                applied++;
            }

            if (loadout.AmmoPouch != null
                && loadout.AmmoPouch.Items.Count == 0
                && catalog.TryGet(DefaultAmmoId, out var ammoDefinition))
            {
                var ammo = factory.Create(ammoDefinition, DefaultReserveRounds);
                if (loadout.AmmoPouch.Place(ammo, new GridPoint(0, 0), rotated: false).Success)
                {
                    applied++;
                }
            }

            return applied;
        }
    }
}
