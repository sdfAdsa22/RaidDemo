namespace RaidDemo.Data
{
    /// <summary>
    /// 射击模式。
    /// </summary>
    /// <remarks>
    /// 放在数据层而不是战斗层，是因为它同时被两处使用：
    /// 武器参数资产（内容层）要序列化它，武器运行时（战斗层）要按它分支。
    /// 放在公共的低层可以避免内容层反向依赖战斗层。
    /// </remarks>
    public enum WeaponFireMode
    {
        /// <summary>单发：每次按下射一发。</summary>
        Single = 0,

        /// <summary>连发：每次按下射固定发数，中途松开不影响已开始的连发。</summary>
        Burst,

        /// <summary>全自动：按住持续射击。</summary>
        Auto,
    }

    /// <summary>
    /// 武器参数契约。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="IItemDefinition"/> 同样的思路：战斗规则只依赖这个接口，
    /// 不依赖 ScriptableObject。测试可以用几行代码造出一把参数精确可控的武器，
    /// 而不需要创建资源文件。</para>
    ///
    /// <para>真实实现是内容层的 <c>WeaponStats</c> 资产。</para>
    /// </remarks>
    public interface IWeaponStats
    {
        /// <summary>单发基础伤害（未被护甲减免前的值）。</summary>
        float BaseDamage { get; }

        /// <summary>射速（发/分钟）。射击间隔由它换算：60 除以射速。</summary>
        float RoundsPerMinute { get; }

        /// <summary>射击模式。</summary>
        WeaponFireMode FireMode { get; }

        /// <summary>连发模式下一次按下的发数。单发与全自动模式忽略此值。</summary>
        int BurstCount { get; }

        /// <summary>弹匣容量（发）。</summary>
        int MagazineCapacity { get; }

        /// <summary>
        /// 口径标识。换弹时按它匹配背包里的弹药。
        /// </summary>
        /// <remarks>
        /// 用字符串而不是枚举：口径属于内容设计，新增口径不应该要求改代码。
        /// 匹配采用序数比较，因此口径标识必须精确一致。
        /// </remarks>
        string CaliberId { get; }

        /// <summary>换弹耗时（秒）。</summary>
        float ReloadSeconds { get; }

        /// <summary>静止时的基础散布（度）。</summary>
        float BaseSpreadDegrees { get; }

        /// <summary>每开一枪累加的散布（度）。</summary>
        float SpreadPerShotDegrees { get; }

        /// <summary>散布上限（度）。连射再久也不会超过它。</summary>
        float MaxSpreadDegrees { get; }

        /// <summary>停火后每秒回落的散布（度/秒）。</summary>
        float SpreadRecoveryPerSecond { get; }

        /// <summary>有效射程（米）。同时用于限制射线检测距离与准星的最大显示距离。</summary>
        float RangeMeters { get; }
    }

    /// <summary>
    /// 弹药参数契约。
    /// </summary>
    /// <remarks>
    /// 弹药只描述两件事：它属于哪个口径、以及它能穿透多厚的护甲。
    /// 伤害由武器决定，不放在弹药上——这样"换一种子弹"只影响穿透，
    /// 玩家不需要重新理解伤害数字，符合"可读性优先"的设计原则。
    /// </remarks>
    public interface IAmmoStats
    {
        /// <summary>口径标识。必须与武器上的值完全一致才能装填。</summary>
        string CaliberId { get; }

        /// <summary>穿透力。与目标护甲值比较后得到穿透系数，见伤害公式。</summary>
        float Penetration { get; }
    }

    /// <summary>
    /// 护甲参数契约。
    /// </summary>
    /// <remarks>
    /// 注意这里**没有减伤率**：减伤由防护等级经全局表换算（见 <c>ArmorTiers</c>）。
    /// 让等级而非资产决定减伤，玩家才能记住"3 级甲就是 50%"。
    /// </remarks>
    public interface IArmorStats
    {
        /// <summary>防护等级，取值 1 到 4。0 表示无护甲，不应出现在资产里。</summary>
        int ProtectionLevel { get; }

        /// <summary>最大耐久。耐久归零后护甲不再提供任何减伤。</summary>
        float MaxDurability { get; }

        /// <summary>磨损系数：每次受击扣除的耐久 = 基础伤害 × 该系数。</summary>
        float WearFactor { get; }
    }
}
