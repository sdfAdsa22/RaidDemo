using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 医疗物品的行为：使用后回复生命。
    /// </summary>
    /// <remarks>
    /// <para>它是 <see cref="ItemBehavior"/> 的第一个具体实现——设计文档里预留的扩展点，
    /// 到 M5.5 才真正用上。挂在物品定义上而不是写死在代码里，
    /// 是为了让「加一种止痛药」只需要建一个资产，不碰任何逻辑。</para>
    ///
    /// <para>只描述「回多少血、要读条多久」这两个数值，不描述使用过程：
    /// 读条、打断、消耗的规则属于逻辑层，见 <c>RaidDemo.Raid.ItemUseInteraction</c>。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "MedicalBehavior",
        menuName = "RaidDemo/物品行为/医疗",
        order = 40)]
    public sealed class MedicalBehavior : ItemBehavior
    {
        /// <summary>使用后回复的生命值。</summary>
        [SerializeField] private int m_HealAmount = 25;

        /// <summary>使用所需的读条时长（秒）。</summary>
        [SerializeField] private float m_UseDurationSeconds = 1.5f;

        /// <summary>使用后回复的生命值。</summary>
        public int HealAmount
        {
            get { return m_HealAmount; }
        }

        /// <summary>使用所需的读条时长（秒）。</summary>
        public float UseDurationSeconds
        {
            get { return m_UseDurationSeconds; }
        }

        /// <inheritdoc />
        public override string Validate(ItemDefinition owner)
        {
            if (m_HealAmount <= 0)
            {
                return $"{owner?.Id}：回血量必须大于 0，否则这件物品用了等于没用。";
            }

            if (m_UseDurationSeconds <= 0f)
            {
                return $"{owner?.Id}：使用时长必须大于 0，否则读条没有意义。";
            }

            return null;
        }

        /// <summary>供编辑器内容生成器写入数值。</summary>
        /// <param name="healAmount">回血量。</param>
        /// <param name="useDurationSeconds">使用时长（秒）。</param>
        public void Configure(int healAmount, float useDurationSeconds)
        {
            m_HealAmount = healAmount;
            m_UseDurationSeconds = useDurationSeconds;
        }
    }
}
