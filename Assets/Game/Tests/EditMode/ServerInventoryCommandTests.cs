using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEditor;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器侧背包命令的测试（M9 · P3）。
    /// </summary>
    /// <remarks>
    /// <para>联机里"捡东西"与"装备"都由服务器执行，用的是**与单机完全相同的那套 handler**。
    /// 这里直接把那套 handler 装在服务器的注册表与随身装备上，验证三件事：</para>
    /// <list type="number">
    /// <item><description>从场景容器把物品移进背包（拾取）；</description></item>
    /// <item><description>把背包里的武器装到主武器槽（装备）；</description></item>
    /// <item><description>同一个格子被拿两次时，第二次必须失败（先到先得）。</description></item>
    /// </list>
    /// <para>用真实目录资产而不是造假的定义：物品的尺寸、可堆叠性、装备槽都来自真实数据。</para>
    /// </remarks>
    [TestFixture]
    public sealed class ServerInventoryCommandTests
    {
        private const string CatalogPath = "Assets/Game/Content/Items/ItemCatalog.asset";
        private const string RifleId = "weapon.rifle.ak74";
        private const int PlayerId = 1;

        private ItemCatalog m_Catalog;
        private ItemFactory m_Factory;
        private ContainerRegistry m_Registry;
        private PlayerLoadout m_Loadout;
        private InventoryContext m_Context;
        private CommandRouter m_Router;

        /// <summary>准备一份"服务器侧"的容器 + 随身装备，并注册与服务器相同的处理器。</summary>
        [SetUp]
        public void SetUp()
        {
            m_Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.IsNotNull(m_Catalog, $"找不到物品目录：{CatalogPath}");

            m_Factory = new ItemFactory();
            m_Registry = new ContainerRegistry();

            // 随身装备要显式构造：背包与弹药挂的尺寸就是游戏里"无包时的口袋"尺寸。
            m_Loadout = new PlayerLoadout(
                new InventoryGrid(Meta.MetaProgress.PocketWidth, Meta.MetaProgress.PocketHeight, "测试背包"),
                new EquipmentLoadout(),
                new InventoryGrid(5, 1, "测试弹药挂"));

            m_Registry.Register(m_Loadout.Backpack, ContainerKind.PlayerBackpack, ContainerIds.PlayerBackpack);
            m_Registry.Register(m_Loadout.AmmoPouch, ContainerKind.AmmoPouch, ContainerIds.AmmoPouch);

            // 事件总线不能为 null：handler 执行成功后会广播"容器变了"，
            // 空总线会在执行路径上直接抛异常（第一次跑就踩到了）。
            m_Context = new InventoryContext(m_Registry, m_Loadout, new EventBus());
            m_Router = new CommandRouter();
            m_Router.Register<InventoryMoveIntent>(new InventoryMoveCommandHandler(m_Context));
            m_Router.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(m_Context));
        }

        /// <summary>把一件武器放进指定的场景容器，返回它的格子坐标。</summary>
        private GridPoint PlaceWeaponInContainer(int containerId, int width = 4, int height = 4)
        {
            var definition = m_Catalog.Get(RifleId);
            Assert.IsNotNull(definition, $"目录里找不到 {RifleId}");

            var grid = new InventoryGrid(width, height, "测试箱");
            Assert.AreEqual(containerId, m_Registry.Register(grid, ContainerKind.Loot, containerId));

            var item = m_Factory.Create(definition, 1);
            Assert.IsTrue(grid.AutoPlace(item).Success, "武器应当能放进测试箱。");
            Assert.IsTrue(grid.TryGetOrigin(item, out var origin));
            return origin;
        }

        /// <summary>拾取：场景容器 → 背包。</summary>
        [Test]
        public void 拾取把物品从场景容器移进背包()
        {
            var origin = PlaceWeaponInContainer(ContainerIds.SceneContainer(0));

            var result = m_Router.Dispatch(new InventoryMoveIntent(
                PlayerId,
                ContainerIds.SceneContainer(0),
                ContainerIds.PlayerBackpack,
                origin.X,
                origin.Y,
                0,
                0));

            Assert.IsTrue(result.Success, $"拾取应当成功：{result.Code} - {result.Message}");

            m_Registry.TryGetGrid(ContainerIds.SceneContainer(0), out var loot);
            Assert.AreEqual(0, loot.Items.Count, "箱子里的东西应当被拿走了。");
            Assert.Greater(m_Loadout.Backpack.Items.Count, 0, "背包里应当有东西了。");
        }

        /// <summary>先到先得：同一个格子拿第二次必须失败。</summary>
        [Test]
        public void 同一格子拿第二次会失败()
        {
            var origin = PlaceWeaponInContainer(ContainerIds.SceneContainer(1));

            var first = m_Router.Dispatch(new InventoryMoveIntent(
                PlayerId, ContainerIds.SceneContainer(1), ContainerIds.PlayerBackpack,
                origin.X, origin.Y, 0, 0));
            Assert.IsTrue(first.Success);

            var second = m_Router.Dispatch(new InventoryMoveIntent(
                PlayerId, ContainerIds.SceneContainer(1), ContainerIds.PlayerBackpack,
                origin.X, origin.Y, 0, 0));
            Assert.IsFalse(second.Success, "已经空了的格子不能再拿走一次。");
        }

        /// <summary>装备：背包里的武器装进主武器槽。</summary>
        [Test]
        public void 装备把背包里的武器放进主武器槽()
        {
            var origin = PlaceWeaponInContainer(ContainerIds.SceneContainer(2));

            Assert.IsTrue(m_Router.Dispatch(new InventoryMoveIntent(
                PlayerId, ContainerIds.SceneContainer(2), ContainerIds.PlayerBackpack,
                origin.X, origin.Y, 0, 0)).Success);

            m_Registry.TryGetGrid(ContainerIds.PlayerBackpack, out var backpack);
            Assert.IsTrue(backpack.TryGetOrigin(backpack.Items[0], out var cell));

            var equip = m_Router.Dispatch(new InventoryEquipIntent(
                PlayerId,
                ContainerIds.PlayerBackpack,
                cell.X,
                cell.Y,
                EquipmentSlot.PrimaryWeapon));

            Assert.IsTrue(equip.Success, $"装备应当成功：{equip.Code} - {equip.Message}");

            var equipped = m_Loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon);
            Assert.IsNotNull(equipped, "主武器槽里应当有武器了。");
            Assert.AreEqual(RifleId, equipped.Definition.Id, "装进去的应当是那把 AK。");
        }
    }
}
