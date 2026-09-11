using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 弹药参数资产。
    /// </summary>
    /// <remarks>
    /// <para>只描述口径与穿透力两件事。**伤害写在武器上而不是弹药上**，
    /// 这样玩家换一种子弹时，需要重新理解的只有"能不能打穿"，而不是两个数字。</para>
    ///
    /// <para>穿透力的参照系是护甲值：1 级甲 10、2 级甲 20、3 级甲 30、4 级甲 40。
    /// 穿透力等于护甲值时穿透系数为 0（全额减伤），达到护甲值的两倍时穿透系数为 1（无视减伤）。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Ammo_",
        menuName = "RaidDemo/弹药参数",
        order = 21)]
    public sealed class AmmoStats : ItemBehavior, IAmmoStats
    {
        /// <summary>口径标识。必须与武器的标识完全一致才能装填。</summary>
        [SerializeField] private string m_CaliberId = "9x19";

        /// <summary>穿透力。与目标有效护甲值比较后得到穿透系数。</summary>
        [SerializeField] private float m_Penetration = 15f;

        /// <inheritdoc />
        public string CaliberId
        {
            get { return m_CaliberId; }
        }

        /// <inheritdoc />
        public float Penetration
        {
            get { return m_Penetration; }
        }

        /// <summary>
        /// 校验参数自洽性。
        /// </summary>
        /// <param name="owner">拥有本行为的物品定义。</param>
        /// <returns>自洽返回 null，否则返回中文说明。</returns>
        public override string Validate(ItemDefinition owner)
        {
            if (string.IsNullOrWhiteSpace(m_CaliberId))
            {
                return "口径标识不能为空，否则这种弹药永远装不进任何枪。";
            }

            if (m_Penetration < 0f)
            {
                return "穿透力不能为负。";
            }

            return null;
        }
    }
}
