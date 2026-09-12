using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 暂停菜单：战局/安全屋中按 Esc 打开。
    /// </summary>
    /// <remarks>
    /// <para>它只负责两件事：显示菜单、把按钮点击转成回调。
    /// 暂停与恢复由 <c>RaidFlowController</c> 统一处理，
    /// 因为时间缩放与流程状态不能由界面自己决定。</para>
    ///
    /// <para>Esc 的开关判断放在场景装配层（SceneBootstrap / SafeHouseBootstrap），
    /// 这样它们可以先检查背包、商人、地图面板是否打开，避免与这些界面的 Esc 冲突。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PauseMenuScreen : MonoBehaviour
    {
        private static readonly Color OverlayColor = new Color(0.03f, 0.04f, 0.06f, 0.82f);
        private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.13f, 0.98f);
        private static readonly Color TitleColor = new Color(0.96f, 0.94f, 0.88f);
        private static readonly Color BodyColor = new Color(0.78f, 0.79f, 0.84f);
        private static readonly Color WarningColor = new Color(0.95f, 0.65f, 0.35f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.46f, 0.80f);
        private static readonly Color ButtonHoverColor = new Color(0.26f, 0.58f, 0.94f);
        private static readonly Color QuitColor = new Color(0.52f, 0.24f, 0.24f);
        private static readonly Color QuitHoverColor = new Color(0.66f, 0.30f, 0.30f);

        private RectTransform m_Root;
        private Text m_WarningLabel;
        private RaidButtonWidget m_ReturnButton;
        private RaidButtonWidget m_QuitButton;
        private Action m_OnReturnToMainMenu;
        private Action m_OnQuit;
        private bool m_IsVisible;

        /// <summary>暂停菜单是否正在显示。</summary>
        public bool IsVisible
        {
            get { return m_IsVisible; }
        }

        /// <summary>构建界面。</summary>
        /// <param name="onReturnToMainMenu">点击「返回主界面」时执行的回调。</param>
        /// <param name="onQuit">点击「返回桌面」时执行的回调。</param>
        public void Initialize(Action onReturnToMainMenu, Action onQuit)
        {
            m_OnReturnToMainMenu = onReturnToMainMenu;
            m_OnQuit = onQuit;

            m_Root = RaidScreenFactory.CreateCanvas(transform, "PauseMenuCanvas", 320);
            RaidScreenFactory.CreatePanel(
                m_Root,
                "Overlay",
                new Vector2(RaidScreenFactory.ReferenceWidth, RaidScreenFactory.ReferenceHeight),
                OverlayColor);

            var panel = RaidScreenFactory.CreatePanel(m_Root, "Panel", new Vector2(620f, 420f), PanelColor);

            RaidScreenFactory.CreateLabel(
                panel, "游戏暂停", new Vector2(40f, 34f), new Vector2(540f, 48f),
                34, TextAnchor.MiddleLeft, TitleColor);

            RaidScreenFactory.CreateLabel(
                panel, "按 Esc 继续游戏", new Vector2(40f, 96f), new Vector2(540f, 26f),
                17, TextAnchor.MiddleLeft, BodyColor);

            m_WarningLabel = RaidScreenFactory.CreateLabel(
                panel, string.Empty, new Vector2(40f, 142f), new Vector2(540f, 52f),
                16, TextAnchor.UpperLeft, WarningColor);
            m_WarningLabel.gameObject.SetActive(false);

            m_ReturnButton = RaidScreenFactory.CreateButton(
                panel, "返回主界面", new Vector2(40f, 240f), new Vector2(250f, 62f),
                ButtonColor, ButtonHoverColor);

            m_QuitButton = RaidScreenFactory.CreateButton(
                panel, "返回桌面", new Vector2(330f, 240f), new Vector2(250f, 62f),
                QuitColor, QuitHoverColor);

            RaidScreenFactory.CreateLabel(
                panel,
                "战局中返回主界面会放弃本局；返回桌面后未撤离的随身物品会在下次启动时丢失。",
                new Vector2(40f, 330f), new Vector2(540f, 52f),
                14, TextAnchor.UpperLeft, BodyColor);

            SetVisible(false);
        }

        /// <summary>显示暂停菜单。</summary>
        /// <param name="warnAbandon">是否显示「战局中返回会丢装备」警告。</param>
        public void Show(bool warnAbandon)
        {
            if (m_Root == null)
            {
                return;
            }

            m_ReturnButton.Label.text = warnAbandon ? "返回主界面（放弃本局）" : "返回主界面";
            m_WarningLabel.gameObject.SetActive(warnAbandon);
            m_WarningLabel.text = warnAbandon
                ? "注意：本局尚未撤离，随身携带的装备与物资会全部丢失。"
                : string.Empty;
            SetVisible(true);
        }

        /// <summary>隐藏暂停菜单。</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            m_IsVisible = visible;
            if (m_Root != null)
            {
                m_Root.gameObject.SetActive(visible);
            }
        }

        /// <summary>轮询两个按钮。Esc 的开关不在这里处理，避免与其它界面抢输入。</summary>
        private void Update()
        {
            if (!m_IsVisible || Mouse.current == null)
            {
                return;
            }

            var pointer = Mouse.current.position.ReadValue();
            var overReturn = m_ReturnButton.Contains(pointer);
            var overQuit = m_QuitButton.Contains(pointer);
            m_ReturnButton.SetHovered(overReturn);
            m_QuitButton.SetHovered(overQuit);

            if (!Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (overReturn)
            {
                m_OnReturnToMainMenu?.Invoke();
            }
            else if (overQuit)
            {
                m_OnQuit?.Invoke();
            }
        }
    }
}
