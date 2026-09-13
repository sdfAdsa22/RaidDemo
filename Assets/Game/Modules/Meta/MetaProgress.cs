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
        /// <summary>
        /// 新存档的启动资金。
        /// </summary>
        /// <remarks>
        /// 只给钱、不送装备：玩家必须先走进商人界面完成第一次购买，
        /// 经济循环因此从第一分钟就能被看见；同时不会破坏"从零开始"的规则。
        /// </remarks>
        public const int StartingMoney = 5000;

        /// <summary>仓库网格的尺寸（列 x 行）。</summary>
        /// <remarks>
        /// 批次 1 先做成固定的较大网格而不是无限容量：无限容量需要一套分页或滚动界面，
        /// 而这一批的重点是「战利品有归宿」，不是仓库管理本身。
        /// </remarks>
        public const int StashWidth = 10;

        public const int StashHeight = 8;

        /// <summary>默认角色 ID，与 PlayerCharacterBuilder 的 male-a 保持一致。</summary>
        public const string DefaultCharacterId = "male-a";

        /// <summary>创建一个空的局外进度。</summary>
        public MetaProgress(int startingMoney = StartingMoney)
        {
            Money = startingMoney > 0 ? startingMoney : 0;
            Stash = new InventoryGrid(StashWidth, StashHeight, "仓库");

            // 随身物也放在这里：出击准备就是在它上面做的，
            // 而「每局重载场景」意味着随身物必须跨局存活，否则准备完一按出击就白准备了。
            Loadout = new PlayerLoadout(
                new InventoryGrid(PocketWidth, PocketHeight, "主背包"),
                new EquipmentLoadout(),
                new InventoryGrid(AmmoPouchCells, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo));

            Quests = new QuestSystem(
                Stash,
                new ItemFactory(),
                null,
                AddMoney,
                _ => NotifyChanged());

            SelectedCharacterId = DefaultCharacterId;
        }

        /// <summary>仓库网格。跨战局保留，每个场景重新登记进容器注册表。</summary>
        public InventoryGrid Stash { get; }

        /// <summary>玩家的随身携带物（背包网格、装备槽、弹药挂）。跨战局保留。</summary>
        public PlayerLoadout Loadout { get; }

        /// <summary>玩家当前持有的金币。</summary>
        public int Money { get; private set; }

        /// <summary>任务系统。与仓库共享同一份存档生命周期。</summary>
        public QuestSystem Quests { get; }

        /// <summary>
        /// 收集图鉴。与仓库共享同一份存档生命周期。
        /// </summary>
        /// <remarks>
        /// 它只记"曾经拿到过"的物品种类，不参与任何数值计算；
        /// 点亮由 <see cref="RefreshCodex"/> 与场景装配里的 <c>CodexMarker</c> 共同驱动。
        /// </remarks>
        public MetaCodex Codex { get; } = new MetaCodex();

        /// <summary>
        /// 当前选择的玩家角色 ID。
        /// </summary>
        /// <remarks>Meta 层不校验角色目录——那属于表现层；这里只保存稳定 ID。</remarks>
        public string SelectedCharacterId { get; private set; }

        /// <summary>局外进度发生任何变化时触发。存档与界面刷新订阅它。</summary>
        public event System.Action Changed;

        /// <summary>仓库内物品的总账面价值，用于界面上回答「我的家底在变好还是变差」。</summary>
        public int TotalStashValue
        {
            get
            {
                var total = 0;
                var items = Stash.Items;
                for (var i = 0; i < items.Count; i++)
                {
                    total += items[i].TotalValue;
                }

                return total;
            }
        }

        /// <summary>增加金币。</summary>
        /// <remarks>
        /// 这里只改数值，不广播变化。一次交易可能同时改余额与物品，
        /// 广播必须等两件事都完成后由调用方统一发出，否则自动存档可能
        /// 在"钱已扣、物品还没放进去"的中间状态写盘。
        /// </remarks>
        public void AddMoney(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Money += amount;
        }

        /// <summary>尝试扣除金币。余额不足时不做任何改变。</summary>
        public bool TrySpend(int amount)
        {
            if (amount <= 0 || Money < amount)
            {
                return false;
            }

            Money -= amount;
            return true;
        }

        /// <summary>注入物品目录，供任务奖励与上交检查使用。</summary>
        public void AttachCatalog(IItemDefinitionLookup catalog)
        {
            Quests.AttachCatalog(catalog);
        }

        /// <summary>
        /// 选择玩家角色并广播变化。
        /// </summary>
        /// <param name="characterId">角色稳定 ID；为空时忽略。</param>
        public void SetSelectedCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) || characterId == SelectedCharacterId)
            {
                return;
            }

            SelectedCharacterId = characterId;
            NotifyChanged();
        }

        /// <summary>存档还原专用：直接写入角色 ID，不触发变化事件。</summary>
        internal void RestoreSelectedCharacter(string characterId)
        {
            SelectedCharacterId = string.IsNullOrEmpty(characterId)
                ? DefaultCharacterId
                : characterId;
        }

        /// <summary>
        /// 扫描玩家当前持有的全部物品，把尚未点亮的种类补进图鉴。
        /// </summary>
        /// <returns>本次新点亮的条目数。</returns>
        /// <remarks>
        /// <para><b>为什么是"扫描持有物"而不是在每个获得路径上打点：</b>物品进入玩家手里有六条路径
        /// （战局搜刮、商人购买、任务奖励、撤离入库、手动拖拽、拆分合并），逐条打点意味着
        /// 以后每加一条新路径都要记得再补一次；漏掉一条的症状是"某些东西永远点不亮"，
        /// 而图鉴不亮不会引发任何报错，只会在很久以后才被发现。</para>
        ///
        /// <para>本方法是幂等的：重复调用只是空转一圈。发现新条目时会广播一次
        /// <see cref="Changed"/>，让自动存档与界面刷新走既有链路，不需要额外的通知机制。</para>
        /// </remarks>
        public int RefreshCodex()
        {
            var discovered = 0;
            discovered += Codex.MarkAll(EnumerateItemIds(Stash));
            discovered += Codex.MarkAll(EnumerateItemIds(Loadout.Backpack));
            discovered += Codex.MarkAll(EnumerateItemIds(Loadout.AmmoPouch));
            discovered += Codex.MarkAll(EnumerateEquipmentItemIds());

            if (discovered > 0)
            {
                NotifyChanged();
            }

            return discovered;
        }

        /// <summary>遍历一个网格里每件物品的稳定 ID。</summary>
        private static IEnumerable<string> EnumerateItemIds(InventoryGrid grid)
        {
            if (grid == null)
            {
                yield break;
            }

            var items = grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                yield return items[i].Definition.Id;
            }
        }

        /// <summary>遍历装备槽里每件物品的稳定 ID。槽位顺序与 <c>DepositLoadoutToStash</c> 一致。</summary>
        private IEnumerable<string> EnumerateEquipmentItemIds()
        {
            var equipment = Loadout.Equipment;
            if (equipment == null)
            {
                yield break;
            }

            var slots = new[]
            {
                EquipmentSlot.PrimaryWeapon, EquipmentSlot.SecondaryWeapon,
                EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Backpack,
            };

            for (var i = 0; i < slots.Length; i++)
            {
                var item = equipment.Get(slots[i]);
                if (item != null)
                {
                    yield return item.Definition.Id;
                }
            }
        }

        /// <summary>
        /// 存档还原专用：直接写入余额，不触发"变化"事件。
        /// </summary>
        internal void RestoreMoney(int money)
        {
            Money = money > 0 ? money : 0;
        }

        /// <summary>广播一次局外变化。</summary>
        internal void NotifyChanged()
        {
            Changed?.Invoke();
        }

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
            NotifyChanged();
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
            NotifyChanged();
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
