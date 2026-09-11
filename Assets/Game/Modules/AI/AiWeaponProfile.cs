using RaidDemo.Data;

namespace RaidDemo.AI
{
    /// <summary>
    /// AI 使用的武器参数。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么 AI 不用玩家的物品资产：</b>玩家武器来自背包里的 <c>ItemDefinition</c>，
    /// 它带着掉落、图标、卖价等一堆 AI 用不到的东西。AI 需要的是同一套**战斗参数契约**
    /// （<see cref="IWeaponStats"/>），因此这里直接实现契约。</para>
    ///
    /// <para>这与灰盒靶子用的 <c>GreyboxArmorStats</c> 是同一个思路：
    /// 规则只依赖契约，因此任何东西都能成为契约的实现。这样 AI 也能受益于
    /// 玩家武器上的每一项机制——射速、散布扩散、换弹耗时、部分装填——
    /// 而不是另走一条"AI 专用"的简化伤害路径。</para>
    ///
    /// <para>数值刻意弱于玩家的步枪：AI 的优势应当来自数量与位置，
    /// 而不是单发伤害。打得准但打得慢，玩家才有换位与还击的空间。</para>
    /// </remarks>
    public sealed class AiWeaponProfile : IWeaponStats
    {
        /// <summary>单发基础伤害。</summary>
        public float BaseDamage { get; set; } = 9f;

        /// <summary>射速（发/分钟）。</summary>
        public float RoundsPerMinute { get; set; } = 420f;

        /// <summary>射击模式。AI 固定用全自动，由状态机控制点射节奏。</summary>
        public WeaponFireMode FireMode { get; set; } = WeaponFireMode.Auto;

        /// <summary>连发模式下的发数。全自动模式下不生效。</summary>
        public int BurstCount { get; set; } = 1;

        /// <summary>弹匣容量。</summary>
        public int MagazineCapacity { get; set; } = 30;

        /// <summary>
        /// 口径标识。
        /// </summary>
        /// <remarks>AI 的备弹是自带的常数，不来自背包，因此这个字段只用于日志与统计。</remarks>
        public string CaliberId { get; set; } = "caliber_ai_greybox";

        /// <summary>换弹耗时（秒）。比玩家更长：换弹是玩家反打 AI 的窗口。</summary>
        public float ReloadSeconds { get; set; } = 3.2f;

        /// <summary>静止时的基础散布（度）。</summary>
        public float BaseSpreadDegrees { get; set; } = 3.5f;

        /// <summary>每开一枪累加的散布（度）。</summary>
        public float SpreadPerShotDegrees { get; set; } = 0.55f;

        /// <summary>散布上限（度）。</summary>
        public float MaxSpreadDegrees { get; set; } = 8f;

        /// <summary>停火后每秒回落的散布（度/秒）。</summary>
        public float SpreadRecoveryPerSecond { get; set; } = 5f;

        /// <summary>有效射程（米）。</summary>
        public float RangeMeters { get; set; } = 32f;

        /// <summary>
        /// 创建一把默认的灰盒 AI 步枪。
        /// </summary>
        /// <remarks>
        /// 提供工厂方法而不是要求调用方逐个赋值：装配代码里少写一行赋值，
        /// 就少一次"AI 打不出伤害"的排查。需要调数值时在返回值上继续设置即可。
        /// </remarks>
        public static AiWeaponProfile CreateGreyboxRifle()
        {
            return new AiWeaponProfile();
        }
    }
}
