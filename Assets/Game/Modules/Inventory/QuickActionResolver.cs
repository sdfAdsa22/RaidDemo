using RaidDemo.Data;
using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>双击一件物品时应当执行的动作类型。</summary>
    public enum QuickActionKind
    {
        /// <summary>没有任何可执行的动作。</summary>
        None = 0,

        /// <summary>移动到另一个容器。</summary>
        MoveToContainer,

        /// <summary>装备到某个槽位。</summary>
        Equip,
    }

    /// <summary>一次快速操作的决策结果。</summary>
    public readonly struct QuickAction
    {
        private QuickAction(QuickActionKind kind, int targetContainerId, EquipmentSlot slot)
        {
            Kind = kind;
            TargetContainerId = targetContainerId;
            Slot = slot;
        }

        /// <summary>动作类型。</summary>
        public QuickActionKind Kind { get; }

        /// <summary>目标容器标识。仅移动动作有意义。</summary>
        public int TargetContainerId { get; }

        /// <summary>目标槽位。仅装备动作有意义。</summary>
        public EquipmentSlot Slot { get; }

        /// <summary>是否是一个可执行的动作。</summary>
        public bool IsValid
        {
            get { return Kind != QuickActionKind.None; }
        }

        /// <summary>构造移动动作。</summary>
        public static QuickAction MoveTo(int containerId)
        {
            return new QuickAction(QuickActionKind.MoveToContainer, containerId, default);
        }

        /// <summary>构造装备动作。</summary>
        public static QuickAction EquipTo(EquipmentSlot slot)
        {
            return new QuickAction(QuickActionKind.Equip, 0, slot);
        }

        /// <summary>构造空动作。</summary>
        public static QuickAction None
        {
            get { return default; }
        }
    }

    /// <summary>
    /// 快速操作（双击）的目标决策。
    /// </summary>
    /// <remarks>
    /// <para>把"双击该做什么"从界面里抽出来单独成类，原因与 M2 抽走规则层一样：
    /// 这段逻辑有明确的优先级与多条回退路径，值得被单元测试逐条锁死，
    /// 而写在 MonoBehaviour 里就只能靠手点。</para>
    /// <para>决策顺序（自上而下，命中即返回）：</para>
    /// <list type="number">
    /// <item><description>弹药：优先进弹药挂，装不下再进背包，再不行进另一个容器。</description></item>
    /// <item><description>可装备物品：装到对应槽位。武器优先空槽，两个武器槽都满则替换主武器。</description></item>
    /// <item><description>其余物品：在背包与战利品箱之间搬运，维持 M2 的原有行为。</description></item>
    /// </list>
    /// </remarks>
    public static class QuickActionResolver
    {
        /// <summary>
        /// 决定双击某件物品时应当执行的动作。
        /// </summary>
        /// <param name="context">可选容器与装备栏。</param>
        /// <param name="source">物品当前所在的容器。</param>
        /// <param name="item">被双击的物品。</param>
        /// <returns>决策结果。没有任何可行动作时返回 <see cref="QuickAction.None"/>。</returns>
        public static QuickAction Resolve(QuickActionContext context, InventoryGrid source, ItemInstance item)
        {
            if (context == null || source == null || item == null)
            {
                return QuickAction.None;
            }

            if (item.Definition.Category == ItemCategory.Ammo)
            {
                return ResolveAmmo(context, source, item);
            }

            var equipSlot = ResolveEquipSlot(context, source, item);
            if (equipSlot.HasValue)
            {
                return QuickAction.EquipTo(equipSlot.Value);
            }

            return ResolveFallbackMove(context, source, item);
        }

        /// <summary>弹药的优先级：弹药挂 → 背包 → 另一个容器。</summary>
        private static QuickAction ResolveAmmo(QuickActionContext context, InventoryGrid source, ItemInstance item)
        {
            if (!ReferenceEquals(source, context.AmmoPouch)
                && context.AmmoPouch != null
                && context.AmmoPouch.CanAutoPlace(item))
            {
                return QuickAction.MoveTo(context.AmmoPouchContainerId);
            }

            if (!ReferenceEquals(source, context.Backpack)
                && context.Backpack != null
                && context.Backpack.CanAutoPlace(item))
            {
                return QuickAction.MoveTo(context.BackpackContainerId);
            }

            return ResolveFallbackMove(context, source, item);
        }

        /// <summary>
        /// 判断物品应当装到哪个槽位。返回 null 表示这不是一件可装备的物品。
        /// </summary>
        /// <remarks>
        /// 武器的槽位选择是唯一有分支的地方：优先空槽，两个都满时替换**主武器**。
        /// 双击表达的意图是"我要用这个"，替换当前手持的那把最符合直觉。
        /// </remarks>
        private static EquipmentSlot? ResolveEquipSlot(QuickActionContext context, InventoryGrid source, ItemInstance item)
        {
            var equipment = context.Equipment;
            if (equipment == null)
            {
                return null;
            }

            switch (item.Definition.Category)
            {
                case ItemCategory.Weapon:
                    if (equipment.Get(EquipmentSlot.PrimaryWeapon) == null)
                    {
                        return EquipmentSlot.PrimaryWeapon;
                    }

                    return equipment.Get(EquipmentSlot.SecondaryWeapon) == null
                        ? EquipmentSlot.SecondaryWeapon
                        : EquipmentSlot.PrimaryWeapon;

                case ItemCategory.Helmet:
                    return EquipmentSlot.Head;

                case ItemCategory.BodyArmor:
                    return EquipmentSlot.Body;

                case ItemCategory.Backpack:
                    return EquipmentSlot.Backpack;

                default:
                    return null;
            }
        }

        /// <summary>其余物品：在背包与战利品箱之间搬运，维持 M2 的原有行为。</summary>
        private static QuickAction ResolveFallbackMove(QuickActionContext context, InventoryGrid source, ItemInstance item)
        {
            if (ReferenceEquals(source, context.Backpack))
            {
                if (context.Loot != null && context.Loot.CanAutoPlace(item))
                {
                    return QuickAction.MoveTo(context.LootContainerId);
                }

                return QuickAction.None;
            }

            if (context.Backpack != null && context.Backpack.CanAutoPlace(item))
            {
                return QuickAction.MoveTo(context.BackpackContainerId);
            }

            return QuickAction.None;
        }
    }

    /// <summary>
    /// 快速操作决策所需的上下文：三个容器与装备栏。
    /// </summary>
    /// <remarks>
    /// 做成独立对象而不是一长串参数，是因为决策规则今后还会继续增加分支，
    /// 每加一条就改一次签名会让调用方到处跟着改。
    /// </remarks>
    public sealed class QuickActionContext
    {
        /// <summary>主背包。</summary>
        public InventoryGrid Backpack;

        /// <summary>弹药挂。</summary>
        public InventoryGrid AmmoPouch;

        /// <summary>战利品箱。</summary>
        public InventoryGrid Loot;

        /// <summary>主背包的容器标识。</summary>
        public int BackpackContainerId;

        /// <summary>弹药挂的容器标识。</summary>
        public int AmmoPouchContainerId;

        /// <summary>战利品箱的容器标识。</summary>
        public int LootContainerId;

        /// <summary>装备栏。</summary>
        public EquipmentLoadout Equipment;
    }
}
