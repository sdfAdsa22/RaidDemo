using RaidDemo.Inventory;
using RaidDemo.Data;
using RaidDemo.Shared;
using System.Collections.Generic;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 局外进度：跨战局、跨场景存在的那部分玩家资产。
    /// </summary>
    /// <remarks>
    /// <para><b>它必须挂在跨场景存活的对象上。</b>本项目每开一局都重新加载场景，
    /// 场景里的一切（容器注册表、战局会话、界面）都会被销毁重建；
    /// 只有挂在 <c>DontDestroyOnLoad</c> 对象上的东西才能活过一局。</para>
    ///
    /// <para>仓库网格本身是**同一个对象**被反复登记：每个场景启动时把它登记进
    /// 该场景的容器注册表（用 <see cref="ContainerKind.Stash"/>），
    /// 于是界面与命令层照常按容器 ID 访问，而网格里的物品跨局保留。
    /// 这也是为什么不需要给「仓库」写一套独立的物品搬迁逻辑。</para>
    /// </remarks>
    public sealed class MetaProgress
    {
        /// <summary>仓库网格的尺寸（列 x 行）。</summary>
        /// <remarks>
        /// 批次 1 先做成固定的较大网格而不是无限容量：无限容量需要一套分页或滚动界面，
        /// 而这一批的重点是「战利品有归宿」，不是仓库管理本身。
        /// </remarks>
        public const int StashWidth = 10;

        public const int StashHeight = 8;

        /// <summary>创建一个空的局外进度。</summary>
        public MetaProgress()
        {
            Stash = new InventoryGrid(StashWidth, StashHeight, "仓库");

            // 随身物也放在这里：出击准备就是在它上面做的，
            // 而「每局重载场景」意味着随身物必须跨局存活，否则准备完一按出击就白准备了。
            Loadout = new PlayerLoadout(
                new InventoryGrid(PocketWidth, PocketHeight, "主背包"),
                new EquipmentLoadout(),
                new InventoryGrid(AmmoPouchCells, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo));
        }

        /// <summary>仓库网格。跨战局保留，每个场景重新登记进容器注册表。</summary>
        public InventoryGrid Stash { get; }

        /// <summary>玩家的随身携带物（背包网格、装备槽、弹药挂）。跨战局保留。</summary>
        public PlayerLoadout Loadout { get; }

        /// <summary>没有装备背包时的口袋尺寸，与 M5.6 的规则一致。</summary>
        public const int PocketWidth = 5;

        public const int PocketHeight = 5;

        /// <summary>弹药挂的格数。</summary>
        public const int AmmoPouchCells = 5;

        /// <summary>
        /// 把随身携带的全部物品搬进仓库。撤离成功时调用。
        /// </summary>
        /// <returns>成功入库的件数。</returns>
        /// <remarks>
        /// 先搬装备槽、再搬弹药挂与背包：装备类物品占地最大，
        /// 让小件先进仓库会把空间切碎（M5-P-13 那条教训）。
        /// 放不下的物品会被**丢弃并计数**，而不是静默消失——它是仓库满时唯一可能丢东西的路径。
        /// </remarks>
        public int DepositLoadoutToStash()
        {
            var moved = 0;
            var failed = 0;
            var equipment = Loadout.Equipment;
            var slots = new[]
            {
                EquipmentSlot.PrimaryWeapon, EquipmentSlot.SecondaryWeapon,
                EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Backpack,
            };

            for (var i = 0; i < slots.Length; i++)
            {
                if (equipment.Get(slots[i]) == null)
                {
                    continue;
                }

                // Unequip 在仓库放不下时会失败且**保持槽位不变**，因此这里不需要额外判断。
                if (equipment.Unequip(slots[i], Stash).Success)
                {
                    moved++;
                }
                else
                {
                    failed++;
                }
            }

            moved += DrainGrid(Loadout.AmmoPouch, ref failed);
            moved += DrainGrid(Loadout.Backpack, ref failed);
            LastDepositFailures = failed;
            return moved;
        }

        /// <summary>上一批入库时因为仓库放不下而被丢弃的件数。</summary>
        public int LastDepositFailures { get; private set; }

        /// <summary>
        /// 清空随身携带物。阵亡与超时时调用——**这就是「装备真的没了」的实现**。
        /// </summary>
        /// <remarks>仓库不受影响：它是唯一的安全区。</remarks>
        public void ClearLoadout()
        {
            Loadout.Equipment.Clear();
            DiscardAll(Loadout.AmmoPouch);
            DiscardAll(Loadout.Backpack);
        }

        /// <summary>把一个网格里的物品全部丢弃。</summary>
        private static void DiscardAll(InventoryGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            var items = new List<ItemInstance>(grid.Items);
            for (var i = 0; i < items.Count; i++)
            {
                grid.Remove(items[i]);
            }
        }

        /// <summary>把一个网格里的物品全部搬进仓库。</summary>
        private int DrainGrid(InventoryGrid grid, ref int failed)
        {
            if (grid == null)
            {
                return 0;
            }

            var moved = 0;
            var items = new List<ItemInstance>(grid.Items);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!grid.Remove(item).Success)
                {
                    continue;
                }

                if (Stash.AutoPlace(item).Success)
                {
                    moved++;
                }
                else
                {
                    failed++;
                }
            }

            return moved;
        }
    }
}
