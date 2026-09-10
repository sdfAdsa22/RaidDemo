using System;
using RaidDemo.Data;
using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 角色身上的装备槽集合。
    /// </summary>
    /// <remarks>
    /// <para>它只负责"哪个槽里放着什么"，不持有 UI、不引用场景，因此可以在测试里直接构造。
    /// 槽位数量固定为 5 个（主武器、副武器、头盔、护甲、背包），这是项目已确认的范围。</para>
    ///
    /// <para><b>关于被替换下来的物品</b>：换装备时旧装备必须有去处，否则就是凭空消失。
    /// 本类的约定是——调用方先把新装备从原容器取出，再把原容器作为 <c>returnTo</c> 传进来；
    /// 本类负责为旧装备在 <c>returnTo</c> 里确认位置，确认不了就整体失败，什么都不改。</para>
    /// </remarks>
    public sealed class EquipmentLoadout
    {
        /// <summary>槽位数量，等于 <see cref="EquipmentSlot"/> 的取值个数。</summary>
        public const int SlotCount = 5;

        /// <summary>每个槽位上的物品，null 表示空槽。</summary>
        private readonly ItemInstance[] m_Slots;

        /// <summary>创建一个空装备栏。</summary>
        public EquipmentLoadout()
        {
            m_Slots = new ItemInstance[SlotCount];
        }

        /// <summary>所有槽位的总重量（千克）。装备着的枪与护甲同样压在身上。</summary>
        public float TotalWeightKg
        {
            get
            {
                var total = 0f;
                for (var i = 0; i < m_Slots.Length; i++)
                {
                    if (m_Slots[i] != null)
                    {
                        total += m_Slots[i].WeightKg;
                    }
                }

                return total;
            }
        }

        /// <summary>是否装备了背包。</summary>
        public bool HasBackpack
        {
            get { return m_Slots[(int)EquipmentSlot.Backpack] != null; }
        }

        /// <summary>读取指定槽位上的物品，空槽返回 null。</summary>
        public ItemInstance Get(EquipmentSlot slot)
        {
            return m_Slots[(int)slot];
        }

        /// <summary>
        /// 判断某种分类能否装进指定槽位。
        /// </summary>
        /// <returns>允许返回 true。</returns>
        /// <remarks>规则集中在这里，UI 做高亮、命令处理器做校验都读它，避免两处各写一份。</remarks>
        public static bool IsCategoryAllowed(ItemCategory category, EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.PrimaryWeapon:
                case EquipmentSlot.SecondaryWeapon:
                    return category == ItemCategory.Weapon;

                case EquipmentSlot.Head:
                    return category == ItemCategory.Helmet;

                case EquipmentSlot.Body:
                    return category == ItemCategory.BodyArmor;

                case EquipmentSlot.Backpack:
                    return category == ItemCategory.Backpack;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 把物品装到指定槽位。
        /// </summary>
        /// <param name="item">要装备的物品，调用方需保证它已从原容器取出。</param>
        /// <param name="slot">目标槽位。</param>
        /// <param name="returnTo">
        /// 被替换下来的旧装备的退路。槽位原本为空时可以不传；
        /// 槽位非空却不传，操作会以"槽位已被占用"失败。
        /// </param>
        /// <returns>操作结果。</returns>
        public InventoryResult Equip(ItemInstance item, EquipmentSlot slot, InventoryGrid returnTo = null)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "要装备的物品不存在。");
            }

            if (!IsCategoryAllowed(item.Definition.Category, slot))
            {
                return InventoryResult.Fail(
                    InventoryFailure.SlotTypeMismatch,
                    $"{item.Definition.DisplayName} 不能装备到 {slot} 槽位。");
            }

            var replaced = m_Slots[(int)slot];
            if (replaced == null)
            {
                m_Slots[(int)slot] = item;
                return InventoryResult.Ok();
            }

            if (ReferenceEquals(replaced, item))
            {
                return InventoryResult.Ok();
            }

            if (returnTo == null)
            {
                return InventoryResult.Fail(
                    InventoryFailure.Occupied,
                    "槽位已被占用，且没有指定旧装备的退路。");
            }

            // 先确认旧装备有地方放，再动槽位。顺序反过来的话，
            // 一旦放不下就会出现"装备没了、背包里也没有"的丢件事故。
            if (!returnTo.CanAutoPlace(replaced))
            {
                return InventoryResult.Fail(
                    InventoryFailure.Full,
                    $"{returnTo.Label} 放不下换下来的 {replaced.Definition.DisplayName}。");
            }

            var placed = returnTo.AutoPlace(replaced);
            if (!placed.Success)
            {
                // 兜底：校验与提交之间若出现不一致，宁可让本次装备失败，也不能丢东西。
                return placed;
            }

            m_Slots[(int)slot] = item;
            return InventoryResult.Ok();
        }

        /// <summary>
        /// 卸下指定槽位的装备并放进目标容器。
        /// </summary>
        /// <param name="slot">要卸下的槽位。</param>
        /// <param name="target">接收装备的容器。</param>
        /// <returns>操作结果。容器放不下时失败，槽位保持不变。</returns>
        public InventoryResult Unequip(EquipmentSlot slot, InventoryGrid target)
        {
            var item = m_Slots[(int)slot];
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, $"{slot} 槽位是空的。");
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "目标容器不存在。");
            }

            if (!target.CanAutoPlace(item))
            {
                return InventoryResult.Fail(
                    InventoryFailure.Full,
                    $"{target.Label} 放不下卸下的 {item.Definition.DisplayName}。");
            }

            var placed = target.AutoPlace(item);
            if (!placed.Success)
            {
                return placed;
            }

            m_Slots[(int)slot] = null;
            return InventoryResult.Ok();
        }

        /// <summary>清空全部槽位。仅供测试装载与存档初始化使用。</summary>
        public void Clear()
        {
            Array.Clear(m_Slots, 0, m_Slots.Length);
        }
    }
}
