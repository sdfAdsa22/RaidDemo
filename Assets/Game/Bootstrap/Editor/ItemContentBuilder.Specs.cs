using RaidDemo.Data;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 物品内容生成器的数据部分：一条物品定义的形状，以及初始物品表。
    /// </summary>
    /// <remarks>
    /// <para>从主文件拆出来的原因有两个：主文件触及 400 行上限；这份表本身就是设计文档的一部分，
    /// 单独成文件之后，"调整数值只改哪一页"是一眼可见的。</para>
    ///
    /// <para><b>重新生成会覆盖同名资产</b>，因此不要在生成出来的资产上手工改数值——要改数值请改这张表。</para>
    /// </remarks>
    public static partial class ItemContentBuilder
    {
        /// <summary>一条物品定义的数据。</summary>
        private readonly struct ItemSpec
        {
            public ItemSpec(
                string id,
                string displayName,
                ItemCategory category,
                RarityTier rarity,
                int width,
                int height,
                float weightKg,
                int baseValue,
                int maxStack,
                bool canRotate,
                int containerWidth = 0,
                int containerHeight = 0,
                string description = null)
            {
                Id = id;
                DisplayName = displayName;
                Category = category;
                Rarity = rarity;
                Width = width;
                Height = height;
                WeightKg = weightKg;
                BaseValue = baseValue;
                MaxStack = maxStack;
                CanRotate = canRotate;
                ContainerWidth = containerWidth;
                ContainerHeight = containerHeight;
                Description = description;
            }

            public string Id { get; }

            public string DisplayName { get; }

            public ItemCategory Category { get; }

            public RarityTier Rarity { get; }

            public int Width { get; }

            public int Height { get; }

            public float WeightKg { get; }

            public int BaseValue { get; }

            public int MaxStack { get; }

            public bool CanRotate { get; }

            public int ContainerWidth { get; }

            public int ContainerHeight { get; }

            /// <summary>图鉴与详情面板的一句说明；可以为空。</summary>
            public string Description { get; }

            /// <summary>是否是一个容器。</summary>
            public bool IsContainer
            {
                get { return ContainerWidth > 0 && ContainerHeight > 0; }
            }
        }

        /// <summary>
        /// 初始物品表。
        /// </summary>
        /// <remarks>
        /// <para>覆盖每个分类至少一件，目的是让背包系统的每条分支都能在灰盒里被走到：
        /// 可堆叠的弹药与材料、不可堆叠的装备、占多格并可旋转的长条武器、
        /// 带内部空间但不可堆叠的背包。</para>
        /// <para>重量刻意定得偏高，让灰盒里的物品全捡一遍就能进入重装乃至超重状态，
        /// 否则负重系统在演示时永远看不出效果。</para>
        /// <para>description 是图鉴详情里的那句说明：一句话说清"它是什么、用来干什么"。</para>
        /// </remarks>
        private static readonly ItemSpec[] s_Specs =
        {
            new ItemSpec("ammo.9x19.standard", "9x19 标准弹", ItemCategory.Ammo, RarityTier.Common,
                1, 1, 0.012f, 8, 120, canRotate: false,
                description: "最常见的手枪弹，PM 手枪与冲锋枪都使用它。"),
            new ItemSpec("ammo.5.45.standard", "5.45 标准弹", ItemCategory.Ammo, RarityTier.Uncommon,
                1, 1, 0.014f, 25, 90, canRotate: false,
                description: "AK-74 的口粮。穿透力比手枪弹高出一档，别拿它当零钱花。"),
            new ItemSpec("medical.bandage.small", "小绷带", ItemCategory.Medical, RarityTier.Common,
                1, 1, 0.1f, 400, 5, canRotate: false,
                description: "临时止血用。回复量不大，但只要 1.5 秒，交火间隙也来得及。"),
            new ItemSpec("medical.kit.field", "野战医疗包", ItemCategory.Medical, RarityTier.Rare,
                1, 2, 0.6f, 9000, 1, canRotate: true,
                description: "野战级医疗包，一次能把伤势拉回安全线，代价是 3 秒原地站定。"),
            new ItemSpec("weapon.pistol.pm", "PM 手枪", ItemCategory.Weapon, RarityTier.Common,
                2, 1, 0.7f, 1800, 1, canRotate: true,
                description: "轻巧的备用武器，8 米内可靠，适合在主武器换弹时救急。"),
            new ItemSpec("weapon.rifle.ak74", "AK-74 步枪", ItemCategory.Weapon, RarityTier.Uncommon,
                3, 1, 3.2f, 7200, 1, canRotate: true,
                description: "主力步枪，三连发点射，12 米内压制力最强。"),
            new ItemSpec("armor.helmet.steel", "钢盔", ItemCategory.Helmet, RarityTier.Uncommon,
                2, 2, 1.2f, 4500, 1, canRotate: false,
                description: "钢制头盔，护住头部的最后一道保险。耐久见底时请及时更换。"),
            new ItemSpec("armor.vest.plate", "防弹背心", ItemCategory.BodyArmor, RarityTier.Rare,
                2, 3, 5.0f, 15000, 1, canRotate: true,
                description: "板式防弹衣，躯干防护的核心装备，重量也相当可观。"),
            new ItemSpec("backpack.small", "小型背包", ItemCategory.Backpack, RarityTier.Common,
                3, 3, 1.0f, 1200, 1, canRotate: false, containerWidth: 6, containerHeight: 6,
                description: "小型背包，6×6 的内部空间，够装一趟轻装搜刮的收获。"),
            new ItemSpec("backpack.raider", "突击背包", ItemCategory.Backpack, RarityTier.Rare,
                4, 4, 2.5f, 12000, 1, canRotate: false, containerWidth: 7, containerHeight: 7,
                description: "突击背包，7×7 的大容量，适合把一整间厂房搬回家。"),
            new ItemSpec("loot.bolt.copper", "铜螺栓", ItemCategory.Loot, RarityTier.Common,
                1, 1, 0.02f, 600, 20, canRotate: false,
                description: "铜螺栓。工地上随处可见，值一点小钱，胜在不占地方。"),
            new ItemSpec("loot.canister.fuel", "燃料罐", ItemCategory.Loot, RarityTier.Rare,
                1, 2, 2.4f, 11000, 1, canRotate: true,
                description: "满装燃料罐，工业区的硬通货。占两格，但值得为它绕路。"),
            new ItemSpec("loot.watch.gold", "金表", ItemCategory.Loot, RarityTier.Epic,
                1, 1, 0.05f, 32000, 1, canRotate: false,
                description: "金表。整局最值钱的单件，拿到它就该认真考虑撤离了。"),
        };
    }
}
