using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;
using UnityEditor;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 装备槽镜像编解码的契约测试。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>装备槽不是网格容器，原本没有下行通道——
    /// 服务器配发或换上的武器，客户端一概看不见，表现是"进图后 HUD 无武器、按开火没反应"，
    /// 而服务器日志一切正常（2026-09-14 联机基础问题修复的定位结论）。</para>
    ///
    /// <para>修复方式是把装备槽打包成"1×N 伪容器"（行号 = 槽位序号）借容器批次下发。
    /// 这里的断言钉住两端共用的映射约定：任何一端改了行号语义而没有同步另一端，
    /// 都会在这里直接失败，而不是变成"装备了却看不见"这种只能实机排查的现象。</para>
    /// </remarks>
    [TestFixture]
    public sealed class EquipmentMirrorCodecTests
    {
        private const string CatalogPath = "Assets/Game/Content/Items/ItemCatalog.asset";
        private const string RifleId = "weapon.rifle.ak74";
        private const string HelmetId = "armor.helmet.steel";
        private const string UnknownId = "weapon.rifle.does_not_exist";

        private ItemCatalog m_Catalog;

        [SetUp]
        public void SetUp()
        {
            m_Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.IsNotNull(m_Catalog, $"找不到物品目录：{CatalogPath}");
        }

        /// <summary>按 Id 造一件物品实例；目录里没有就断言失败。</summary>
        private ItemInstance Create(string id)
        {
            Assert.IsTrue(m_Catalog.TryGet(id, out var definition), $"目录里找不到 {id}");
            return new ItemFactory().Create(definition);
        }

        /// <summary>手工拼一条镜像条目，用于构造"服务器不可能发出、但协议上合法"的坏数据。</summary>
        private static ContainerContentsMessage MirrorWith(params ContainerItemMessage[] items)
        {
            return new ContainerContentsMessage
            {
                ContainerId = ContainerIds.EquipmentMirror,
                Width = EquipmentMirrorCodec.MirrorWidth,
                Height = EquipmentLoadout.SlotCount,
                Items = items,
            };
        }

        [Test]
        public void Build_MapsEverySlotToItsOwnRow()
        {
            var equipment = new EquipmentLoadout();
            equipment.Equip(Create(RifleId), EquipmentSlot.PrimaryWeapon);
            equipment.Equip(Create(HelmetId), EquipmentSlot.Head);

            var mirror = EquipmentMirrorCodec.Build(equipment);

            Assert.AreEqual(ContainerIds.EquipmentMirror, mirror.ContainerId, "镜像必须使用约定的容器编号。");
            Assert.AreEqual(EquipmentMirrorCodec.MirrorWidth, mirror.Width);
            Assert.AreEqual(EquipmentLoadout.SlotCount, mirror.Height, "镜像高度必须等于槽位总数。");
            Assert.AreEqual(2, mirror.Items.Length, "只打包非空槽位。");

            // 不保证顺序（按槽位遍历天然有序，但契约只承诺"行号 = 槽位"）。
            var rifle = Find(mirror, RifleId);
            var helmet = Find(mirror, HelmetId);
            Assert.AreEqual((int)EquipmentSlot.PrimaryWeapon, rifle.CellY, "主武器的行号必须是主武器槽位序号。");
            Assert.AreEqual((int)EquipmentSlot.Head, helmet.CellY, "头盔的行号必须是头盔槽位序号。");
            Assert.AreEqual(0, rifle.CellX, "镜像固定单列，横坐标必须是 0。");
        }

        [Test]
        public void Build_ThrowsOnNullEquipment()
        {
            Assert.Throws<System.ArgumentNullException>(() => EquipmentMirrorCodec.Build(null));
        }

        [Test]
        public void Apply_RebuildsEquipmentFromMirror()
        {
            var equipment = new EquipmentLoadout();
            var mirror = MirrorWith(
                new ContainerItemMessage
                {
                    ItemId = RifleId,
                    Count = 1,
                    CellX = 0,
                    CellY = (int)EquipmentSlot.PrimaryWeapon,
                },
                new ContainerItemMessage
                {
                    ItemId = HelmetId,
                    Count = 1,
                    CellX = 0,
                    CellY = (int)EquipmentSlot.Head,
                });

            var applied = EquipmentMirrorCodec.Apply(equipment, mirror, m_Catalog);

            Assert.AreEqual(2, applied);
            Assert.IsNotNull(equipment.Get(EquipmentSlot.PrimaryWeapon), "主武器槽必须被镜像填上。");
            Assert.AreEqual(RifleId, equipment.Get(EquipmentSlot.PrimaryWeapon).Definition.Id);
            Assert.IsNotNull(equipment.Get(EquipmentSlot.Head), "头盔槽必须被镜像填上。");
        }

        [Test]
        public void Apply_ClearsEquipmentThatIsMissingFromMirror()
        {
            // 本地先有一把枪；服务器下发的镜像里没有它（例如阵亡清空）——必须被清掉。
            var equipment = new EquipmentLoadout();
            equipment.Equip(Create(RifleId), EquipmentSlot.PrimaryWeapon);

            var applied = EquipmentMirrorCodec.Apply(equipment, MirrorWith(), m_Catalog);

            Assert.AreEqual(0, applied);
            Assert.IsNull(equipment.Get(EquipmentSlot.PrimaryWeapon), "镜像没有的装备必须从本地消失。");
        }

        [Test]
        public void Apply_SkipsUnknownDefinitionAndOutOfRangeRows()
        {
            var equipment = new EquipmentLoadout();
            var mirror = MirrorWith(
                new ContainerItemMessage { ItemId = UnknownId, Count = 1, CellY = (int)EquipmentSlot.PrimaryWeapon },
                new ContainerItemMessage { ItemId = RifleId, Count = 1, CellY = 99 });

            var applied = EquipmentMirrorCodec.Apply(equipment, mirror, m_Catalog);

            Assert.AreEqual(0, applied, "查不到的定义与越界的行号都不该被应用。");
            Assert.IsNull(equipment.Get(EquipmentSlot.PrimaryWeapon));
        }

        [Test]
        public void Apply_ReturnsZeroOnNullOrMissingCatalog()
        {
            Assert.AreEqual(0, EquipmentMirrorCodec.Apply(null, MirrorWith(), m_Catalog));
            Assert.AreEqual(0, EquipmentMirrorCodec.Apply(new EquipmentLoadout(), MirrorWith(), null));
        }

        private static ContainerItemMessage Find(in ContainerContentsMessage mirror, string itemId)
        {
            for (var i = 0; i < mirror.Items.Length; i++)
            {
                if (mirror.Items[i].ItemId == itemId)
                {
                    return mirror.Items[i];
                }
            }

            Assert.Fail($"镜像里找不到 {itemId}");
            return default;
        }
    }
}
