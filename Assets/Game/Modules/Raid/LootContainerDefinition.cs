using RaidDemo.Data;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 战利品容器的种类。
    /// </summary>
    /// <remarks>
    /// 灰盒阶段用 ID 字符串而不是枚举来标识具体容器，
    /// 枚举只用来表达「这是哪一类箱子」，供界面配色与以后的音效使用。
    /// 新增成员一律追加到末尾，避免已序列化的数据错位。
    /// </remarks>
    public enum ContainerFlavor
    {
        /// <summary>通用补给箱。</summary>
        Crate = 0,

        /// <summary>弹药箱。</summary>
        AmmoBox = 1,

        /// <summary>医疗箱。</summary>
        MedicalBox = 2,

        /// <summary>武器架。</summary>
        WeaponRack = 3,

        /// <summary>保险柜：全图数量最少、价值最高。</summary>
        Safe = 4,
    }

    /// <summary>
    /// 一种战利品容器的定义：多大、什么类型、抽哪张表。
    /// </summary>
    /// <remarks>
    /// 容器尺寸属于玩法而非美术：它直接决定「这个箱子值得为它冒险吗」。
    /// 保险柜是 4x4，意味着它能装下一件 4x4 的突击背包，
    /// 这是刻意留出的「一发入魂」可能性。
    /// </remarks>
    public sealed class LootContainerDefinition
    {
        /// <summary>创建容器定义。</summary>
        /// <param name="id">稳定标识，场景标记里写的就是它。</param>
        /// <param name="displayName">显示名，出现在交互提示与面板标题上。</param>
        /// <param name="flavor">容器类型。</param>
        /// <param name="gridWidth">内部网格列数。</param>
        /// <param name="gridHeight">内部网格行数。</param>
        /// <param name="table">掉落表。</param>
        public LootContainerDefinition(
            string id,
            string displayName,
            ContainerFlavor flavor,
            int gridWidth,
            int gridHeight,
            LootTable table)
        {
            Id = id;
            DisplayName = displayName;
            Flavor = flavor;
            GridSize = new GridSize(gridWidth, gridHeight);
            Table = table;
        }

        /// <summary>稳定标识。</summary>
        public string Id { get; }

        /// <summary>显示名。</summary>
        public string DisplayName { get; }

        /// <summary>容器类型。</summary>
        public ContainerFlavor Flavor { get; }

        /// <summary>内部网格尺寸。</summary>
        public GridSize GridSize { get; }

        /// <summary>掉落表。</summary>
        public LootTable Table { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{DisplayName}({Id}) {GridSize.Width}x{GridSize.Height}";
        }
    }
}
