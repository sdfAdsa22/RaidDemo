using System.Collections;
using System.Linq;
using NUnit.Framework;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using RaidDemo.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace RaidDemo.Tests.PlayMode
{
    /// <summary>
    /// 背包界面的指针交互补测：拖拽、双击快速转移、标题栏「整理仓库」按钮。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须放 PlayMode：</b>这三条都是"指针事件 → 界面状态 → 命令"的链路，
    /// 编辑模式里 MonoBehaviour 的 Update 不会执行，界面的拖拽状态机也就不会跑起来。
    /// M11 实机验收时这三条被判为 BLOCKED（用系统级鼠标注入无法稳定复现拖拽），
    /// 这里改用输入系统的测试夹具直接产生设备事件，把那条缺口补上。</para>
    ///
    /// <para><b>界面输入为什么能被测试驱动：</b>界面读的是
    /// <c>Mouse.current</c>（新输入系统），而不是 Unity 的 EventSystem，
    /// 因此 <see cref="InputTestFixture"/> 造出来的虚拟鼠标就是它真正读取的那只鼠标——
    /// 测试驱动的是真链路，不是替身。</para>
    /// </remarks>
    [TestFixture]
    public sealed class InventoryUiInteractionPlayModeTests : InputTestFixture
    {
        /// <summary>虚拟鼠标。所有指针操作都通过它产生设备事件。</summary>
        private Mouse m_Mouse;

        /// <summary>虚拟键盘。界面每帧都会读它（Tab / F / R / Esc），没有设备会直接返回。</summary>
        private Keyboard m_Keyboard;

        /// <summary>测试搭建出来的界面宿主，用例结束时销毁。</summary>
        private GameObject m_Host;

        private ItemFactory m_Factory;
        private EventBus m_Bus;
        private ContainerRegistry m_Registry;
        private CommandRouter m_Router;
        private InventoryGrid m_Backpack;
        private InventoryGrid m_Stash;
        private InventoryGrid m_Pouch;
        private int m_BackpackId;
        private int m_StashId;
        private int m_PouchId;
        private InventoryScreenController m_Screen;

        /// <summary>弹药堆：用于拖拽，避免与"双击优先进弹药挂"的规则混在一起。</summary>
        private IItemDefinition m_Ammo;

        /// <summary>普通战利品：双击时在背包与容器之间搬运。</summary>
        private IItemDefinition m_Loot;

        /// <summary>三格长的步枪：整理时应当最先落位到左上角。</summary>
        private IItemDefinition m_Rifle;

        /// <inheritdoc />
        public override void Setup()
        {
            base.Setup();
            m_Mouse = InputSystem.AddDevice<Mouse>();
            m_Keyboard = InputSystem.AddDevice<Keyboard>();

            m_Factory = new ItemFactory();
            m_Bus = new EventBus();
            m_Registry = new ContainerRegistry();
            m_Router = new CommandRouter();

            m_Backpack = new InventoryGrid(5, 5, "主背包");
            m_Stash = new InventoryGrid(10, 8, "仓库");
            m_Pouch = new InventoryGrid(1, 5, "弹药挂");
            m_BackpackId = m_Registry.Register(m_Backpack, ContainerKind.PlayerBackpack);
            m_StashId = m_Registry.Register(m_Stash, ContainerKind.Stash);
            m_PouchId = m_Registry.Register(m_Pouch, ContainerKind.AmmoPouch);

            var loadout = new PlayerLoadout(m_Backpack, new EquipmentLoadout(), m_Pouch);
            var context = new InventoryContext(m_Registry, loadout, m_Bus);
            m_Router.Register<InventoryMoveIntent>(new InventoryMoveCommandHandler(context));
            m_Router.Register<InventoryQuickTransferIntent>(new InventoryQuickTransferCommandHandler(context));
            m_Router.Register<InventoryRotateIntent>(new InventoryRotateCommandHandler(context));
            m_Router.Register<InventorySplitIntent>(new InventorySplitCommandHandler(context));
            m_Router.Register<InventorySortIntent>(new InventorySortCommandHandler(context));
            m_Router.Register<InventoryEquipIntent>(new InventoryEquipCommandHandler(context));
            m_Router.Register<InventoryUnequipIntent>(new InventoryUnequipCommandHandler(context));

            m_Ammo = new FakeItem("ammo.9x19", ItemCategory.Ammo, maxStack: 120, weightKg: 0.012f);
            m_Loot = new FakeItem("loot.watch", ItemCategory.Loot, baseValue: 900);
            m_Rifle = new FakeItem("weapon.rifle", ItemCategory.Weapon, width: 3, height: 1, weightKg: 3.4f);

            m_Host = new GameObject("InventoryUiTestHost");
            m_Screen = m_Host.AddComponent<InventoryScreenController>();
            m_Screen.Initialize(
                m_Router,
                m_Registry,
                loadout,
                m_Bus,
                m_BackpackId,
                m_PouchId,
                new EncumbranceProfile(),
                _ => { });
            m_Screen.SetStashContainer(m_StashId);
        }

        /// <inheritdoc />
        public override void TearDown()
        {
            if (m_Host != null)
            {
                Object.Destroy(m_Host);
            }

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator 拖拽把仓库里的物品搬进背包()
        {
            var watch = m_Factory.Create(m_Loot);
            m_Stash.Place(watch, new GridPoint(2, 3), false);

            m_Screen.OpenStash(m_StashId);
            yield return null;
            yield return null;

            var stashView = FindView(m_StashId);
            var backpackView = FindView(m_BackpackId);
            Assert.IsNotNull(stashView, "仓库网格视图应当已经建好。");
            Assert.IsNotNull(backpackView, "背包网格视图应当已经建好。");

            yield return DragCell(stashView, new GridPoint(2, 3), backpackView, new GridPoint(1, 1));

            Assert.IsNull(m_Stash.GetAt(new GridPoint(2, 3)), "拖拽结束后仓库源格子应当空出来。");
            Assert.AreSame(watch, m_Backpack.GetAt(new GridPoint(1, 1)), "物品应当落在背包的松手格上。");
        }

        [UnityTest]
        public IEnumerator 双击把仓库里的物品快速转移进背包()
        {
            var watch = m_Factory.Create(m_Loot);
            m_Stash.Place(watch, new GridPoint(4, 4), false);

            m_Screen.OpenStash(m_StashId);
            yield return null;
            yield return null;

            var stashView = FindView(m_StashId);
            Assert.IsNotNull(stashView, "仓库网格视图应当已经建好。");
            var point = ScreenPointOf(stashView, new GridPoint(4, 4));

            // 双击窗口是 0.35 秒，这里两下点击相隔一帧，必定落在窗口内。
            yield return ClickAt(point);
            yield return ClickAt(point);

            Assert.IsNull(m_Stash.GetAt(new GridPoint(4, 4)), "双击后物品应当离开仓库。");
            Assert.IsTrue(
                m_Backpack.Items.Contains(watch),
                "非弹药的普通物品双击后应当被搬进背包。");
        }

        [UnityTest]
        public IEnumerator 点整理仓库按钮后物品按面积重排到左上角()
        {
            var rifle = m_Factory.Create(m_Rifle);
            m_Stash.Place(rifle, new GridPoint(2, 5), false);
            m_Stash.Place(m_Factory.Create(m_Loot), new GridPoint(0, 3), false);
            m_Stash.Place(m_Factory.Create(m_Ammo, 30), new GridPoint(6, 6), false);

            m_Screen.OpenStash(m_StashId);
            yield return null;
            yield return null;

            var buttonCenter = FindButtonCenter("整理仓库");
            Assert.IsTrue(buttonCenter.HasValue, "仓库界面的标题栏应当有「整理仓库」按钮。");

            yield return ClickAt(buttonCenter.Value);

            Assert.IsTrue(m_Stash.TryGetOrigin(rifle, out var origin), "整理后步枪应当仍在仓库里。");
            Assert.AreEqual(
                new GridPoint(0, 0),
                origin,
                "按面积降序整理时，占三格的步枪应当先落位到左上角。");
        }

        [UnityTest]
        public IEnumerator 战利品箱界面不显示整理仓库按钮()
        {
            var lootGrid = new InventoryGrid(4, 3, "补给箱");
            var lootId = m_Registry.Register(lootGrid, ContainerKind.Loot);
            lootGrid.Place(m_Factory.Create(m_Loot), new GridPoint(0, 0), false);

            m_Screen.OpenLootContainer(lootId, "补给箱");
            yield return null;
            yield return null;

            Assert.IsFalse(
                FindButtonCenter("整理仓库").HasValue,
                "右栏是战利品时不应该出现「整理仓库」——那是仓库上的动作。");
        }

        /// <summary>在当前界面上找到指定容器的网格视图。</summary>
        private InventoryGridView FindView(int containerId)
        {
            var views = m_Screen.GetComponentsInChildren<InventoryGridView>(includeInactive: true);
            for (var i = 0; i < views.Length; i++)
            {
                if (views[i].ContainerId == containerId)
                {
                    return views[i];
                }
            }

            return null;
        }

        /// <summary>把格子坐标换算成屏幕坐标（取格子中心）。</summary>
        private static Vector2 ScreenPointOf(InventoryGridView view, GridPoint cell)
        {
            // 格子区域的轴心在左上角：x 向右为正，y 向下为负。
            var cells = (RectTransform)view.transform.Find("Cells");
            var local = new Vector2(
                (cell.X + 0.5f) * InventoryGridView.CellSize,
                -(cell.Y + 0.5f) * InventoryGridView.CellSize);
            return RectTransformUtility.WorldToScreenPoint(null, cells.TransformPoint(local));
        }

        /// <summary>按标题文字找按钮中心。找不到返回 null。</summary>
        private Vector2? FindButtonCenter(string caption)
        {
            var labels = m_Screen.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: false);
            for (var i = 0; i < labels.Length; i++)
            {
                if (labels[i].text != caption)
                {
                    continue;
                }

                // 按钮文字是拉伸填满按钮的，因此文字矩形的中心就是按钮中心。
                var rect = labels[i].rectTransform;
                return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            }

            return null;
        }

        /// <summary>把鼠标移到指定位置后按下再松开。</summary>
        private IEnumerator ClickAt(Vector2 screenPoint)
        {
            Move(m_Mouse.position, screenPoint);
            yield return null;
            yield return null;
            Press(m_Mouse.leftButton);
            yield return null;
            yield return null;
            Release(m_Mouse.leftButton);
            yield return null;
            yield return null;
        }

        /// <summary>从源格子拖到目标格子。</summary>
        private IEnumerator DragCell(
            InventoryGridView source, GridPoint from,
            InventoryGridView target, GridPoint to)
        {
            Move(m_Mouse.position, ScreenPointOf(source, from));
            yield return null;
            yield return null;

            Press(m_Mouse.leftButton);
            yield return null;
            yield return null;

            Move(m_Mouse.position, ScreenPointOf(target, to));
            yield return null;
            yield return null;

            Release(m_Mouse.leftButton);
            yield return null;
            yield return null;
        }

        /// <summary>
        /// 测试用物品定义：只实现规则层依赖的字段，避免用例去加载 ScriptableObject 资产。
        /// </summary>
        private sealed class FakeItem : IItemDefinition
        {
            public FakeItem(
                string id,
                ItemCategory category,
                int width = 1,
                int height = 1,
                float weightKg = 1f,
                int baseValue = 100,
                int maxStack = 1)
            {
                Id = id;
                DisplayName = id;
                Category = category;
                GridSize = new GridSize(width, height);
                WeightKg = weightKg;
                BaseValue = baseValue;
                MaxStack = maxStack;
                ContainerGridSize = new GridSize(4, 4);
                IsContainer = category == ItemCategory.Backpack;
            }

            public string Id { get; }

            public string DisplayName { get; }

            public ItemCategory Category { get; }

            public RarityTier Rarity => RarityTier.Common;

            public GridSize GridSize { get; }

            public float WeightKg { get; }

            public int BaseValue { get; }

            public int MaxStack { get; }

            public bool CanRotate => true;

            public bool IsContainer { get; }

            public GridSize ContainerGridSize { get; }

            public IWeaponStats WeaponStats => null;

            public IAmmoStats AmmoStats => null;

            public IArmorStats ArmorStats => null;
        }
    }
}
