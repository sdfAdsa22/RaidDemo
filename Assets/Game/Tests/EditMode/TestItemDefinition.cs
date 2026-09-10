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
            RarityTier rarity = RarityTier.Common)
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
        public override string ToString()
        {
            return $"TestItemDefinition({Id})";
        }
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
