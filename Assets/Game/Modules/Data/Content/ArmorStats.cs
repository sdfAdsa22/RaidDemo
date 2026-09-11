using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 护甲参数资产。
    /// </summary>
    /// <remarks>
    /// <para>只描述等级、最大耐久与磨损系数三件事。
    /// **减伤率由等级经全局表换算**（见战斗层的 <c>ArmorTiers</c>），
    /// 不在这里逐件配置——"3 级甲就是 50% 减伤"必须是一句玩家能记住的话，
    /// 如果两件同为 3 级的护甲减伤不一样，这条可读性就没了。</para>
    ///
    /// <para>耐久的作用是让护甲**连续衰减**而不是突然失效：
    /// 耐久越低，有效护甲值越低，穿透系数随之上升，玩家能感觉到"越打越脆"。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Armor_",
        menuName = "RaidDemo/护甲参数",
        order = 22)]
    public sealed class ArmorStats : ItemBehavior, IArmorStats
    {
        /// <summary>防护等级，取值 1 到 4。等级决定护甲值与减伤率。</summary>
        [SerializeField] private int m_ProtectionLevel = 2;

        /// <summary>最大耐久。归零后护甲不再提供任何减伤。</summary>
        [SerializeField] private float m_MaxDurability = 60f;

        /// <summary>磨损系数。每次受击扣除的耐久 = 基础伤害 × 该系数，取值通常在 0.2 到 0.6 之间。</summary>
        [SerializeField] private float m_WearFactor = 0.35f;

        /// <inheritdoc />
        public int ProtectionLevel
        {
            get { return m_ProtectionLevel; }
        }

        /// <inheritdoc />
        public float MaxDurability
        {
            get { return m_MaxDurability; }
        }

        /// <inheritdoc />
        public float WearFactor
        {
            get { return m_WearFactor; }
        }

        /// <summary>
        /// 校验参数自洽性。
        /// </summary>
        /// <param name="owner">拥有本行为的物品定义。</param>
        /// <returns>自洽返回 null，否则返回中文说明。</returns>
        public override string Validate(ItemDefinition owner)
        {
            if (m_ProtectionLevel < 1 || m_ProtectionLevel > 4)
            {
                return $"防护等级必须在 1 到 4 之间，当前为 {m_ProtectionLevel}。";
            }

            if (m_MaxDurability <= 0f)
            {
                return "最大耐久必须大于 0，否则这件护甲一穿上就是坏的。";
            }

            if (m_WearFactor < 0f)
            {
                return "磨损系数不能为负。";
            }

            return null;
        }
    }
}
