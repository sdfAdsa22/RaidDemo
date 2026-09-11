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
        private int m_LootContainerId;
        private int m_AmmoPouchContainerId;
        private EncumbranceProfile m_EncumbranceProfile;
        private Action<bool> m_SetCursorLock;
        private IDisposable m_Subscription;

        private GameObject m_Root;
        private InventoryGridView m_BackpackView;
        private InventoryGridView m_AmmoPouchView;
        private InventoryGridView m_LootView;
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
        /// 初始化界面。
        /// </summary>
        /// <param name="router">命令路由。</param>
        /// <param name="registry">容器注册表。</param>
        /// <param name="loadout">角色携带物。</param>
        /// <param name="eventBus">事件总线。</param>
        /// <param name="backpackContainerId">主背包的容器 ID。</param>
        /// <param name="lootContainerId">战利品容器的容器 ID。</param>
        /// <param name="ammoPouchContainerId">弹药挂的容器 ID。</param>
        /// <param name="encumbranceProfile">负重配置，用于显示承载上限。</param>
        /// <param name="setCursorLock">光标锁定开关。界面需要解锁光标才能用鼠标拖拽。</param>
        public void Initialize(
            CommandRouter router,
            ContainerRegistry registry,
            PlayerLoadout loadout,
            EventBus eventBus,
            int backpackContainerId,
            int lootContainerId,
            int ammoPouchContainerId,
            EncumbranceProfile encumbranceProfile,
            Action<bool> setCursorLock)
        {
            m_Router = router;
            m_Registry = registry;
            m_Loadout = loadout;
            m_EventBus = eventBus;
            m_BackpackContainerId = backpackContainerId;
            m_LootContainerId = lootContainerId;
            m_AmmoPouchContainerId = ammoPouchContainerId;
            m_EncumbranceProfile = encumbranceProfile;
            m_SetCursorLock = setCursorLock;

            BuildLayout();
            m_Subscription = m_EventBus.Subscribe<InventoryChangedEvent>(_ => RefreshAll());
            m_EventBus.Subscribe<EncumbranceChangedEvent>(_ => RefreshWeightBar());
            RefreshAll();
            SetVisible(false);
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
            if (m_Router == null)
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
                HandleRightClick(pointer);
            }
        }
    }
}
