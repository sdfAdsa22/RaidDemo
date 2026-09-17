using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 主菜单：左侧品牌区（大标题 + 导语 + 信息胶囊）＋ 右侧竖排菜单（继续 / 新游戏 / 联机 / 退出）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么品牌区要带玩法导语：</b>这个 Demo 会被不认识它的人打开（面试官、同学）。
    /// 没有说明时，他们大概率会按 WASD 走两步、开两枪就关掉，
    /// 而搜刮（E）与撤离（站在绿圈里）这两个真正体现设计的地方根本不会被看到。
    /// 详细操作写在安全屋的墙上，这里只留一句话级别的导语。</para>
    ///
    /// <para><b>这是一块不透明独立界面</b>（负责人 2026-09-13 决定）：它不叠在战局场景上，
    /// 而是一整块深绿松底色 + 居中大卡片。2026-09-17 负责人从两个 HTML 小样里选了
    /// 「左品牌、右菜单」的方案 B：左边奶油纸面承载品牌与导语，右边深绿松底色独立成按钮区，
    /// 视线落点比"所有元素挤在一列"更集中。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class MainMenuScreen : MonoBehaviour
    {
        /// <summary>卡片尺寸（参考像素）：1440×760 在 1920×1080 参考分辨率下四周仍留出呼吸空间。</summary>
        private static readonly Vector2 PanelSize = new Vector2(1440f, 760f);

        private RectTransform m_Root;
        private UiButton m_ContinueButton;
        private UiButton m_NewGameButton;
        private UiButton m_MultiplayerButton;
        private UiButton m_QuitButton;
        private TextMeshProUGUI m_NoticeLabel;
        private Action m_OnContinue;
        private Action m_OnNewGame;
        private Action m_OnMultiplayer;
        private Action m_OnQuit;
        private bool m_IsVisible;
        private bool m_HasSave;
        private bool m_ConfirmNewGame;
        private float m_ConfirmRemaining;

        /// <summary>构建界面。</summary>
        /// <param name="onContinue">点击「继续 / 开始」时执行的回调。</param>
        /// <param name="onNewGame">点击「新游戏」时执行的回调。</param>
        /// <param name="onMultiplayer">
        /// 点击「联机」时执行的回调；为 null 时该按钮隐藏。
        /// </param>
        /// <param name="onQuit">
        /// 点击「退出游戏」时执行的回调；为 null 时该按钮隐藏。
        /// </param>
        /// <remarks>
        /// 后两项做成可选参数而不是重载：调用点只有装配层一处，
        /// 传 null 表示"这个版本没有该入口"，与界面无关的构建（自动化、测试宿主）也能照旧编译。
        /// </remarks>
        public void Initialize(Action onContinue, Action onNewGame, Action onMultiplayer = null, Action onQuit = null)
        {
            m_OnContinue = onContinue;
            m_OnNewGame = onNewGame;
            m_OnMultiplayer = onMultiplayer;
            m_OnQuit = onQuit;

            m_Root = UiFactory.CreateCanvas(transform, "MainMenuCanvas", 300);

            // 不透明底：主菜单是独立界面，背后没有游戏画面。
            UiFactory.CreateBackdrop(m_Root, "Backdrop", UiPalette.MenuBackdrop);
            BuildBackdropDecor(m_Root);

            // 面板投影：同一张九宫格贴图压暗后向下偏移，做出"厚卡片浮在底上"的层次。
            var shadow = UiFactory.CreateCenteredPanel(m_Root, "PanelShadow", PanelSize, UiSprites.Card);
            shadow.anchoredPosition = new Vector2(0f, -12f);
            shadow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);

            var panel = UiFactory.CreateCenteredPanel(m_Root, "Panel", PanelSize, UiSprites.Card);

            BuildBrandColumn(panel);
            BuildMenuColumn(panel);
            SetVisible(false);
        }

        /// <summary>告诉菜单是否存在存档，据此切换主按钮文字与「新游戏」是否显示。</summary>
        public void SetHasSave(bool hasSave)
        {
            m_HasSave = hasSave;
            m_ContinueButton.Label.text = hasSave ? "继续游戏（Enter）" : "开始游戏（Enter）";
            m_NewGameButton.Rect.gameObject.SetActive(hasSave);
            LayoutButtonColumn();
            if (!hasSave)
            {
                ResetNewGameConfirm();
            }
        }

        /// <summary>显示一次性提示（例如强退战局的惩罚）。</summary>
        public void SetNotice(string notice)
        {
            var has = !string.IsNullOrEmpty(notice);
            m_NoticeLabel.gameObject.SetActive(has);
            if (has)
            {
                m_NoticeLabel.text = notice;
            }
        }

        /// <summary>显示或隐藏菜单。</summary>
        public void SetVisible(bool visible)
        {
            m_IsVisible = visible;
            // 菜单隐藏或重新显示时清掉"新游戏二次确认"，
            // 否则返回菜单时会残留上一轮的确认文字。
            ResetNewGameConfirm();
            if (m_Root != null)
            {
                m_Root.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 轮询鼠标与回车键。
        /// </summary>
        /// <remarks>
        /// 只在可见时处理输入：菜单隐藏后若仍然读取鼠标，
        /// 玩家在战局里点一次左键就会意外触发「出击」。
        /// </remarks>
        private void Update()
        {
            if (!m_IsVisible || m_OnContinue == null)
            {
                return;
            }

            if (m_ConfirmNewGame)
            {
                m_ConfirmRemaining -= Time.unscaledDeltaTime;
                if (m_ConfirmRemaining <= 0f)
                {
                    ResetNewGameConfirm();
                }
            }

            var keyboard = Keyboard.current;
            var pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            var isClicked = Mouse.current != null && Mouse.current.leftButton.isPressed;
            var wasPressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

            var overContinue = Mouse.current != null && m_ContinueButton.Contains(pointer);
            var overNewGame = m_HasSave && Mouse.current != null && m_NewGameButton.Contains(pointer);
            var overMultiplayer = m_OnMultiplayer != null && Mouse.current != null && m_MultiplayerButton.Contains(pointer);
            var overQuit = m_OnQuit != null && Mouse.current != null && m_QuitButton.Contains(pointer);

            m_ContinueButton.SetHovered(overContinue);
            m_NewGameButton.SetHovered(overNewGame);
            m_MultiplayerButton.SetHovered(overMultiplayer);
            m_QuitButton.SetHovered(overQuit);
            m_ContinueButton.ApplyVisual(overContinue && isClicked);
            m_NewGameButton.ApplyVisual(overNewGame && isClicked);
            m_MultiplayerButton.ApplyVisual(overMultiplayer && isClicked);
            m_QuitButton.ApplyVisual(overQuit && isClicked);

            var confirmed = keyboard != null && keyboard.enterKey.wasPressedThisFrame;

            // Esc 不再退出游戏（负责人 2026-09-17：退出改为显式按钮，
            // 避免玩家想"取消/关闭"时直接把进程退掉）。
            // 这里只保留它最后一个职责：二次确认期间按 Esc 等价于点别的按钮，取消确认。
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && m_ConfirmNewGame)
            {
                ResetNewGameConfirm();
                return;
            }

            if ((wasPressed && overContinue) || confirmed)
            {
                ResetNewGameConfirm();
                m_OnContinue.Invoke();
                return;
            }

            if (wasPressed && overMultiplayer)
            {
                ResetNewGameConfirm();
                m_OnMultiplayer.Invoke();
                return;
            }

            if (wasPressed && overQuit)
            {
                ResetNewGameConfirm();
                m_OnQuit.Invoke();
                return;
            }

            if (wasPressed && overNewGame)
            {
                if (!m_ConfirmNewGame)
                {
                    m_ConfirmNewGame = true;
                    m_ConfirmRemaining = 4f;
                    m_NewGameButton.Label.text = "再次点击确认清空进度";
                }
                else
                {
                    ResetNewGameConfirm();
                    m_OnNewGame?.Invoke();
                }
            }
        }

        private void ResetNewGameConfirm()
        {
            m_ConfirmNewGame = false;
            m_ConfirmRemaining = 0f;
            if (m_NewGameButton != null)
            {
                m_NewGameButton.Label.text = "新游戏";
            }
        }
    }
}
