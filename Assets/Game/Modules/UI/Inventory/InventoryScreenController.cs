using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面：构建布局、驱动拖拽、把操作翻译成命令。
    /// </summary>
    /// <remarks>
    /// <para><b>本类不修改任何游戏数据。</b>它做的全部事情是：读容器数据画界面，
    /// 把鼠标动作翻译成 InventoryMoveIntent 之类的命令，发给 CommandRouter。</para>
    /// <para>数据真正改变之后，InventoryChangedEvent 会回来触发重画。这条环路保证了
    /// 界面显示的一定是权威数据，而不是界面自己以为的状态。</para>
    /// <para>界面在运行时用代码构建，不依赖预制体。灰盒阶段这样最省事，
    /// 等 M7 做美术时再把布局换成预制体，届时本类的逻辑部分不需要改动。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController : MonoBehaviour
    {
        /// <summary>界面参考分辨率。所有布局数值都按这个尺寸写。</summary>
        private const float ReferenceWidth = 1920f;

        private const float ReferenceHeight = 1080f;

        /// <summary>面板尺寸与位置（像素）。</summary>
        private const float PanelWidth = 900f;

        private const float PanelHeight = 820f;

        private const float PanelTop = 120f;

        /// <summary>装备槽尺寸与间距（像素）。</summary>
        private const float SlotWidth = 200f;

        private const float SlotHeight = 44f;

        private const float SlotGap = 56f;

        /// <summary>负重条尺寸（像素）。</summary>
        private const float BarWidth = 200f;

        private const float BarHeight = 18f;

        private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.10f, 0.96f);
        private static readonly Color SlotColor = new Color(0.22f, 0.22f, 0.25f, 1f);
        private static readonly Color SlotHighlightColor = new Color(0.30f, 0.85f, 0.40f, 0.55f);
        private static readonly Color BarBackColor = new Color(0.18f, 0.18f, 0.20f, 1f);
        private static readonly Color LightColor = new Color(0.35f, 0.80f, 0.45f);
        private static readonly Color HeavyColor = new Color(0.95f, 0.75f, 0.25f);
        private static readonly Color OverloadedColor = new Color(0.95f, 0.30f, 0.30f);

        /// <summary>装备槽的界面元素。</summary>
        private sealed class SlotWidget
        {
            public EquipmentSlot Slot;
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public ItemInstance Item;
        }

        private readonly List<SlotWidget> m_Slots = new List<SlotWidget>();

        private CommandRouter m_Router;
        private ContainerRegistry m_Registry;
        private PlayerLoadout m_Loadout;
        private EventBus m_EventBus;
        private int m_BackpackContainerId;

        /// <summary>当前展示在面板里的战利品容器 ID。0 表示没有打开任何容器。</summary>
        private int m_LootContainerId;

        private int m_AmmoPouchContainerId;
        private EncumbranceProfile m_EncumbranceProfile;
        private Action<bool> m_SetCursorLock;
        private IDisposable m_Subscription;

        private GameObject m_Root;

        /// <summary>界面画布。右键菜单作为它的子节点，才能盖在面板之上。</summary>
        private GameObject m_CanvasHost;

        /// <summary>物品目录。右键菜单靠它判断「这件东西能不能用」。</summary>
        private ItemCatalog m_ItemCatalog;

        /// <summary>「使用物品」的回调（容器 ID + 格子坐标）。由装配层注入。</summary>
        private Action<int, int, int> m_RequestUseItem;
        private InventoryGridView m_BackpackView;
        private InventoryGridView m_AmmoPouchView;
        private InventoryGridView m_LootView;

        /// <summary>战利品面板的左上角锚点（像素）。由背包与弹药挂的实际高度算出来。</summary>
        private Vector2 m_LootAnchorTopLeft;

        private Image m_BarFill;
        private Text m_BarLabel;

        private bool m_IsOpen;
        private bool m_IsDragging;
        private InventoryGridView m_DragSource;
        private ItemInstance m_DragItem;
        private GridPoint m_DragGrabOffset;
        private bool m_DragRotated;
        private SlotWidget m_HoveredSlot;

        /// <summary>快速操作的决策上下文，复用同一个实例避免每次双击产生垃圾。</summary>
        private readonly QuickActionContext m_QuickActionContext = new QuickActionContext();

        /// <summary>界面是否处于打开状态。装配层据此暂停角色移动。</summary>
        public bool IsOpen
        {
            get { return m_IsOpen; }
        }

        /// <summary>
        /// 是否接受输入。
        /// </summary>
        /// <remarks>
        /// 主菜单状态下必须关掉：本组件自己是轮询 Tab 键的，
        /// 若不加这道开关，玩家在主菜单按下 Tab 就会在主菜单底下弹出一个背包面板，
        /// 而光标状态也会被它抢走。
        /// </remarks>
        public bool InputEnabled { get; set; } = true;

        /// <summary>关闭界面。已经关闭时不做任何事。由装配层在战局结束时调用。</summary>
        public void Close()
        {
            if (m_IsOpen)
            {
                SetVisible(false);
            }
        }

        /// <summary>
        /// 初始化界面。
        /// </summary>
        /// <param name="router">命令路由。</param>
        /// <param name="registry">容器注册表。</param>
        /// <param name="loadout">角色携带物。</param>
        /// <param name="eventBus">事件总线。</param>
        /// <param name="backpackContainerId">主背包的容器 ID。</param>
        /// <param name="ammoPouchContainerId">弹药挂的容器 ID。</param>
        /// <param name="encumbranceProfile">负重配置，用于显示承载上限。</param>
        /// <param name="setCursorLock">光标锁定开关。界面需要解锁光标才能用鼠标拖拽。</param>
        public void Initialize(
            CommandRouter router,
            ContainerRegistry registry,
            PlayerLoadout loadout,
            EventBus eventBus,
            int backpackContainerId,
            int ammoPouchContainerId,
            EncumbranceProfile encumbranceProfile,
            Action<bool> setCursorLock,
            ItemCatalog itemCatalog = null,
            Action<int, int, int> requestUseItem = null)
        {
            m_Router = router;
            m_Registry = registry;
            m_Loadout = loadout;
            m_EventBus = eventBus;
            m_BackpackContainerId = backpackContainerId;
            m_AmmoPouchContainerId = ammoPouchContainerId;
            m_EncumbranceProfile = encumbranceProfile;
            m_SetCursorLock = setCursorLock;
            m_ItemCatalog = itemCatalog;
            m_RequestUseItem = requestUseItem;

            BuildLayout();
            m_Subscription = m_EventBus.Subscribe<InventoryChangedEvent>(_ => RefreshAll());
            m_EventBus.Subscribe<EncumbranceChangedEvent>(_ => RefreshWeightBar());
            RefreshAll();
            SetVisible(false);
        }

        /// <summary>
        /// 当前展示的战利品容器 ID，0 表示没有。
        /// </summary>
        /// <remarks>
        /// 暴露给装配层用于提示与调试。界面自己不判断「能不能搜刮」——
        /// 那是战局规则，属于逻辑层。
        /// </remarks>
        public int LootContainerId
        {
            get { return m_LootContainerId; }
        }

        /// <summary>
        /// 打开指定战利品容器。
        /// </summary>
        /// <param name="containerId">容器 ID。</param>
        /// <param name="displayName">容器显示名，用于标题。</param>
        /// <remarks>
        /// <para>打开动作包含「顺便把界面也显示出来」：搜刮读条完成之后玩家期待的
        /// 就是能立刻搬东西，若还要再按一次 Tab，读条的意义会被这次多余操作冲淡。</para>
        ///
        /// <para>换一个容器时会重建视图而不是复用：视图与容器是一一对应的，
        /// 复用需要把内部状态全部重绑，出错风险远高于重建一个小小的网格面板。</para>
        /// </remarks>
        public void OpenLootContainer(int containerId, string displayName)
        {
            if (containerId <= 0)
            {
                return;
            }

            if (!m_Registry.TryGetGrid(containerId, out var grid))
            {
                return;
            }

            if (m_LootContainerId != containerId)
            {
                DestroyLootView();
                m_LootContainerId = containerId;
                var title = string.IsNullOrEmpty(displayName) ? "战利品" : $"战利品：{displayName}";
                m_LootView = CreateGridView(
                    (RectTransform)m_Root.transform,
                    grid,
                    containerId,
                    title,
                    m_LootAnchorTopLeft);
            }

            SetVisible(true);
        }

        /// <summary>
        /// 关闭战利品面板。
        /// </summary>
        /// <remarks>
        /// 只销毁界面，容器里的物品留在原处：搜刮的产物是「物品换了位置」，
        /// 而不是「界面被关掉了」。这条区别决定了再打开一次箱子时东西还在不在。
        /// </remarks>
        public void CloseLootContainer()
        {
            DestroyLootView();
            m_LootContainerId = 0;
        }

        /// <summary>销毁战利品视图。没有视图时不做任何事。</summary>
        private void DestroyLootView()
        {
            if (m_LootView != null)
            {
                Destroy(m_LootView.gameObject);
                m_LootView = null;
            }
        }

        private void OnDestroy()
        {
            m_Subscription?.Dispose();
        }

        /// <summary>切换界面开关。</summary>
        public void Toggle()
        {
            SetVisible(!m_IsOpen);
        }

        private void Update()
        {
            if (m_Router == null || !InputEnabled)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                Toggle();
            }

            if (!m_IsOpen)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            var pointer = mouse.position.ReadValue();

            // 菜单打开时它优先吃掉本帧输入：否则点菜单会被当成「开始拖拽」。
            var escapePressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (UpdateContextMenu(
                    pointer,
                    mouse.leftButton.wasPressedThisFrame,
                    mouse.rightButton.wasPressedThisFrame,
                    escapePressed))
            {
                return;
            }

            if (m_IsDragging && keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                m_DragRotated = !m_DragRotated;
            }

            if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
            {
                DispatchSort();
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                BeginPointerDown(pointer);
            }

            if (m_IsDragging && mouse.leftButton.isPressed)
            {
                UpdatePreview(pointer);
            }

            if (mouse.leftButton.wasReleasedThisFrame && m_IsDragging)
            {
                EndDrag(pointer);
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                // Shift + 右键保留「直接拆一半」这条快捷路径：拆分是高频操作，
                // 全部塞进菜单会让整理背包变成两次点击起步。
                if (keyboard != null && keyboard.leftShiftKey.isPressed)
                {
                    HandleRightClickDirect(pointer);
                }
                else
                {
                    OpenContextMenu(pointer);
                }
            }
        }
    }
}
