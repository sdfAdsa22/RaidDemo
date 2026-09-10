using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 角色随身携带的全部物品：一个背包网格加一组装备槽。
    /// </summary>
    /// <remarks>
    /// <para>它是负重计算的输入源，也是"这一局我要带走什么"的完整答案。
    /// 仓库、商人与结算系统在后续里程碑里都从它取数。</para>
    ///
    /// <para>注意装备栏里的枪与护甲**同样计入总重量**——这一点不是细节，
    /// 它意味着换一把更重的枪会立刻影响撤离能力，玩家在选择主武器时就要考虑负重预算。</para>
    /// </remarks>
    public sealed class PlayerLoadout
    {
        private readonly InventoryGrid m_Backpack;
        private readonly EquipmentLoadout m_Equipment;

        /// <summary>创建角色携带物。</summary>
        /// <param name="backpack">主背包网格，不允许为 null。</param>
        /// <param name="equipment">装备栏，不允许为 null。</param>
        public PlayerLoadout(InventoryGrid backpack, EquipmentLoadout equipment)
        {
            m_Backpack = backpack ?? throw new System.ArgumentNullException(nameof(backpack));
            m_Equipment = equipment ?? throw new System.ArgumentNullException(nameof(equipment));
        }

        /// <summary>主背包网格。</summary>
        public InventoryGrid Backpack
        {
            get { return m_Backpack; }
        }

        /// <summary>装备栏。</summary>
        public EquipmentLoadout Equipment
        {
            get { return m_Equipment; }
        }

        /// <summary>背包与装备栏的总重量（千克）。</summary>
        public float TotalWeightKg
        {
            get { return m_Backpack.TotalWeightKg + m_Equipment.TotalWeightKg; }
        }

        /// <summary>
        /// 计算负重比。
        /// </summary>
        /// <param name="profile">负重配置，可为 null。</param>
        /// <returns>负重比；配置缺失时返回 0，表示没有约束。</returns>
        public float EncumbranceRatio(EncumbranceProfile profile)
        {
            return profile == null ? 0f : profile.RatioFor(TotalWeightKg);
        }

        /// <summary>判定当前负重状态。</summary>
        /// <param name="profile">负重配置。</param>
        public EncumbranceState EvaluateState(EncumbranceProfile profile)
        {
            return EncumbranceRules.Evaluate(TotalWeightKg, profile);
        }

        /// <summary>直接算出当前应生效的移动修正。</summary>
        /// <param name="profile">负重配置。</param>
        /// <remarks>装配层每帧调用它，然后把结果交给移动配置。</remarks>
        public MovementModifiers EvaluateModifiers(EncumbranceProfile profile)
        {
            return EncumbranceRules.Resolve(TotalWeightKg, profile);
        }
    }
}
