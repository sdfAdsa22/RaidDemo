namespace RaidDemo.Data
{
    /// <summary>
    /// 物品的静态定义契约：一类物品"是什么"。
    /// </summary>
    /// <remarks>
    /// <para>为什么用接口而不是直接依赖 ScriptableObject：规则层（网格、堆叠、负重）
    /// 只需要知道上表这几个字段，不需要知道 UnityEngine。
    /// 把契约抽出来之后，规则代码可以在 EditMode 测试中用几行代码造出一个假物品，
    /// 无需创建资源文件、无需加载场景。</para>
    ///
    /// <para>真实实现是 <c>RaidDemo.Data.Content</c> 里的 ScriptableObject；
    /// 测试实现是测试程序集里的 <c>TestItemDefinition</c>。两者对规则层完全等价。</para>
    ///
    /// <para><b>静态定义与运行时状态严格分离</b>：本接口只描述"所有 AK-74 共有的东西"，
    /// 一件具体的 AK-74 的当前状态（数量、是否旋转、将来的装弹数与耐久）在
    /// <see cref="ItemInstance"/> 上。</para>
    /// </remarks>
    public interface IItemDefinition
    {
        /// <summary>
        /// 稳定标识，形如 <c>category.name.variant</c>，例如 <c>weapon.rifle.ak74</c>。
        /// </summary>
        /// <remarks>
        /// 存档只记录这个字符串。因此重命名显示名、换图标、调数值都不会破坏旧存档；
        /// 反过来，**一经发布就不得修改**——改了等于删掉旧物品、新增一件新物品。
        /// </remarks>
        string Id { get; }

        /// <summary>显示名称（中文），用于 UI 与日志。</summary>
        string DisplayName { get; }

        /// <summary>分类。决定可装备到哪个槽位、以及是否参与价值区间校验。</summary>
        ItemCategory Category { get; }

        /// <summary>稀有度档位。只影响展示与定价，不影响战斗力。</summary>
        RarityTier Rarity { get; }

        /// <summary>未旋转时占用的格子尺寸。</summary>
        GridSize GridSize { get; }

        /// <summary>单个物品的重量（千克）。整堆重量由数量乘得。</summary>
        float WeightKg { get; }

        /// <summary>单个物品的基础价值（游戏币）。商人售价以此为基准。</summary>
        int BaseValue { get; }

        /// <summary>
        /// 单格最大堆叠数量。
        /// </summary>
        /// <remarks>
        /// 必须按物品逐一配置而不是全局取一个常量：各类别的合理堆叠量差了两个数量级
        /// （弹药上百发、医疗包个位数、值钱小物件固定 1 件），
        /// 任何全局折中值都会对大多数类别是错的。
        /// </remarks>
        int MaxStack { get; }

        /// <summary>是否允许旋转 90 度放置。长条形物品允许旋转，方形物品旋转无意义。</summary>
        bool CanRotate { get; }

        /// <summary>本身是否是一个容器（例如背包）。</summary>
        bool IsContainer { get; }

        /// <summary>作为容器时的内部网格尺寸。非容器时无意义。</summary>
        GridSize ContainerGridSize { get; }
    }
}
