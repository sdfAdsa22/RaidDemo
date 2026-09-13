using RaidDemo.Data;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 供测试使用的物品定义替身。
    /// </summary>
    /// <remarks>
    /// <para>背包规则只依赖 <see cref="IItemDefinition"/> 接口，因此测试里不需要创建任何
    /// ScriptableObject 资产、不需要加载场景，用几行代码就能造出一件物品。</para>
    ///
    /// <para>这正是"契约与资产分离"的价值所在：如果规则层直接依赖 ScriptableObject，
    /// 那么每一个背包用例都要先写资源文件，测试会慢到没人愿意维护。</para>
    /// </remarks>
    internal sealed class TestItemDefinition : IItemDefinition
    {
        /// <summary>创建一个测试用物品定义。</summary>
        /// <param name="id">稳定标识，同时作为默认显示名。</param>
        /// <param name="category">分类。</param>
        /// <param name="width">未旋转时的宽度（格）。</param>
        /// <param name="height">未旋转时的高度（格）。</param>
        /// <param name="weightKg">单个重量（千克）。</param>
        /// <param name="baseValue">单个价值。</param>
        /// <param name="maxStack">单格堆叠上限。</param>
        /// <param name="canRotate">是否允许旋转。</param>
        /// <param name="containerWidth">作为容器时的内部宽度。</param>
        /// <param name="containerHeight">作为容器时的内部高度。</param>
        /// <param name="rarity">稀有度档位。</param>
        /// <param name="weaponStats">作为武器时的战斗参数，可为空。</param>
        /// <param name="ammoStats">作为弹药时的战斗参数，可为空。</param>
        /// <param name="armorStats">作为护甲时的战斗参数，可为空。</param>
        public TestItemDefinition(
            string id,
            ItemCategory category = ItemCategory.Loot,
            int width = 1,
            int height = 1,
            float weightKg = 1f,
            int baseValue = 100,
            int maxStack = 1,
            bool canRotate = true,
            int containerWidth = 4,
            int containerHeight = 4,
            RarityTier rarity = RarityTier.Common,
            IWeaponStats weaponStats = null,
            IAmmoStats ammoStats = null,
            IArmorStats armorStats = null)
        {
            Id = id;
            DisplayName = id;
            Category = category;
            GridSize = new GridSize(width, height);
            WeightKg = weightKg;
            BaseValue = baseValue;
            MaxStack = maxStack;
            CanRotate = canRotate;
            ContainerGridSize = new GridSize(containerWidth, containerHeight);
            Rarity = rarity;
            IsContainer = category == ItemCategory.Backpack;
            WeaponStats = weaponStats;
            AmmoStats = ammoStats;
            ArmorStats = armorStats;
        }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public string DisplayName { get; }

        /// <inheritdoc />
        public ItemCategory Category { get; }

        /// <inheritdoc />
        public RarityTier Rarity { get; }

        /// <inheritdoc />
        public GridSize GridSize { get; }

        /// <inheritdoc />
        public float WeightKg { get; }

        /// <inheritdoc />
        public int BaseValue { get; }

        /// <inheritdoc />
        public int MaxStack { get; }

        /// <inheritdoc />
        public bool CanRotate { get; }

        /// <inheritdoc />
        public bool IsContainer { get; }

        /// <inheritdoc />
        public GridSize ContainerGridSize { get; }

        /// <inheritdoc />
        public IWeaponStats WeaponStats { get; }

        /// <inheritdoc />
        public IAmmoStats AmmoStats { get; }

        /// <inheritdoc />
        public IArmorStats ArmorStats { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"TestItemDefinition({Id})";
        }
    }

    /// <summary>
    /// 供测试使用的武器参数替身。
    /// </summary>
    /// <remarks>
    /// 所有字段都可以在构造时精确指定，因此"射速 600、弹匣 30、基础散布 1.5 度"
    /// 这类条件在测试里是写死的确定值，不会因为资产被改动而失效。
    /// </remarks>
    internal sealed class TestWeaponStats : IWeaponStats
    {
        /// <summary>创建武器参数。</summary>
        public TestWeaponStats(
            float baseDamage = 25f,
            float roundsPerMinute = 600f,
            WeaponFireMode fireMode = WeaponFireMode.Auto,
            int burstCount = 3,
            int magazineCapacity = 30,
            string caliberId = "9x19",
            float reloadSeconds = 2f,
            float baseSpreadDegrees = 0f,
            float spreadPerShotDegrees = 0f,
            float maxSpreadDegrees = 0f,
            float spreadRecoveryPerSecond = 0f,
            float rangeMeters = 40f,
            int pelletCount = 1,
            float pelletSpreadDegrees = 0f)
        {
            BaseDamage = baseDamage;
            RoundsPerMinute = roundsPerMinute;
            FireMode = fireMode;
            BurstCount = burstCount;
            MagazineCapacity = magazineCapacity;
            CaliberId = caliberId;
            ReloadSeconds = reloadSeconds;
            BaseSpreadDegrees = baseSpreadDegrees;
            SpreadPerShotDegrees = spreadPerShotDegrees;
            MaxSpreadDegrees = maxSpreadDegrees;
            SpreadRecoveryPerSecond = spreadRecoveryPerSecond;
            RangeMeters = rangeMeters;
            PelletCount = pelletCount;
            PelletSpreadDegrees = pelletSpreadDegrees;
        }

        /// <inheritdoc />
        public float BaseDamage { get; }

        /// <inheritdoc />
        public float RoundsPerMinute { get; }

        /// <inheritdoc />
        public WeaponFireMode FireMode { get; }

        /// <inheritdoc />
        public int BurstCount { get; }

        /// <inheritdoc />
        public int MagazineCapacity { get; }

        /// <inheritdoc />
        public string CaliberId { get; }

        /// <inheritdoc />
        public float ReloadSeconds { get; }

        /// <inheritdoc />
        public float BaseSpreadDegrees { get; }

        /// <inheritdoc />
        public float SpreadPerShotDegrees { get; }

        /// <inheritdoc />
        public float MaxSpreadDegrees { get; }

        /// <inheritdoc />
        public float SpreadRecoveryPerSecond { get; }

        /// <inheritdoc />
        public float RangeMeters { get; }

        /// <inheritdoc />
        public int PelletCount { get; }

        /// <inheritdoc />
        public float PelletSpreadDegrees { get; }
    }

    /// <summary>供测试使用的弹药参数替身。</summary>
    internal sealed class TestAmmoStats : IAmmoStats
    {
        /// <summary>创建弹药参数。</summary>
        /// <param name="caliberId">口径标识。</param>
        /// <param name="penetration">穿透力。</param>
        public TestAmmoStats(string caliberId = "9x19", float penetration = 15f)
        {
            CaliberId = caliberId;
            Penetration = penetration;
        }

        /// <inheritdoc />
        public string CaliberId { get; }

        /// <inheritdoc />
        public float Penetration { get; }
    }

    /// <summary>供测试使用的护甲参数替身。</summary>
    internal sealed class TestArmorStats : IArmorStats
    {
        /// <summary>创建护甲参数。</summary>
        /// <param name="protectionLevel">防护等级。</param>
        /// <param name="maxDurability">最大耐久。</param>
        /// <param name="wearFactor">磨损系数。</param>
        public TestArmorStats(int protectionLevel = 2, float maxDurability = 60f, float wearFactor = 0.35f)
        {
            ProtectionLevel = protectionLevel;
            MaxDurability = maxDurability;
            WearFactor = wearFactor;
        }

        /// <inheritdoc />
        public int ProtectionLevel { get; }

        /// <inheritdoc />
        public float MaxDurability { get; }

        /// <inheritdoc />
        public float WearFactor { get; }
    }

    /// <summary>
    /// 供测试使用的独立状态替身。
    /// </summary>
    /// <remarks>
    /// M2 阶段游戏里不存在任何真实的状态载荷，但堆叠规则必须已经支持"状态不同则不可堆叠"。
    /// 这个替身就是用来证明那条规则真的生效，而不是写了一行永远不会被走到的代码。
    /// </remarks>
    internal sealed class TestItemState : ItemState
    {
        private readonly int m_Value;

        /// <summary>创建一个带标记值的状态。</summary>
        /// <param name="value">用于区分状态的标记值。</param>
        public TestItemState(int value)
        {
            m_Value = value;
        }

        /// <inheritdoc />
        public override bool ContentEquals(ItemState other)
        {
            return other is TestItemState typed && typed.m_Value == m_Value;
        }
    }
}
