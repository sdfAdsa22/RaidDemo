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
        private InventoryGrid m_Backpack;
        private readonly InventoryGrid m_AmmoPouch;
        private readonly EquipmentLoadout m_Equipment;

        /// <summary>创建角色携带物。</summary>
        /// <param name="backpack">主背包网格，不允许为 null。</param>
        /// <param name="equipment">装备栏，不允许为 null。</param>
        /// <param name="ammoPouch">
        /// 弹药挂。换弹只从它取弹，因此它可以为 null（表示身上没有可用的弹药）。
        /// </param>
        public PlayerLoadout(InventoryGrid backpack, EquipmentLoadout equipment, InventoryGrid ammoPouch = null)
        {
            m_Backpack = backpack ?? throw new System.ArgumentNullException(nameof(backpack));
            m_Equipment = equipment ?? throw new System.ArgumentNullException(nameof(equipment));
            m_AmmoPouch = ammoPouch;
        }

        /// <summary>
        /// 替换随身背包网格。
        /// </summary>
        /// <param name="backpack">新网格，为 null 时忽略。</param>
        /// <remarks>
        /// 换背包（装备或卸下）会改变随身容量，因此网格本身需要被替换。
        /// 调用方负责先把旧网格里的物品搬进新网格——这个方法只做替换，不搬运，
        /// 因为「装不下怎么办」属于规则，不该藏在一个 setter 里。
        /// </remarks>
        public void ReplaceBackpack(InventoryGrid backpack)
        {
            if (backpack != null)
            {
                m_Backpack = backpack;
            }
        }

        /// <summary>主背包网格。</summary>
        public InventoryGrid Backpack
        {
            get { return m_Backpack; }
        }

        /// <summary>
        /// 弹药挂：随身可取的弹药。
        /// </summary>
        /// <remarks>
        /// 换弹**只**从这里取弹，因此它是"能不能继续打"的唯一来源。
        /// 背包里的弹药要先搬进来才能使用——这条规则让"弹挂里装多少"成为出击前的决策。
        /// </remarks>
        public InventoryGrid AmmoPouch
        {
            get { return m_AmmoPouch; }
        }

        /// <summary>装备栏。</summary>
        public EquipmentLoadout Equipment
        {
            get { return m_Equipment; }
        }

        /// <summary>背包与装备栏的总重量（千克）。</summary>
        public float TotalWeightKg
        {
            get
            {
                var total = m_Backpack.TotalWeightKg + m_Equipment.TotalWeightKg;
                if (m_AmmoPouch != null)
                {
                    total += m_AmmoPouch.TotalWeightKg;
                }

                return total;
            }
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
