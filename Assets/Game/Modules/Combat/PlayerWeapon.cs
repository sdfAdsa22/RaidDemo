using RaidDemo.Data;
using RaidDemo.Kernel;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 玩家当前手持的武器：装备哪把、弹匣什么状态、弹匣里装的是哪种弹。
    /// </summary>
    /// <remarks>
    /// <para>把"当前武器"从背包里单独抽出来，是因为背包只知道"装备槽里放着一件物品"，
    /// 而战斗需要的是"这把枪现在还能打几发"。后者是运行时状态，不属于物品定义。</para>
    /// <para>它同时负责在**保持弹匣状态**的前提下响应换枪：同一个武器定义再次装备时
    /// 不重建运行时，否则玩家把枪放回背包再拿出来，弹匣就会白送一满。</para>
    /// </remarks>
    public sealed class PlayerWeapon
    {
        private readonly DeterministicRandom m_Random;

        private IWeaponStats m_Stats;
        private WeaponRuntime m_Runtime;

        /// <summary>创建手持武器状态。</summary>
        /// <param name="random">散布用的确定性随机数。</param>
        public PlayerWeapon(DeterministicRandom random)
        {
            m_Random = random ?? throw new System.ArgumentNullException(nameof(random));
        }

        /// <summary>当前是否装备了武器。</summary>
        public bool IsEquipped
        {
            get { return m_Runtime != null; }
        }

        /// <summary>当前武器运行时。未装备时为 null。</summary>
        public WeaponRuntime Runtime
        {
            get { return m_Runtime; }
        }

        /// <summary>当前武器参数。未装备时为 null。</summary>
        public IWeaponStats Stats
        {
            get { return m_Stats; }
        }

        /// <summary>
        /// 弹匣内当前装填弹药的穿透力。
        /// </summary>
        /// <remarks>
        /// 这一项让"带什么子弹"成为真实选择：装满穿甲弹之后，
        /// 在打空并重新装填之前，每一发都按穿甲弹的穿透力结算。
        /// </remarks>
        public float LoadedPenetration { get; private set; }

        /// <summary>
        /// 装备一把武器。传入 null 表示卸下。
        /// </summary>
        /// <param name="stats">武器参数。</param>
        /// <returns>武器确实发生了变化返回 true；装备的还是同一把则返回 false。</returns>
        public bool Equip(IWeaponStats stats)
        {
            if (stats == null)
            {
                var had = m_Runtime != null;
                Clear();
                return had;
            }

            if (ReferenceEquals(stats, m_Stats) && m_Runtime != null)
            {
                return false;
            }

            m_Stats = stats;
            m_Runtime = new WeaponRuntime(stats, m_Random);
            LoadedPenetration = 0f;
            return true;
        }

        /// <summary>卸下武器并清空弹匣状态。</summary>
        public void Clear()
        {
            m_Stats = null;
            m_Runtime = null;
            LoadedPenetration = 0f;
        }

        /// <summary>记录弹匣内弹药的穿透力。</summary>
        /// <param name="penetration">穿透力。</param>
        public void SetLoadedPenetration(float penetration)
        {
            LoadedPenetration = penetration < 0f ? 0f : penetration;
        }
    }
}
