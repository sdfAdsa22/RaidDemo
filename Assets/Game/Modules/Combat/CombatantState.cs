using RaidDemo.Data;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 一个可受击单位的战斗状态：生命、护甲等级与护甲耐久。
    /// </summary>
    /// <remarks>
    /// <para>纯 C# 类。玩家与 AI 都用它，区别只在于意图从哪来——
    /// M4 的 AI 与玩家共用同一套伤害结算，不会出现"AI 打人更疼"这种双份实现。</para>
    /// <para>护甲状态（等级 / 耐久 / 磨损）保存在这里，而不是反过来去问背包要——
    /// 战斗中每次受击都遍历装备列表既慢又容易在装备切换的瞬间读到不一致的状态。</para>
    /// </remarks>
    public sealed class CombatantState
    {
        private readonly int m_Id;
        private readonly float m_MaxHealth;
        private readonly int m_ArmorLevel;
        private readonly float m_ArmorMaxDurability;
        private readonly float m_ArmorWearFactor;

        private float m_Health;
        private float m_ArmorDurability;

        /// <summary>
        /// 创建一个可受击单位。
        /// </summary>
        /// <param name="id">运行时标识，必须唯一且非 0。</param>
        /// <param name="maxHealth">最大生命值。</param>
        /// <param name="armor">护甲参数，可为 null 表示无甲。</param>
        public CombatantState(int id, float maxHealth, IArmorStats armor = null)
        {
            m_Id = id;
            m_MaxHealth = maxHealth > 0f ? maxHealth : 0f;
            m_Health = m_MaxHealth;

            if (armor != null)
            {
                m_ArmorLevel = ArmorTiers.ClampLevel(armor.ProtectionLevel);
                m_ArmorMaxDurability = armor.MaxDurability > 0f ? armor.MaxDurability : 0f;
                m_ArmorWearFactor = armor.WearFactor > 0f ? armor.WearFactor : 0f;
                m_ArmorDurability = m_ArmorMaxDurability;
            }
        }

        /// <summary>运行时标识。</summary>
        public int Id
        {
            get { return m_Id; }
        }

        /// <summary>最大生命值。</summary>
        public float MaxHealth
        {
            get { return m_MaxHealth; }
        }

        /// <summary>当前生命值。</summary>
        public float Health
        {
            get { return m_Health; }
        }

        /// <summary>是否还活着。</summary>
        public bool IsAlive
        {
            get { return m_Health > 0f; }
        }

        /// <summary>当前护甲耐久。</summary>
        public float ArmorDurability
        {
            get { return m_ArmorDurability; }
        }

        /// <summary>当前护甲快照，供伤害结算读取。</summary>
        public ArmorSnapshot GetArmorSnapshot()
        {
            return new ArmorSnapshot(m_ArmorLevel, m_ArmorDurability, m_ArmorMaxDurability, m_ArmorWearFactor);
        }

        /// <summary>
        /// 承受一次伤害。
        /// </summary>
        /// <param name="baseDamage">武器基础伤害。</param>
        /// <param name="penetration">弹药穿透力。</param>
        /// <param name="isCritical">是否暴击命中。</param>
        /// <param name="tuning">全局调参，可为 null。</param>
        /// <returns>本次结算的详细结果。</returns>
        /// <remarks>
        /// 已经死亡的单位不再承受伤害：否则一次连发会在同一帧里"打穿"尸体，
        /// 让击杀统计与护甲损耗都变得无法解释。
        /// </remarks>
        public DamageOutcome ApplyDamage(
            float baseDamage,
            float penetration,
            bool isCritical,
            CombatTuning tuning)
        {
            if (!IsAlive)
            {
                return new DamageOutcome(0f, 0f, 0f);
            }

            var request = new DamageRequest(baseDamage, penetration, GetArmorSnapshot(), isCritical);
            var outcome = DamageCalculator.Resolve(request, tuning);

            m_Health -= outcome.Damage;
            if (m_Health < 0f)
            {
                m_Health = 0f;
            }

            if (outcome.ArmorDamage > 0f)
            {
                m_ArmorDurability -= outcome.ArmorDamage;
                if (m_ArmorDurability < 0f)
                {
                    m_ArmorDurability = 0f;
                }
            }

            return outcome;
        }

        /// <summary>直接设置生命值。供调试与测试使用。</summary>
        public void SetHealth(float value)
        {
            m_Health = value < 0f ? 0f : value > m_MaxHealth ? m_MaxHealth : value;
        }

        public override string ToString()
        {
            return $"Combatant#{m_Id}(生命={m_Health:F0}/{m_MaxHealth:F0}, 甲={m_ArmorLevel}级/{m_ArmorDurability:F0})";
        }
    }
}
