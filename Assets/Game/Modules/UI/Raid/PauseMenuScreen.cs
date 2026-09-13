using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 暂停菜单：战局/安全屋中按 Esc 打开。
    /// </summary>
    /// <remarks>
    /// <para>它只负责两件事：显示菜单、把按钮点击转成回调。暂停与恢复由 <c>RaidFlowController</c>
    /// 统一处理，因为时间缩放与流程状态不能由界面自己决定。</para>
    ///
    /// <para><b>M7 批次 4 换皮：</b>本类从 <c>RaidScreenFactory</c>（纯色方板 + 旧版 Text）
    /// 迁到 <c>UiFactory</c>。版式刻意与主菜单共用同一套面板语言——奶油纸面、深墨描边、
    /// 略深的标题条、厚片按钮——这样"暂停"看起来和主菜单是同一个游戏，
    /// 而不是两块风格不同的临时界面。</para>
    ///
    /// <para>Esc 的开关判断仍然放在场景装配层（SceneBootstrap / SafeHouseBootstrap）：
    /// 它们要先检查背包、商人、地图面板是否打开，避免与这些界面的 Esc 冲突。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PauseMenuScreen : MonoBehaviour
    {
        /// <summary>面板尺寸（参考像素）。</summary>
        private static readonly Vector2 PanelSize = new Vector2(660f, 460f);

        /// <summary>内容区左边距。</summary>
        private const float Padding = 36f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 78f;

        private RectTransform m_Root;
        private TextMeshProUGUI m_WarningLabel;
        private UiButton m_ReturnButton;
        private UiButton m_QuitButton;
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

            var canvas = UiFactory.CreateCanvas(transform, "PauseMenuCanvas", 320);

            // 遮罩与面板挂在同一个根下一起显隐。遮罩若单独挂在画布上，
            // 关掉面板后它会留在屏幕上把游戏画面永久压暗（这批已经踩过一次）。
            var screen = UiFactory.CreateRect(canvas, "Screen");
            UiFactory.Stretch(screen);
            m_Root = screen;
            UiFactory.CreateVeil(screen, "Veil");

            var panel = UiFactory.CreateCenteredPanel(screen, "Panel", PanelSize, UiSprites.Card);

            BuildTitleBar(panel);
            BuildBody(panel);
            SetVisible(false);
        }

        /// <summary>标题条：界面名 + 恢复方式。</summary>
        private static void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(
                panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            UiFactory.CreateLabel(
                panel,
                "游戏暂停",
                new Vector2(Padding, 20f),
                new Vector2(320f, 40f),
                30f,
                TextAlignmentOptions.Left,
                UiPalette.Ink);

            UiFactory.CreateLabel(
                panel,
                "Esc 继续游戏",
                new Vector2(PanelSize.x - 236f, 28f),
                new Vector2(200f, 26f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Right,
                UiPalette.InkSoft);
        }

        /// <summary>警告、两个按钮与说明。</summary>
        private void BuildBody(RectTransform panel)
        {
            var width = PanelSize.x - (Padding * 2f);

            // 警告行占固定高度、默认隐藏：显示时不会把下面的按钮挤动。
            // 暂停菜单只有一瞬的机会让人读懂，按钮突然位移会打断"该点哪里"的判断。
            m_WarningLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Padding, TitleBarHeight + 18f),
                new Vector2(width, 52f),
                UiPalette.BodySize,
                TextAlignmentOptions.TopLeft,
                UiPalette.Warn,
                wrap: true);
            m_WarningLabel.gameObject.SetActive(false);

            m_ReturnButton = UiFactory.CreateButton(
                panel,
                "返回主界面",
                new Vector2(Padding, TitleBarHeight + 92f),
                new Vector2(280f, 62f),
                UiButtonKind.Primary);

            m_QuitButton = UiFactory.CreateButton(
                panel,
                "返回桌面",
                new Vector2(Padding + 296f, TitleBarHeight + 92f),
                new Vector2(256f, 62f));

            UiFactory.CreateLabel(
                panel,
                "战局中返回主界面会放弃本局；返回桌面后未撤离的随身物品会在下次启动时丢失。",
                new Vector2(Padding, TitleBarHeight + 182f),
                new Vector2(width, 56f),
                UiPalette.SmallSize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkSoft,
                wrap: true);
        }

        /// <summary>显示暂停菜单。</summary>
        /// <param name="warnAbandon">是否显示「战局中返回会丢装备」警告。</param>
        public void Show(bool warnAbandon)
        {
            if (m_Root == null)
            {
                return;
            }

            // 按钮文字跟着警告一起变：只弹警告不改按钮，玩家仍然会以为这是"普通返回"。
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

        /// <summary>
        /// 轮询两个按钮。
        /// </summary>
        /// <remarks>
        /// Esc 的开关不在这里处理，避免与其它界面抢输入；点击仍然走"每帧轮询鼠标"这一套，
        /// 与背包、商人、主菜单完全一致——工程里没有 EventSystem，也不再引入第二套输入通路。
        /// </remarks>
        private void Update()
        {
            if (!m_IsVisible)
            {
                return;
            }

            var mouse = Mouse.current;
            var pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            var isPressed = mouse != null && mouse.leftButton.isPressed;
            var wasPressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

            var overReturn = mouse != null && m_ReturnButton.Contains(pointer);
            var overQuit = mouse != null && m_QuitButton.Contains(pointer);

            // 先刷新悬停（抬起 + 提亮），再把"鼠标是否正按在自己身上"交给按钮。
            // ApplyVisual 内部只在"松开→按下"的那一帧发点击音，因此这里可以每帧无条件调用。
            m_ReturnButton.SetHovered(overReturn);
            m_QuitButton.SetHovered(overQuit);
            m_ReturnButton.ApplyVisual(overReturn && isPressed);
            m_QuitButton.ApplyVisual(overQuit && isPressed);

            if (!wasPressed)
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
