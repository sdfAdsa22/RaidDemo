namespace RaidDemo.Data
{
    /// <summary>
    /// 物品分类。决定物品能放进哪一类装备槽，也决定它在战利品表里属于哪一档。
    /// </summary>
    /// <remarks>
    /// 分类是**穷举**的：新增一个分类意味着要同时考虑装备槽规则、UI 布局与掉落权重，
    /// 因此不适合做成可扩展的字符串标识。稀有度则相反（见 <see cref="RarityTier"/> 的说明）。
    /// </remarks>
    public enum ItemCategory
    {
        /// <summary>武器。可装备到主武器槽或副武器槽。</summary>
        Weapon = 0,

        /// <summary>弹药。按口径分型号，可堆叠。</summary>
        Ammo,

        /// <summary>医疗用品。绷带、医疗包、注射器。</summary>
        Medical,

        /// <summary>头盔。只能装备到头盔槽。</summary>
        /// <remarks>
        /// 头盔与躯干护甲刻意分成两个分类，而不是合并成一个"护甲"。
        /// 分类是装备槽规则的唯一依据，合并之后规则层就分不清两者，
        /// 会出现"钢盔穿在身上、防弹背心戴在头上"这种荒唐的换装。
        /// </remarks>
        Helmet,

        /// <summary>躯干护甲（防弹背心、板甲衣）。只能装备到护甲槽。</summary>
        BodyArmor,

        /// <summary>背包。装备后决定随身网格的容量。</summary>
        Backpack,

        /// <summary>值钱小物件。这一类是"价值密度"玩法的主要载体。</summary>
        Loot,

        /// <summary>钥匙。配合门锁使用，门锁本身是推迟项（P-04）。</summary>
        Key,

        /// <summary>任务物品。不可出售，离场时结算。</summary>
        Quest,
    }

    /// <summary>
    /// 稀有度档位。描述物品的**价值档位与稀缺程度**，而不是战斗力。
    /// </summary>
    /// <remarks>
    /// <para>五档设计，最高档用金色而不是红色：射击游戏里红色被"敌人、危险、禁用"占用了语义，
    /// 再拿它表示"最值钱"会抢占视觉通道。</para>
    ///
    /// <para><b>红线：稀有度不驱动武器伤害与护甲减伤。</b>那是 M3 的武器数值与 M6 的装备等级线，
    /// 两条轴正交。一旦稀有度等于强度，游戏就退化成刷装备，搜索本身也失去意义——
    /// 玩家只会捡紫色，白色物品变成垃圾，"要不要多拿一件"这个问题随之消失。</para>
    ///
    /// <para>序列化时按名称写盘而不是写整数序号，避免将来在中间插入新档位导致旧存档整体错位。</para>
    /// </remarks>
    public enum RarityTier
    {
        /// <summary>普通（白）。遍地都是：弹药、基础材料、杂物。</summary>
        Common = 0,

        /// <summary>精良（绿）。常见可用：初级装备、常用消耗品。</summary>
        Uncommon,

        /// <summary>稀有（蓝）。值得为它多跑一间房。</summary>
        Rare,

        /// <summary>史诗（紫）。战局目标级，看见就起贪心。</summary>
        Epic,

        /// <summary>传说（金）。极稀有，考验你撤不撤。</summary>
        Legendary,
    }

    /// <summary>
    /// 各稀有度档位对应的价值区间（单位：游戏币）。
    /// </summary>
    /// <remarks>
    /// <para>这组数字只有一个用途：在编辑器里校验 <see cref="ItemCategory.Loot"/> 类物品的定价是否离谱。
    /// 它不是运行时规则，玩家与玩法逻辑都不读它。</para>
    ///
    /// <para><b>只对 <see cref="ItemCategory.Loot"/> 生效。</b>弹药单发 8 块、绷带一个 400 块，
    /// 它们按"每次使用的消耗量"定价，与整件物品的价值档位不是一个量纲；
    /// 硬套区间只会逼着数值造假。稀有度对它们表达的是战场稀缺程度，不是价格。</para>
    /// </remarks>
    public static class RarityValueBands
    {
        /// <summary>普通档的价值下限。</summary>
        public const int CommonMin = 500;

        /// <summary>普通档的价值上限，同时也是精良档的下限。</summary>
        public const int CommonMax = 2_000;

        /// <summary>精良档的价值上限，同时也是稀有档的下限。</summary>
        public const int UncommonMax = 8_000;

        /// <summary>稀有档的价值上限，同时也是史诗档的下限。</summary>
        public const int RareMax = 25_000;

        /// <summary>史诗档的价值上限，传说档从其之上开始且没有上限。</summary>
        public const int EpicMax = 80_000;

        /// <summary>取指定档位的价值下限（含）。</summary>
        public static int GetMin(RarityTier tier)
        {
            switch (tier)
            {
                case RarityTier.Common:
                    return CommonMin;
                case RarityTier.Uncommon:
                    return CommonMax;
                case RarityTier.Rare:
                    return UncommonMax;
                case RarityTier.Epic:
                    return RareMax;
                default:
                    return EpicMax;
            }
        }

        /// <summary>取指定档位的价值上限（含）。传说档没有上限，返回 <see cref="int.MaxValue"/>。</summary>
        public static int GetMax(RarityTier tier)
        {
            switch (tier)
            {
                case RarityTier.Common:
                    return CommonMax;
                case RarityTier.Uncommon:
                    return UncommonMax;
                case RarityTier.Rare:
                    return RareMax;
                case RarityTier.Epic:
                    return EpicMax;
                default:
                    return int.MaxValue;
            }
        }
    }
}
