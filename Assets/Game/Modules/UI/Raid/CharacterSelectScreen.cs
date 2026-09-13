using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 游戏内角色选择界面：左侧 12 张角色卡，右侧实时 3D 预览。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用实时预览而不是静态头像：</b>角色选择的价值在于“换个人出去”，
    /// 静态头像只能证明有这张图；实时模型能直接看到走路、待机与配色，选完马上能在安全屋里看到结果。</para>
    /// <para>界面本身不读存档、不保存数据：选中回调由启动层写入 MetaProgress 并刷新场景里的玩家。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class CharacterSelectScreen : MonoBehaviour
    {
        /// <summary>一个可选角色。</summary>
        public sealed class CharacterOption
        {
            public CharacterOption(string id, string displayName, GameObject prefab)
            {
                Id = id;
                DisplayName = displayName;
                Prefab = prefab;
            }

            public string Id { get; }

            public string DisplayName { get; }

            public GameObject Prefab { get; }
        }

        private static readonly Vector2 PanelSize = new Vector2(1360f, 760f);

        private const float Padding = 40f;
        private const float TitleBarHeight = 84f;
        private const float CardWidth = 220f;
        private const float CardHeight = 64f;
        private const float CardGapX = 16f;
        private const float CardGapY = 14f;
        private const int CardColumns = 3;

        private readonly List<UiButton> m_Cards = new List<UiButton>(12);
        private readonly List<string> m_CardIds = new List<string>(12);

        private RectTransform m_CanvasHost;
        private GameObject m_Root;
        private UiButton m_BackButton;
        private IReadOnlyList<CharacterOption> m_Options;
        private Action<string> m_OnSelected;
        private Action m_OnClose;
        private string m_SelectedId;
        private bool m_IsVisible;

        /// <summary>界面是否正在显示。</summary>
        public bool IsOpen => m_IsVisible;

        /// <summary>
        /// 构建界面。
        /// </summary>
        /// <param name="options">可选角色列表。</param>
        /// <param name="selectedId">当前选中的角色 id；为空时选第一项。</param>
        /// <param name="onSelected">用户切换角色时调用；只在点击时触发。</param>
        /// <param name="onClose">关闭界面时调用。</param>
        public void Initialize(
            IReadOnlyList<CharacterOption> options,
            string selectedId,
            Action<string> onSelected,
            Action onClose)
        {
            m_Options = options;
            m_OnSelected = onSelected;
            m_OnClose = onClose;

            DestroyLayout();
            BuildLayout();
            ApplySelection(ResolveInitialId(selectedId), notify: false);
            SetVisible(false);
        }

        /// <summary>显示或隐藏界面；隐藏时同步停掉预览相机。</summary>
        public void SetVisible(bool visible)
        {
            m_IsVisible = visible;
            if (m_CanvasHost != null)
            {
                m_CanvasHost.gameObject.SetActive(visible);
            }

            SetPreviewCameraEnabled(visible);
            if (visible)
            {
                RebuildPreviewModel();
            }
        }

        private void Update()
        {
            if (!m_IsVisible)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                UiAudio.Play(UiCue.Cancel);
                m_OnClose?.Invoke();
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                UpdateCardInput(mouse);
                UpdateBackButton(mouse);
            }

            RotatePreviewModel();
        }

        private void UpdateCardInput(Mouse mouse)
        {
            var pointer = mouse.position.ReadValue();
            for (var i = 0; i < m_Cards.Count; i++)
            {
                var card = m_Cards[i];
                var hovered = card.Contains(pointer);
                card.SetHovered(hovered);
                card.ApplyVisual(hovered && mouse.leftButton.isPressed);

                if (hovered && mouse.leftButton.wasPressedThisFrame)
                {
                    ApplySelection(m_CardIds[i], notify: true);
                }
            }
        }

        private void UpdateBackButton(Mouse mouse)
        {
            if (m_BackButton == null)
            {
                return;
            }

            var pointer = mouse.position.ReadValue();
            var hovered = m_BackButton.Contains(pointer);
            m_BackButton.SetHovered(hovered);
            m_BackButton.ApplyVisual(hovered && mouse.leftButton.isPressed);
            if (hovered && mouse.leftButton.wasPressedThisFrame)
            {
                m_OnClose?.Invoke();
            }
        }

        /// <summary>切换选中角色并刷新卡片与预览。</summary>
        private void ApplySelection(string id, bool notify)
        {
            m_SelectedId = id;
            RefreshCardVariants();
            RebuildPreviewModel();

            if (notify)
            {
                UiAudio.Play(UiCue.Confirm);
                m_OnSelected?.Invoke(id);
            }
        }

        private void RefreshCardVariants()
        {
            for (var i = 0; i < m_Cards.Count; i++)
            {
                var selected = m_CardIds[i] == m_SelectedId;
                m_Cards[i].SetVariant(selected ? UiButtonKind.Primary : UiButtonKind.Normal);
            }
        }

        private string ResolveInitialId(string requested)
        {
            if (m_Options == null || m_Options.Count == 0)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(requested))
            {
                for (var i = 0; i < m_Options.Count; i++)
                {
                    if (m_Options[i].Id == requested)
                    {
                        return requested;
                    }
                }
            }

            return m_Options[0].Id;
        }

        private CharacterOption FindOption(string id)
        {
            if (m_Options == null)
            {
                return null;
            }

            for (var i = 0; i < m_Options.Count; i++)
            {
                if (m_Options[i].Id == id)
                {
                    return m_Options[i];
                }
            }

            return null;
        }

        private void BuildLayout()
        {
            m_CanvasHost = UiFactory.CreateCanvas(transform, "CharacterSelectCanvas", 330);
            UiFactory.CreateBackdrop(m_CanvasHost, "Backdrop", UiPalette.MenuBackdrop);

            var panel = UiFactory.CreateCenteredPanel(
                m_CanvasHost, "Panel", PanelSize, UiSprites.Card);
            m_Root = panel.gameObject;

            BuildTitleBar(panel);
            BuildCards(panel);
            BuildPreview(panel);

            m_BackButton = UiFactory.CreateButton(
                panel,
                "返回",
                new Vector2((PanelSize.x * 0.5f) - 90f, PanelSize.y - 76f),
                new Vector2(180f, 52f));
        }

        private static void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(
                panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            UiFactory.CreateLabel(
                panel, "选择角色", new Vector2(Padding, 18f), new Vector2(420f, 48f),
                UiPalette.TitleSize * 0.7f, TextAlignmentOptions.Left, UiPalette.Ink);
            UiFactory.CreateLabel(
                panel, "点击卡片切换 · Esc 返回", new Vector2(PanelSize.x - 420f, 26f),
                new Vector2(380f, 30f), UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft);
        }

        private void BuildCards(RectTransform panel)
        {
            if (m_Options == null)
            {
                return;
            }

            for (var i = 0; i < m_Options.Count; i++)
            {
                var option = m_Options[i];
                var column = i % CardColumns;
                var row = i / CardColumns;
                var x = Padding + (column * (CardWidth + CardGapX));
                var y = TitleBarHeight + Padding + (row * (CardHeight + CardGapY));

                var card = UiFactory.CreateButton(
                    panel,
                    option.DisplayName,
                    new Vector2(x, y),
                    new Vector2(CardWidth, CardHeight));
                m_Cards.Add(card);
                m_CardIds.Add(option.Id);
            }
        }

        private void OnDestroy()
        {
            DestroyLayout();
        }

        private void DestroyLayout()
        {
            CleanupPreview();
            if (m_CanvasHost != null)
            {
                Destroy(m_CanvasHost.gameObject);
                m_CanvasHost = null;
            }

            m_Root = null;
            m_BackButton = null;
            m_Cards.Clear();
            m_CardIds.Clear();
        }
    }
}
