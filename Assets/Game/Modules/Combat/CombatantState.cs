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

        private float m_Health;

        /// <summary>一套护甲的运行时状态。</summary>
        /// <remarks>
        /// 用结构体把「等级 / 耐久上限 / 磨损系数 / 当前耐久」捆在一起，
        /// 是因为头部与身体各需要一份：分成八个字段会很容易出现「改了一半」的错误。
        /// </remarks>
        private struct ArmorState
        {
            public int Level;
            public float MaxDurability;
            public float WearFactor;
            public float Durability;

            /// <summary>由护甲参数构造。参数为 null 时得到一套「无甲」。</summary>
            public static ArmorState From(IArmorStats stats)
            {
                var state = default(ArmorState);
                if (stats == null)
                {
                    return state;
                }

                state.Level = ArmorTiers.ClampLevel(stats.ProtectionLevel);
                state.MaxDurability = stats.MaxDurability > 0f ? stats.MaxDurability : 0f;
                state.WearFactor = stats.WearFactor > 0f ? stats.WearFactor : 0f;
                state.Durability = state.MaxDurability;
                return state;
            }

            /// <summary>换算成伤害计算用的快照。</summary>
            public ArmorSnapshot ToSnapshot()
            {
                return new ArmorSnapshot(Level, Durability, MaxDurability, WearFactor);
            }
        }

        /// <summary>身体护甲（命中躯干时生效）。</summary>
        private ArmorState m_BodyArmor;

        /// <summary>头部护甲（命中上部位时生效）。</summary>
        private ArmorState m_HeadArmor;

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

            m_BodyArmor = ArmorState.From(armor);
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
            get { return m_BodyArmor.Durability; }
        }

        /// <summary>当前头部护甲耐久。</summary>
        public float HeadArmorDurability
        {
            get { return m_HeadArmor.Durability; }
        }

        /// <summary>当前身体护甲快照，供伤害结算读取。</summary>
        public ArmorSnapshot GetArmorSnapshot()
        {
            return m_BodyArmor.ToSnapshot();
        }

        /// <summary>当前头部护甲快照。</summary>
        public ArmorSnapshot GetHeadArmorSnapshot()
        {
            return m_HeadArmor.ToSnapshot();
        }

        /// <summary>
        /// 重新设置护甲，并按当前耐久上限重置耐久。
        /// </summary>
        /// <param name="head">头部护甲，可为 null。</param>
        /// <param name="body">身体护甲，可为 null。</param>
        /// <remarks>
        /// 战局中换装（从箱子里捡到防弹背心并穿上）必须走这里：
        /// 护甲在创建单位时读一次、之后永不更新的写法，
        /// 会让「捡到并穿上」这件事完全不生效——而且没有任何报错。
        /// </remarks>
        public void SetArmor(IArmorStats head, IArmorStats body)
        {
            m_HeadArmor = ArmorState.From(head);
            m_BodyArmor = ArmorState.From(body);
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

            // 命中上部位（暴击）由头盔挡，其余由身体护甲挡。
            var request = new DamageRequest(
                baseDamage,
                penetration,
                isCritical ? GetHeadArmorSnapshot() : GetArmorSnapshot(),
                isCritical);
            var outcome = DamageCalculator.Resolve(request, tuning);

            m_Health -= outcome.Damage;
            if (m_Health < 0f)
            {
                m_Health = 0f;
            }

            if (outcome.ArmorDamage > 0f)
            {
                // 磨损记在真正挡下这一击的那一套护甲上。
                var damaged = isCritical ? m_HeadArmor : m_BodyArmor;
                damaged.Durability -= outcome.ArmorDamage;
                if (damaged.Durability < 0f)
                {
                    damaged.Durability = 0f;
                }

                if (isCritical)
                {
                    m_HeadArmor = damaged;
                }
                else
                {
                    m_BodyArmor = damaged;
                }
            }

            return outcome;
        }

        /// <summary>直接设置生命值。供调试与测试使用。</summary>
        public void SetHealth(float value)
        {
            m_Health = value < 0f ? 0f : value > m_MaxHealth ? m_MaxHealth : value;
        }

        /// <summary>
        /// 恢复生命值。
        /// </summary>
        /// <param name="amount">想要恢复的量。非正值会被忽略。</param>
        /// <returns>实际恢复量（会被生命上限截断）。</returns>
        /// <remarks>
        /// <para>M4 的用途是 AI 撤退阶段的自我恢复：撤退状态必须能真的把血补回来，
        /// 否则"生命过低 → 撤退 → 恢复 → 再次交战"这条链路永远走不通。</para>
        /// <para>死亡单位不会被治疗：<see cref="IsAlive"/> 为 false 时直接返回 0，
        /// 这样后续加入复活机制时不会出现"医疗包把尸体救活但状态机没重置"的怪象。</para>
        /// </remarks>
        public float Heal(float amount)
        {
            if (amount <= 0f || !IsAlive)
            {
                return 0f;
            }

            var before = m_Health;
            m_Health += amount;
            if (m_Health > m_MaxHealth)
            {
                m_Health = m_MaxHealth;
            }

            return m_Health - before;
        }

        public override string ToString()
        {
            return $"Combatant#{m_Id}(生命={m_Health:F0}/{m_MaxHealth:F0}, "
                + $"甲={m_BodyArmor.Level}级/{m_BodyArmor.Durability:F0}, "
                + $"盔={m_HeadArmor.Level}级/{m_HeadArmor.Durability:F0})";
        }
    }
}
