using System;
using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 收集图鉴界面：按类别分页展示全部物品，已获得的点亮、未获得的显示黑影。
    /// </summary>
    /// <remarks>
    /// <para><b>它只读不写。</b>点亮发生在获得物品的那一刻（CodexMarker 与
    /// MetaProgress.RefreshCodex），界面只负责把 MetaCodex 的现状画出来。
    /// 这样即使图鉴界面从未打开过，收集进度也在照常累积。</para>
    ///
    /// <para><b>未获得条目显示为黑影加问号</b>（负责人 2026-09-13 决定）：
    /// 保留剪影与稀有度描边作为线索，但不剧透名称与数值——
    /// 收集类界面的乐趣一半来自还差哪几件的悬念。</para>
    ///
    /// <para>数据来源是场景里的 ItemCatalog（游戏里一共有哪些物品的唯一权威），
    /// 而不是存档——存档里只有已点亮的 ID，两者相交才是界面上看到的格子。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class CodexScreenController : MonoBehaviour
    {
        /// <summary>图鉴的类别页签。</summary>
        private enum CodexFilter
        {
            /// <summary>全部。</summary>
            All = 0,

            /// <summary>武器。</summary>
            Weapon = 1,

            /// <summary>弹药。</summary>
            Ammo = 2,

            /// <summary>护甲（头盔 + 躯干）。</summary>
            Armor = 3,

            /// <summary>医疗。</summary>
            Medical = 4,

            /// <summary>背包。</summary>
            Backpack = 5,

            /// <summary>杂物（战利品 + 钥匙 + 任务物品）。</summary>
            Misc = 6,
        }

        /// <summary>一张物品卡的状态。</summary>
        private sealed class CardWidget
        {
            public ItemDefinition Definition;
            public RectTransform Rect;
            public Image Background;
            public Image Outline;
            public Image Icon;
            public Image RarityStrip;
            public TextMeshProUGUI Name;
            public bool Discovered;
        }

        /// <summary>一个页签的状态。</summary>
        private sealed class TabWidget
        {
            public CodexFilter Filter;
            public UiButton Button;
        }

        /// <summary>界面层级：高于结算与暂停面板，低于角色选择。</summary>
        private const int SortingOrder = 325;

        /// <summary>面板尺寸（参考像素）。与商人、背包同一套尺寸语言。</summary>
        private static readonly Vector2 PanelSize = new Vector2(1500f, 780f);

        /// <summary>未获得条目的名称占位。</summary>
        private const string UnknownName = "？？？";

        private MetaCodex m_Codex;
        private ItemCatalog m_Catalog;
        private Action m_OnClose;

        private GameObject m_ScreenRoot;
        private TextMeshProUGUI m_ProgressLabel;
        private readonly List<TabWidget> m_Tabs = new List<TabWidget>();
        private readonly List<CardWidget> m_Cards = new List<CardWidget>();
        private CodexFilter m_Filter = CodexFilter.All;
        private CardWidget m_Selected;
        private UiButton m_CloseButton;

        private Image m_DetailIcon;
        private TextMeshProUGUI m_DetailName;
        private TextMeshProUGUI m_DetailMeta;
        private TextMeshProUGUI m_DetailBaseTitle;
        private TextMeshProUGUI m_DetailBase;
        private Image m_DetailStatsDivider;
        private TextMeshProUGUI m_DetailStatsTitle;
        private TextMeshProUGUI m_DetailStats;
        private Image m_DetailDescriptionDivider;
        private TextMeshProUGUI m_DetailDescription;
        private TextMeshProUGUI m_DetailDescriptionTitle;

        /// <summary>界面是否正在显示。装配层据此冻结角色输入。</summary>
        public bool IsOpen
        {
            get { return m_ScreenRoot != null && m_ScreenRoot.activeSelf; }
        }

        /// <summary>
        /// 构建界面。
        /// </summary>
        /// <param name="catalog">物品目录：决定图鉴里一共有哪些条目。</param>
        /// <param name="codex">收集进度。</param>
        /// <param name="onClose">关闭界面时调用。</param>
        public void Initialize(ItemCatalog catalog, MetaCodex codex, Action onClose)
        {
            m_Catalog = catalog;
            m_Codex = codex;
            m_OnClose = onClose;

            BuildLayout();
            RefreshAll();
            SetVisible(false);
        }

        /// <summary>打开图鉴。</summary>
        public void Open()
        {
            if (m_ScreenRoot == null)
            {
                return;
            }

            RefreshAll();
            PreferDiscoveredSelection();
            SetVisible(true);
            UiAudio.Play(UiCue.PanelOpen);
        }

        /// <summary>关闭图鉴。</summary>
        public void Close()
        {
            if (m_ScreenRoot == null || !m_ScreenRoot.activeSelf)
            {
                return;
            }

            SetVisible(false);
            UiAudio.Play(UiCue.PanelClose);
            m_OnClose?.Invoke();
        }

        /// <summary>
        /// 统计目录里已经点亮的条目数。
        /// </summary>
        /// <param name="catalog">物品目录；为 null 时返回 0。</param>
        /// <param name="codex">收集进度；为 null 时返回 0。</param>
        /// <remarks>安全屋墙上那块展示牌与界面标题共用这一份统计，避免两处各写一套循环。</remarks>
        public static int CountDiscovered(ItemCatalog catalog, MetaCodex codex)
        {
            if (catalog == null || codex == null)
            {
                return 0;
            }

            var items = catalog.All;
            var count = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (codex.Contains(items[i].Id))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>统计某件物品是否已被点亮。目录缺失视为未点亮。</summary>
        private bool IsDiscovered(ItemDefinition definition)
        {
            return m_Codex != null && definition != null && m_Codex.Contains(definition.Id);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            var pointer = mouse.position.ReadValue();
            UpdateTabInput(pointer);
            UpdateCardInput(pointer);
            UpdateCloseInput(pointer);
        }

        private void SetVisible(bool visible)
        {
            if (m_ScreenRoot != null)
            {
                m_ScreenRoot.SetActive(visible);
            }
        }

        private void OnDestroy()
        {
            m_OnClose = null;
        }
    }
}
