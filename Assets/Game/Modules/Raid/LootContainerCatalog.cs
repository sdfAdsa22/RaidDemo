using System.Collections.Generic;
using RaidDemo.Data;

namespace RaidDemo.Raid
{
    /// <summary>
    /// 全部战利品容器定义的索引。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么容器定义写在代码里而不是资产里：</b>灰盒阶段容器只有五种，
    /// 掉落权重又需要频繁调整，写成代码可以随提交一起走版本控制、随时 diff；
    /// 等 M6 接入商人经济之后，掉落表会迁移到可配置资产。</para>
    ///
    /// <para>所有物品 ID 都必须能在物品目录里找到，<see cref="Validate"/> 负责这件事。
    /// 拼错 ID 的症状是「某个箱子永远是空的」，从画面上根本看不出问题在哪。</para>
    /// </remarks>
    public static class LootContainerCatalog
    {
        /// <summary>全部容器定义，按固定顺序排列。</summary>
        private static readonly IReadOnlyList<LootContainerDefinition> s_All = BuildAll();

        /// <summary>全部容器定义。</summary>
        public static IReadOnlyList<LootContainerDefinition> All
        {
            get { return s_All; }
        }

        /// <summary>按 ID 查找容器定义。</summary>
        /// <param name="id">容器定义 ID。</param>
        /// <returns>找不到时返回 null。</returns>
        public static LootContainerDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var i = 0; i < s_All.Count; i++)
            {
                if (string.Equals(s_All[i].Id, id, System.StringComparison.Ordinal))
                {
                    return s_All[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 校验全部掉落表引用的物品 ID 是否都存在于目录中。
        /// </summary>
        /// <param name="catalog">物品目录，可为 null。</param>
        /// <returns>全部合法返回 null，否则返回中文问题描述。</returns>
        public static string Validate(ItemCatalog catalog)
        {
            if (catalog == null)
            {
                return "物品目录为空，无法校验掉落表。";
            }

            for (var i = 0; i < s_All.Count; i++)
            {
                var definition = s_All[i];
                var tableProblem = definition.Table != null ? definition.Table.Validate() : "缺少掉落表。";
                if (tableProblem != null)
                {
                    return $"{definition.Id}：{tableProblem}";
                }

                var entries = definition.Table.Entries;
                for (var e = 0; e < entries.Count; e++)
                {
                    if (!catalog.TryGet(entries[e].ItemId, out _))
                    {
                        return $"{definition.Id} 引用了目录中不存在的物品：{entries[e].ItemId}。";
                    }
                }
            }

            return null;
        }

        /// <summary>构造全部容器定义。</summary>
        private static IReadOnlyList<LootContainerDefinition> BuildAll()
        {
            return new[]
            {
                BuildCommonCrate(),
                BuildAmmoBox(),
                BuildMedicalBox(),
                BuildWeaponRack(),
                BuildSafe(),
                BuildDebugCrate(),
            };
        }

        /// <summary>创建一条掉落条目，减少表格里的重复书写。</summary>
        private static LootTableEntry Entry(string itemId, int weight, int minCount = 1, int maxCount = 1)
        {
            return new LootTableEntry(itemId, weight, minCount, maxCount);
        }

        /// <summary>
        /// 补给箱：数量最多、价值最低的容器。
        /// </summary>
        /// <remarks>
        /// 抽 4 次。绝大多数产出是铜螺栓、手枪弹与绷带，
        /// 只有小概率给出武器、头盔或燃料罐——它负责提供「安全的收益」，
        /// 让玩家不至于每个箱子都在赌命。
        /// </remarks>
        private static LootContainerDefinition BuildCommonCrate()
        {
            return new LootContainerDefinition(
                "crate.common",
                "补给箱",
                ContainerFlavor.Crate,
                5,
                4,
                new LootTable("loot.common", "补给箱掉落", 4, new[]
                {
                    Entry("loot.bolt.copper", 30, 1, 6),
                    Entry("ammo.9x19.standard", 25, 10, 30),
                    Entry("medical.bandage.small", 20, 1, 2),
                    Entry("ammo.5.45.standard", 12, 8, 20),
                    Entry("weapon.pistol.pm", 6),
                    Entry("backpack.small", 4),
                    Entry("loot.canister.fuel", 2),
                    Entry("armor.helmet.steel", 1),
                }));
        }

        /// <summary>
        /// 弹药箱：抽 3 次，弹药占比超过七成。
        /// </summary>
        /// <remarks>
        /// 玩家的弹药压力主要靠它缓解，因此单次给量明显高于补给箱。
        /// 它也是「打完一轮必须冒险去补给」这条节奏循环的支点。
        /// </remarks>
        private static LootContainerDefinition BuildAmmoBox()
        {
            return new LootContainerDefinition(
                "crate.ammo",
                "弹药箱",
                ContainerFlavor.AmmoBox,
                4,
                3,
                new LootTable("loot.ammo", "弹药箱掉落", 3, new[]
                {
                    Entry("ammo.9x19.standard", 40, 30, 60),
                    Entry("ammo.5.45.standard", 35, 20, 45),
                    Entry("medical.bandage.small", 10, 1, 2),
                    Entry("loot.bolt.copper", 9, 2, 5),
                    Entry("armor.helmet.steel", 6),
                }));
        }

        /// <summary>
        /// 医疗箱：抽 3 次，以绷带与医疗包为主。
        /// </summary>
        /// <remarks>
        /// 野战医疗包是稀有物品，把它放进医疗箱而不是保险柜，
        /// 是为了让「想活着离开」这条路线也有明确的搜索目标。
        /// </remarks>
        private static LootContainerDefinition BuildMedicalBox()
        {
            return new LootContainerDefinition(
                "crate.medical",
                "医疗箱",
                ContainerFlavor.MedicalBox,
                4,
                3,
                new LootTable("loot.medical", "医疗箱掉落", 3, new[]
                {
                    Entry("medical.bandage.small", 55, 1, 3),
                    Entry("medical.kit.field", 15),
                    Entry("ammo.9x19.standard", 15, 8, 16),
                    Entry("loot.bolt.copper", 15, 1, 3),
                }));
        }

        /// <summary>
        /// 武器架：抽 3 次，产出一把武器或一件护甲的机会明显更高。
        /// </summary>
        /// <remarks>
        /// 它是「战斗力升级」的主要来源：拿到 AK-74 与防弹背心之后，
        /// 玩家与 AI 的交火距离与容错都会明显改善。
        /// 但武器架只放在厂房深处与堆场角落，去拿它必须承担风险。
        /// </remarks>
        private static LootContainerDefinition BuildWeaponRack()
        {
            return new LootContainerDefinition(
                "crate.weapon",
                "武器架",
                ContainerFlavor.WeaponRack,
                6,
                3,
                new LootTable("loot.weapon", "武器架掉落", 3, new[]
                {
                    Entry("ammo.5.45.standard", 30, 15, 35),
                    Entry("weapon.pistol.pm", 22),
                    Entry("ammo.9x19.standard", 20, 15, 30),
                    Entry("weapon.rifle.ak74", 14),
                    Entry("armor.helmet.steel", 8),
                    Entry("armor.vest.plate", 6),
                }));
        }

        /// <summary>
        /// 测试箱：**开发期专用，交付前必须连同场景里的标记一起删除**。
        /// </summary>
        /// <remarks>
        /// <para>固定产出武器、护甲、头盔、背包各一件，用于快速验证
        /// 「装备是否生效」这条链路（护甲减伤、头盔挡上部位、背包改变容量）。
        /// 用固定产出而不是随机掉落，是因为验证需要的是**每次都有**，
        /// 而不是「多开几次总会出现」。</para>
        ///
        /// <para>ID 里带 debug 前缀，便于全局搜索清理；
        /// 场景里的标记名同样是 Loot_xx_crate.debug，一眼能认出来。</para>
        /// </remarks>
        private static LootContainerDefinition BuildDebugCrate()
        {
            return new LootContainerDefinition(
                "crate.debug",
                "测试箱",
                ContainerFlavor.Crate,
                6,
                4,
                new LootTable("loot.debug", "测试箱掉落", 1, new[]
                {
                    Entry("ammo.5.45.standard", 1, 30, 30),
                }),
                new[]
                {
                    Entry("weapon.rifle.ak74", 1),
                    Entry("armor.vest.plate", 1),
                    Entry("armor.helmet.steel", 1),
                    Entry("backpack.small", 1),
                });
        }

        /// <summary>
        /// 保险柜：全图唯一的「赌一把」容器。
        /// </summary>
        /// <remarks>
        /// <para>抽 3 次。权重分布是：普通 25、精良（步枪）15、
        /// 稀有（燃料罐 / 医疗包 / 防弹背心 / 突击背包）48、史诗（金表）12。
        /// 单次抽不到稀有以上的概率是 40%，三次都抽不到的概率是 0.4³ ≈ 6.4%，
        /// 也就是约 94% 的保险柜至少能开出一件稀有物品。它不保证出史诗，
        /// 但几乎不会让人空手而归。</para>
        ///
        /// <para>金表（史诗）单独占 12 的权重，是地图上最贵的一件东西。
        /// 一局只摆一个保险柜，因此它天然成为「这一局要不要贪」的焦点。</para>
        /// </remarks>
        private static LootContainerDefinition BuildSafe()
        {
            return new LootContainerDefinition(
                "safe.rare",
                "保险柜",
                ContainerFlavor.Safe,
                4,
                4,
                new LootTable("loot.safe", "保险柜掉落", 3, new[]
                {
                    Entry("loot.bolt.copper", 15, 3, 8),
                    Entry("ammo.9x19.standard", 10, 15, 30),
                    Entry("loot.canister.fuel", 20),
                    Entry("medical.kit.field", 12),
                    Entry("loot.watch.gold", 12),
                    Entry("weapon.rifle.ak74", 15),
                    Entry("armor.vest.plate", 10),
                    Entry("backpack.raider", 6),
                }));
        }
    }
}
