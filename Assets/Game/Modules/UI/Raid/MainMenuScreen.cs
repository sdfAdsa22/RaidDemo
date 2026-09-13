using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 主菜单：继续 / 新游戏 / 退出，加上一页操作说明。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有操作说明：</b>这个 Demo 会被不认识它的人打开（面试官、同学）。
    /// 没有说明时，他们大概率会按 WASD 走两步、开两枪就关掉，
    /// 而搜刮（E）与撤离（站在绿圈里）这两个真正体现设计的地方根本不会被看到。</para>
    ///
    /// <para><b>这是一块不透明独立界面</b>（负责人 2026-09-13 决定）：它不叠在战局场景上，
    /// 而是一整块深绿松底色 + 居中奶油面板。理由是这样主菜单给人的第一印象是"一个成品"，
    /// 而不是"一局游戏暂停了"；战局场景在菜单状态下不推进，也就没有必要露出来。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MainMenuScreen : MonoBehaviour
    {
        /// <summary>面板尺寸（参考像素）。</summary>
        private static readonly Vector2 PanelSize = new Vector2(900f, 660f);

        /// <summary>面板内边距。</summary>
        private const float Padding = 40f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 100f;

        private RectTransform m_Root;
        private UiButton m_ContinueButton;
        private UiButton m_NewGameButton;
        private TextMeshProUGUI m_NoticeLabel;
        private Action m_OnContinue;
        private Action m_OnNewGame;
        private bool m_IsVisible;
        private bool m_HasSave;
        private bool m_ConfirmNewGame;
        private float m_ConfirmRemaining;

        /// <summary>构建界面。</summary>
        /// <param name="onContinue">点击「继续 / 开始」时执行的回调。</param>
        /// <param name="onNewGame">点击「新游戏」时执行的回调。</param>
        public void Initialize(Action onContinue, Action onNewGame)
        {
            m_OnContinue = onContinue;
            m_OnNewGame = onNewGame;

            m_Root = UiFactory.CreateCanvas(transform, "MainMenuCanvas", 300);

            // 不透明底：主菜单是独立界面，背后没有游戏画面。
            UiFactory.CreateBackdrop(m_Root, "Backdrop", UiPalette.MenuBackdrop);

            // 面板投影：同一张九宫格贴图压暗后向下偏移，做出"厚卡片浮在底上"的层次。
            var shadow = UiFactory.CreateCenteredPanel(m_Root, "PanelShadow", PanelSize, UiSprites.Card);
            shadow.anchoredPosition = new Vector2(0f, -12f);
            shadow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);

            var panel = UiFactory.CreateCenteredPanel(m_Root, "Panel", PanelSize, UiSprites.Card);

            BuildTitleBar(panel);
            BuildBody(panel);
            SetVisible(false);
        }

        /// <summary>标题条：略深的一条纸 + 标题 + 版本号。</summary>
        private static void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            // 标题阴影：卡通风格里最省的立体感做法——同一句话用半透明墨色错开 3 像素再画一遍。
            UiFactory.CreateLabel(
                panel, "RAID DEMO", new Vector2(Padding + 3f, 22f + 3f), new Vector2(600f, 64f),
                UiPalette.TitleSize, TextAlignmentOptions.Left, new Color(UiPalette.Outline.r, UiPalette.Outline.g, UiPalette.Outline.b, 0.22f));
            UiFactory.CreateLabel(
                panel, "RAID DEMO", new Vector2(Padding, 22f), new Vector2(600f, 64f),
                UiPalette.TitleSize, TextAlignmentOptions.Left, UiPalette.Ink);

            UiFactory.CreateLabel(
                panel, "M7 · Windows", new Vector2(PanelSize.x - 300f, 38f), new Vector2(260f, 34f),
                UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft);
        }

        /// <summary>说明文字、按钮与提示。</summary>
        private void BuildBody(RectTransform panel)
        {
            var top = TitleBarHeight;

            UiFactory.CreateLabel(
                panel,
                "3D 斜俯视搜打撤 · 卡通低多边形",
                new Vector2(Padding, top + 26f),
                new Vector2(PanelSize.x - (Padding * 2f), 34f),
                UiPalette.SubtitleSize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            m_ContinueButton = UiFactory.CreateButton(
                panel,
                "开始游戏（Enter）",
                new Vector2(Padding, top + 88f),
                new Vector2(420f, 88f),
                UiButtonKind.Primary);

            m_NewGameButton = UiFactory.CreateButton(
                panel,
                "新游戏",
                new Vector2(Padding + 440f, top + 88f),
                new Vector2(320f, 88f));

            m_NoticeLabel = UiFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(Padding, top + 200f),
                new Vector2(PanelSize.x - (Padding * 2f), 40f),
                UiPalette.BodySize,
                TextAlignmentOptions.Left,
                UiPalette.Warn);
            m_NoticeLabel.gameObject.SetActive(false);

            UiFactory.CreateLabel(
                panel,
                "在一块不大的安全屋里，你可以整理仓库、试枪、从出口选地图出击。\n"
                + "操作说明写在安全屋的墙上；出击前的准备也都在那里完成。",
                new Vector2(Padding, top + 250f),
                new Vector2(PanelSize.x - (Padding * 2f), 96f),
                UiPalette.BodySize,
                TextAlignmentOptions.TopLeft,
                UiPalette.Ink,
                wrap: true);

            UiFactory.CreateLabel(
                panel,
                "WASD 移动 · Shift 奔跑 · 左键射击 · R 换弹 · Tab 背包 · F1 调试",
                new Vector2(Padding, PanelSize.y - 96f),
                new Vector2(PanelSize.x - (Padding * 2f), 30f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            UiFactory.CreateLabel(
                panel,
                "单人 · 一局 8 分钟　｜　Esc 退出游戏",
                new Vector2(Padding, PanelSize.y - 58f),
                new Vector2(PanelSize.x - (Padding * 2f), 30f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.InkDisabled);
        }

        /// <summary>告诉菜单是否存在存档，据此切换主按钮文字与「新游戏」是否显示。</summary>
        public void SetHasSave(bool hasSave)
        {
            m_HasSave = hasSave;
            m_ContinueButton.Label.text = hasSave ? "继续游戏（Enter）" : "开始游戏（Enter）";
            m_NewGameButton.Rect.gameObject.SetActive(hasSave);
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

            m_ContinueButton.SetHovered(overContinue);
            m_NewGameButton.SetHovered(overNewGame);
            m_ContinueButton.ApplyVisual(overContinue && isClicked);
            m_NewGameButton.ApplyVisual(overNewGame && isClicked);

            var confirmed = keyboard != null && keyboard.enterKey.wasPressedThisFrame;

            // Esc 退出游戏。编辑器里退出播放模式，构建里真正退出——
            // 否则在编辑器里点「退出」会毫无反应，看起来像坏了。
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (m_ConfirmNewGame)
                {
                    // 确认期间 Esc 先取消确认；直接退出会让玩家以为按钮坏了。
                    ResetNewGameConfirm();
                    return;
                }

#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }

            if ((wasPressed && overContinue) || confirmed)
            {
                ResetNewGameConfirm();
                m_OnContinue.Invoke();
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
