using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Raid;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 战利品掉落测试：可复现、容错、容器定义完整性。
    /// </summary>
    /// <remarks>
    /// <para>掉落是随机的，但随机必须可复现：同一个种子必须产出同一批物资。
    /// 否则「这一局为什么开出这些东西」永远无法回答，联机时两端也会不一致。</para>
    ///
    /// <para>这里用真实的 <see cref="ItemCatalog"/> 资产类型，但物品定义是运行时构造的，
    /// 因此测试不依赖仓库里任何具体资产文件的内容。</para>
    /// </remarks>
    [TestFixture]
    public sealed class LootTableTests
    {
        /// <summary>创建一个只包含指定物品 ID 的物品目录。</summary>
        private static ItemCatalog CreateCatalog(params string[] ids)
        {
            var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            var serialized = new SerializedObject(catalog);
            var list = serialized.FindProperty("m_Items");
            list.arraySize = ids.Length;

            for (var i = 0; i < ids.Length; i++)
            {
                var definition = ScriptableObject.CreateInstance<ItemDefinition>();
                var definitionSerialized = new SerializedObject(definition);
                definitionSerialized.FindProperty("m_Id").stringValue = ids[i];
                definitionSerialized.FindProperty("m_DisplayName").stringValue = ids[i];
                definitionSerialized.FindProperty("m_Width").intValue = 1;
                definitionSerialized.FindProperty("m_Height").intValue = 1;
                definitionSerialized.FindProperty("m_MaxStack").intValue = 30;
                definitionSerialized.FindProperty("m_BaseValue").intValue = 100;
                definitionSerialized.ApplyModifiedPropertiesWithoutUndo();

                list.GetArrayElementAtIndex(i).objectReferenceValue = definition;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.RebuildLookup();
            return catalog;
        }

        /// <summary>把网格里的物品汇总成「ID:数量」的可比字符串。</summary>
        private static string Snapshot(InventoryGrid grid)
        {
            var text = string.Empty;
            var items = grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                text += $"{items[i].Definition.Id}x{items[i].StackCount};";
            }

            return text;
        }

        [Test]
        public void 同一个种子产出完全相同的物资()
        {
            var catalog = CreateCatalog("loot.bolt", "ammo.9x19");
            var table = new LootTable("test", "测试掉落", 6, new[]
            {
                new LootTableEntry("loot.bolt", 3, 1, 4),
                new LootTableEntry("ammo.9x19", 2, 5, 10),
            });

            var first = new InventoryGrid(6, 4, "第一次");
            var second = new InventoryGrid(6, 4, "第二次");
            new LootRoller(catalog, new DeterministicRandom(2026u), new ItemFactory()).Roll(table, first);
            new LootRoller(catalog, new DeterministicRandom(2026u), new ItemFactory()).Roll(table, second);

            Assert.AreEqual(Snapshot(first), Snapshot(second), "同种子必须产出同一批物资");
            Assert.Greater(first.Items.Count, 0, "固定种子下不应抽出空箱子");
        }

        [Test]
        public void 表里引用了不存在的物品时安全跳过()
        {
            var catalog = CreateCatalog("loot.bolt");
            var table = new LootTable("test", "含错误条目", 4, new[]
            {
                new LootTableEntry("物品不存在", 1, 1, 1),
            });
            var grid = new InventoryGrid(4, 3, "测试箱");

            var placed = 0;
            Assert.DoesNotThrow(() =>
            {
                placed = new LootRoller(catalog, new DeterministicRandom(1u), new ItemFactory())
                    .Roll(table, grid);
            });
            Assert.AreEqual(0, placed, "未知 ID 不应产出任何物品");
            Assert.AreEqual(0, grid.Items.Count);
        }

        [Test]
        public void 容器定义齐全且能校验出缺失的物品()
        {
            var all = LootContainerCatalog.All;
            Assert.AreEqual(5, all.Count, "应有 5 种容器定义");

            var ids = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < all.Count; i++)
            {
                Assert.IsTrue(ids.Add(all[i].Id), $"容器 ID 重复：{all[i].Id}");
                Assert.IsNotNull(LootContainerCatalog.Get(all[i].Id));
                Assert.Greater(all[i].GridSize.Width, 0);
                Assert.Greater(all[i].GridSize.Height, 0);
            }

            Assert.IsNotNull(LootContainerCatalog.Get("safe.rare"), "保险柜必须存在");
            Assert.IsNull(LootContainerCatalog.Get("不存在"));

            // 目录里一件物品都没有 → 必须报出问题
            var emptyCatalog = CreateCatalog();
            Assert.IsNotNull(LootContainerCatalog.Validate(emptyCatalog));
        }
    }
}
