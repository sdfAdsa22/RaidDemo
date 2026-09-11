namespace RaidDemo.Combat
{
    /// <summary>
    /// 目标护甲状态的快照。
    /// </summary>
    /// <remarks>
    /// <para>伤害结算刻意读取快照而不是护甲对象本身：这让计算变成纯函数，
    /// 同一次结算不会因为中途改了护甲而出现前后不一致的结果。</para>
    /// <para>等级为 0 表示没有护甲，这是最常见的分支，因此用它表示无甲而不是另一个布尔标记。</para>
    /// </remarks>
    public readonly struct ArmorSnapshot
    {
        /// <summary>创建护甲快照。</summary>
        /// <param name="level">防护等级，0 表示无护甲。</param>
        /// <param name="durability">当前耐久。</param>
        /// <param name="maxDurability">最大耐久。</param>
        /// <param name="wearFactor">磨损系数。</param>
        public ArmorSnapshot(int level, float durability, float maxDurability, float wearFactor)
        {
            Level = level;
            Durability = durability;
            MaxDurability = maxDurability;
            WearFactor = wearFactor;
        }

        /// <summary>防护等级，0 表示无护甲。</summary>
        public int Level { get; }

        /// <summary>当前耐久。</summary>
        public float Durability { get; }

        /// <summary>最大耐久。</summary>
        public float MaxDurability { get; }

        /// <summary>磨损系数。</summary>
        public float WearFactor { get; }

        /// <summary>完全没有护甲的快照，用于无甲目标。</summary>
        public static ArmorSnapshot None
        {
            get { return new ArmorSnapshot(0, 0f, 0f, 0f); }
        }

        /// <summary>是否处于有护甲且还没打坏的状态。</summary>
        public bool IsEffective
        {
            get { return Level > 0 && MaxDurability > 0f && Durability > 0f; }
        }
    }

    /// <summary>
    /// 一次伤害结算的输入。
    /// </summary>
    public readonly struct DamageRequest
    {
        /// <summary>创建伤害请求。</summary>
        /// <param name="baseDamage">武器基础伤害。</param>
        /// <param name="penetration">弹药穿透力。</param>
        /// <param name="armor">目标护甲快照。</param>
        /// <param name="isCritical">是否暴击命中。</param>
        public DamageRequest(float baseDamage, float penetration, ArmorSnapshot armor, bool isCritical = false)
        {
            BaseDamage = baseDamage;
            Penetration = penetration;
            Armor = armor;
            IsCritical = isCritical;
        }

        /// <summary>武器基础伤害。</summary>
        public float BaseDamage { get; }

        /// <summary>弹药穿透力。</summary>
        public float Penetration { get; }

        /// <summary>目标护甲快照。</summary>
        public ArmorSnapshot Armor { get; }

        /// <summary>是否暴击命中。</summary>
        public bool IsCritical { get; }
    }

    /// <summary>
    /// 一次伤害结算的输出。
    /// </summary>
    /// <remarks>
    /// 除了最终伤害，还带出护甲耐久扣减与穿透系数——
    /// 后两者是排查"为什么这一枪伤害不对"时唯一有用的线索，丢弃它们就只能靠猜。
    /// </remarks>
    public readonly struct DamageOutcome
    {
        /// <summary>创建结算结果。</summary>
        /// <param name="damage">最终伤害。</param>
        /// <param name="armorDamage">本次造成的护甲耐久损失。</param>
        /// <param name="penetrationFactor">穿透系数。</param>
        public DamageOutcome(float damage, float armorDamage, float penetrationFactor)
        {
            Damage = damage;
            ArmorDamage = armorDamage;
            PenetrationFactor = penetrationFactor;
        }

        /// <summary>最终伤害。永远是 0 或正数。</summary>
        public float Damage { get; }

        /// <summary>本次造成的护甲耐久损失。无护甲或护甲已打坏时为 0。</summary>
        public float ArmorDamage { get; }

        /// <summary>穿透系数，取值 0 到 1。0 表示减伤全额生效，1 表示无视该层减伤。</summary>
        public float PenetrationFactor { get; }

        /// <summary>穿透系数是否达到了 1，即完全无视了护甲减伤。</summary>
        public bool IgnoredArmor
        {
            get { return PenetrationFactor >= 1f; }
        }

        public override string ToString()
        {
            return $"DamageOutcome(伤害={Damage:F1}, 护甲损耗={ArmorDamage:F1}, 穿透={PenetrationFactor:F2})";
        }
    }
}
