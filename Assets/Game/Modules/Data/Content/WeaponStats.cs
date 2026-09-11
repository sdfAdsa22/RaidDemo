using UnityEngine;

namespace RaidDemo.Data
{
    /// <summary>
    /// 武器参数资产。
    /// </summary>
    /// <remarks>
    /// <para>它作为 <see cref="ItemBehavior"/> 挂在武器的物品定义上，
    /// 因此不需要在 <see cref="ItemDefinition"/> 上加任何战斗专用字段——
    /// 一个物品一个行为，扩展新品类只需要新增一个行为子类。</para>
    ///
    /// <para>参数的单位都写在字段注释里。射击手感几乎全部由这张表决定，
    /// 因此每个数值都注明了"调大调小会怎样"，方便后续按体感调参。</para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Weapon_",
        menuName = "RaidDemo/武器参数",
        order = 20)]
    public sealed class WeaponStats : ItemBehavior, IWeaponStats
    {
        /// <summary>单发基础伤害。调高会让交火更快结束，也是唯一直接影响"几枪打死"的值。</summary>
        [SerializeField] private float m_BaseDamage = 25f;

        /// <summary>射速（发/分钟）。射击间隔 = 60 / 该值，因此 600 对应 0.1 秒一发。</summary>
        [SerializeField] private float m_RoundsPerMinute = 600f;

        /// <summary>射击模式。</summary>
        [SerializeField] private WeaponFireMode m_FireMode = WeaponFireMode.Auto;

        /// <summary>连发发数。仅在连发模式下生效。</summary>
        [SerializeField] private int m_BurstCount = 3;

        /// <summary>弹匣容量（发）。</summary>
        [SerializeField] private int m_MagazineCapacity = 30;

        /// <summary>口径标识，必须与弹药的标识完全一致才能装填。</summary>
        [SerializeField] private string m_CaliberId = "9x19";

        /// <summary>换弹耗时（秒）。调大是削弱火力的最直接手段。</summary>
        [SerializeField] private float m_ReloadSeconds = 2.2f;

        /// <summary>基础散布（度）。静止时的最小散布，也是停火后回落的终点。</summary>
        [SerializeField] private float m_BaseSpreadDegrees = 1.5f;

        /// <summary>每开一枪累加的散布（度）。它决定"连射多久开始打不准"。</summary>
        [SerializeField] private float m_SpreadPerShotDegrees = 0.5f;

        /// <summary>散布上限（度）。</summary>
        [SerializeField] private float m_MaxSpreadDegrees = 6f;

        /// <summary>停火后散布回落速度（度/秒）。</summary>
        [SerializeField] private float m_SpreadRecoveryPerSecond = 4f;

        /// <summary>有效射程（米）。同时决定射线检测距离与准星的最大显示距离。</summary>
        [SerializeField] private float m_RangeMeters = 40f;

        /// <inheritdoc />
        public float BaseDamage
        {
            get { return m_BaseDamage; }
        }

        /// <inheritdoc />
        public float RoundsPerMinute
        {
            get { return m_RoundsPerMinute; }
        }

        /// <inheritdoc />
        public WeaponFireMode FireMode
        {
            get { return m_FireMode; }
        }

        /// <inheritdoc />
        public int BurstCount
        {
            get { return m_BurstCount; }
        }

        /// <inheritdoc />
        public int MagazineCapacity
        {
            get { return m_MagazineCapacity; }
        }

        /// <inheritdoc />
        public string CaliberId
        {
            get { return m_CaliberId; }
        }

        /// <inheritdoc />
        public float ReloadSeconds
        {
            get { return m_ReloadSeconds; }
        }

        /// <inheritdoc />
        public float BaseSpreadDegrees
        {
            get { return m_BaseSpreadDegrees; }
        }

        /// <inheritdoc />
        public float SpreadPerShotDegrees
        {
            get { return m_SpreadPerShotDegrees; }
        }

        /// <inheritdoc />
        public float MaxSpreadDegrees
        {
            get { return m_MaxSpreadDegrees; }
        }

        /// <inheritdoc />
        public float SpreadRecoveryPerSecond
        {
            get { return m_SpreadRecoveryPerSecond; }
        }

        /// <inheritdoc />
        public float RangeMeters
        {
            get { return m_RangeMeters; }
        }

        /// <summary>射击间隔（秒）。射速非正时返回一个极大值，避免除零。</summary>
        public float ShotIntervalSeconds
        {
            get { return m_RoundsPerMinute > 0f ? 60f / m_RoundsPerMinute : float.MaxValue; }
        }

        /// <summary>
        /// 校验参数自洽性。
        /// </summary>
        /// <param name="owner">拥有本行为的物品定义。</param>
        /// <returns>自洽返回 null，否则返回中文说明。</returns>
        public override string Validate(ItemDefinition owner)
        {
            if (m_BaseDamage <= 0f)
            {
                return "基础伤害必须大于 0，否则这把枪打不死任何东西。";
            }

            if (m_RoundsPerMinute <= 0f)
            {
                return "射速必须大于 0。";
            }

            if (m_MagazineCapacity <= 0)
            {
                return "弹匣容量必须大于 0。";
            }

            if (string.IsNullOrWhiteSpace(m_CaliberId))
            {
                return "口径标识不能为空，否则这把枪永远无法换弹。";
            }

            if (m_FireMode == WeaponFireMode.Burst && m_BurstCount < 2)
            {
                return "连发模式的发数至少为 2，否则应当使用单发模式。";
            }

            if (m_BaseSpreadDegrees < 0f || m_MaxSpreadDegrees < m_BaseSpreadDegrees)
            {
                return "散布上限不能小于基础散布。";
            }

            if (m_RangeMeters <= 0f)
            {
                return "射程必须大于 0。";
            }

            return null;
        }
    }
}
