using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Meta;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 商人界面：购买、出售与任务面板。
    /// </summary>
    /// <remarks>
    /// <para><b>本类不直接改数据。</b>购买、出售、接任务、领奖全部翻译成命令，
    /// 通过 <see cref="CommandRouter"/> 交给规则层；界面只根据权威数据重画。
    /// 与背包界面保持同一条单向环路。</para>
    ///
    /// <para>它刻意做成独立界面而不是塞进背包控制器：背包控制器已经有拖拽、
    /// 右键菜单、战利品容器三套状态，再叠加交易页签会让四套状态互相影响。
    /// 商人界面只需要"点选 + 按钮"，独立之后逻辑面小得多。</para>
    ///
    /// <para>右侧始终显示仓库网格：玩家在买与卖之间切换时，看到的都是同一份家底，
    /// 不需要来回开关两个界面核对仓位。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class MerchantScreenController : MonoBehaviour
    {
        /// <summary>界面参考分辨率。</summary>
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        /// <summary>面板尺寸与位置。</summary>
        private const float PanelWidth = 1500f;
        private const float PanelHeight = 820f;

        /// <summary>超过这个金额的出售需要二次确认，避免手滑卖掉金表。</summary>
        private const int HighValueSellThreshold = 10000;

        private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.10f, 0.97f);
        private static readonly Color TabColor = new Color(0.22f, 0.23f, 0.27f);
        private static readonly Color TabActiveColor = new Color(0.18f, 0.46f, 0.80f);
        private static readonly Color TabHoverColor = new Color(0.30f, 0.32f, 0.38f);
        private static readonly Color RowColor = new Color(0.14f, 0.15f, 0.18f, 0.95f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.46f, 0.80f);
        private static readonly Color ButtonHoverColor = new Color(0.26f, 0.58f, 0.94f);
        private static readonly Color DisabledColor = new Color(0.26f, 0.27f, 0.30f);
        private static readonly Color TextColor = new Color(0.94f, 0.94f, 0.96f);
        private static readonly Color DimColor = new Color(0.66f, 0.67f, 0.72f);
        private static readonly Color MoneyColor = new Color(0.95f, 0.82f, 0.35f);
        private static readonly Color SuccessColor = new Color(0.40f, 0.90f, 0.55f);
        private static readonly Color FailureColor = new Color(0.95f, 0.45f, 0.40f);
        private static readonly Color ConfirmPanelColor = new Color(0.10f, 0.10f, 0.13f, 0.99f);

        /// <summary>底部提示条同时承担"最近一次操作结果"，几秒后清空。</summary>
        private const float StatusSeconds = 3f;

        private CommandRouter m_Router;
        private ContainerRegistry m_Registry;
        private MetaProgress m_Progress;
        private ItemCatalog m_Catalog;
        private TraderCatalog m_Trader;
        private EventBus m_EventBus;
        private int m_StashContainerId;
        private Action<bool> m_SetCursorLock;

        private GameObject m_Root;
        private InventoryGridView m_StashView;
        private Text m_MoneyLabel;
        private Text m_StashValueLabel;
        private Text m_StatusLabel;
        private float m_StatusRemaining;

        /// <summary>是否处于批量出售模式。</summary>
        private bool m_SellMode;

        /// <summary>批量出售模式下已选中的物品。</summary>
        private readonly List<SellCandidate> m_SellSelection = new List<SellCandidate>();

        /// <summary>右键菜单当前指向的物品。</summary>
        private ItemInstance m_ContextItem;
        private GridPoint m_ContextCell;

        /// <summary>等待二次确认的出售引用与总价。</summary>
        private SellItemRef[] m_PendingSellRefs;
        private int m_PendingSellTotal;

        /// <summary>界面是否打开。装配层据此冻结角色输入。</summary>
        public bool IsOpen
        {
            get { return m_Root != null && m_Root.activeSelf; }
        }

        /// <summary>
        /// 初始化界面。
        /// </summary>
        public void Initialize(
            CommandRouter router,
            ContainerRegistry registry,
            MetaProgress progress,
            ItemCatalog catalog,
            TraderCatalog trader,
            EventBus eventBus,
            int stashContainerId,
            Action<bool> setCursorLock)
        {
            m_Router = router;
            m_Registry = registry;
            m_Progress = progress;
            m_Catalog = catalog;
            m_Trader = trader;
            m_EventBus = eventBus;
            m_StashContainerId = stashContainerId;
            m_SetCursorLock = setCursorLock;

            BuildLayout();
            if (m_Progress != null)
            {
                m_Progress.Changed += OnMetaChanged;
            }

            ExitSellMode();
            RefreshAll();
            SetVisible(false);
        }

        /// <summary>打开交易界面。</summary>
        public void Open()
        {
            if (m_Root == null)
            {
                return;
            }

            ExitSellMode();
            RefreshAll();
            SetVisible(true);
        }

        /// <summary>关闭交易界面。</summary>
        public void Close()
        {
            if (m_Root == null || !m_Root.activeSelf)
            {
                return;
            }

            CancelSellConfirm();
            CloseSellMenu();
            ExitSellMode();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (m_Progress != null)
            {
                m_Progress.Changed -= OnMetaChanged;
            }
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (m_StatusRemaining > 0f)
            {
                m_StatusRemaining -= Time.unscaledDeltaTime;
                if (m_StatusRemaining <= 0f && m_StatusLabel != null)
                {
                    m_StatusLabel.text = string.Empty;
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (m_ConfirmRoot != null && m_ConfirmRoot.activeSelf)
                {
                    CancelSellConfirm();
                }
                else if (m_SellMenuRoot != null && m_SellMenuRoot.activeSelf)
                {
                    CloseSellMenu();
                }
                else if (m_SellMode)
                {
                    ExitSellMode();
                }
                else
                {
                    Close();
                }

                return;
            }

            if (Mouse.current == null)
            {
                return;
            }

            var pointer = Mouse.current.position.ReadValue();
            if (m_ConfirmRoot != null && m_ConfirmRoot.activeSelf)
            {
                UpdateConfirmInput(pointer);
                return;
            }

            if (m_SellMenuRoot != null && m_SellMenuRoot.activeSelf)
            {
                UpdateSellMenuInput(pointer);
                return;
            }

            UpdateSellControlsInput(pointer);
            if (m_SellMode)
            {
                UpdateSellModeInput(pointer);
                return;
            }

            UpdateTabInput(pointer);
            if (m_ActiveTab == MerchantTab.Buy)
            {
                UpdateBuyInput(pointer);
            }
            else
            {
                UpdateQuestInput(pointer);
            }

            // 非出售模式下，右键仓库物品弹出出售菜单。
            TryOpenSellMenu(pointer);
        }

        private void SetVisible(bool visible)
        {
            if (m_Root != null)
            {
                m_Root.SetActive(visible);
            }

            m_SetCursorLock?.Invoke(!visible);
        }

        /// <summary>局外数据变化后重画。只有打开时才需要，避免无意义开销。</summary>
        private void OnMetaChanged()
        {
            if (IsOpen)
            {
                RefreshAll();
            }
        }

        /// <summary>显示一条底部提示。</summary>
        private void ShowStatus(string message, bool success)
        {
            if (m_StatusLabel == null)
            {
                return;
            }

            m_StatusLabel.text = message;
            m_StatusLabel.color = success ? SuccessColor : FailureColor;
            m_StatusRemaining = StatusSeconds;
        }

    }
}
