using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 极简主菜单：继续 / 新游戏 / 退出，加上一页操作说明。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要放操作说明：</b>这个 Demo 会被不认识它的人打开（面试官、同学）。
    /// 没有说明时，他们大概率会按 WASD 走两步、开两枪就关掉，
    /// 而搜刮（E）与撤离（站在绿圈里）这两个真正体现设计的地方根本不会被看到。</para>
    ///
    /// <para>菜单只是遮罩，背后的战局场景保持可见：这让界面看起来像游戏的一部分，
    /// 而不是一张孤零零的图片。战局逻辑在菜单状态下完全不推进（由装配层控制）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MainMenuScreen : MonoBehaviour
    {
        private static readonly Color OverlayColor = new Color(0.04f, 0.05f, 0.07f, 0.93f);
        private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.13f, 0.97f);
        private static readonly Color TitleColor = new Color(0.96f, 0.94f, 0.88f);
        private static readonly Color BodyColor = new Color(0.80f, 0.81f, 0.86f);
        private static readonly Color HintColor = new Color(0.62f, 0.63f, 0.68f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.46f, 0.80f);
        private static readonly Color ButtonHoverColor = new Color(0.26f, 0.58f, 0.94f);


        private RectTransform m_Root;
        private RaidButtonWidget m_ContinueButton;
        private RaidButtonWidget m_NewGameButton;
        private Text m_NoticeLabel;
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

            m_Root = RaidScreenFactory.CreateCanvas(transform, "MainMenuCanvas", 300);
            RaidScreenFactory.CreatePanel(
                m_Root,
                "Overlay",
                new Vector2(RaidScreenFactory.ReferenceWidth, RaidScreenFactory.ReferenceHeight),
                OverlayColor);

            var panel = RaidScreenFactory.CreatePanel(m_Root, "Panel", new Vector2(800f, 560f), PanelColor);

            RaidScreenFactory.CreateLabel(
                panel, "RAID DEMO", new Vector2(48f, 36f), new Vector2(700f, 60f),
                46, TextAnchor.MiddleLeft, TitleColor);
            RaidScreenFactory.CreateLabel(
                panel, "3D 斜俯视搜打撤 · 灰盒演示 · M5 战局闭环",
                new Vector2(48f, 100f), new Vector2(700f, 30f),
                19, TextAnchor.MiddleLeft, HintColor);

            m_ContinueButton = RaidScreenFactory.CreateButton(
                panel,
                "开始游戏（Enter）",
                new Vector2(48f, 170f),
                new Vector2(300f, 68f),
                ButtonColor,
                ButtonHoverColor);

            m_NewGameButton = RaidScreenFactory.CreateButton(
                panel,
                "新游戏",
                new Vector2(368f, 170f),
                new Vector2(240f, 68f),
                new Color(0.28f, 0.29f, 0.33f),
                new Color(0.36f, 0.37f, 0.42f));

            m_NoticeLabel = RaidScreenFactory.CreateLabel(
                panel,
                string.Empty,
                new Vector2(48f, 248f), new Vector2(700f, 44f),
                16, TextAnchor.UpperLeft,
                new Color(0.95f, 0.72f, 0.35f));
            m_NoticeLabel.gameObject.SetActive(false);

            RaidScreenFactory.CreateLabel(
                panel,
                "在一块不大的安全屋里，你可以整理仓库、试枪、从出口选地图出击。\n"
                + "操作说明写在安全屋的墙上；出击前的准备也都在那里完成。",
                new Vector2(48f, 300f), new Vector2(700f, 80f),
                17, TextAnchor.UpperLeft, BodyColor);

            RaidScreenFactory.CreateLabel(
                panel,
                "单人 · Windows · 一局 8 分钟　｜　Esc 退出游戏",
                new Vector2(48f, 500f), new Vector2(700f, 26f),
                15, TextAnchor.MiddleLeft, HintColor);

            SetVisible(false);
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
            var overContinue = Mouse.current != null && m_ContinueButton.Contains(pointer);
            var overNewGame = m_HasSave
                && Mouse.current != null
                && m_NewGameButton.Contains(pointer);
            m_ContinueButton.SetHovered(overContinue);
            m_NewGameButton.SetHovered(overNewGame);

            var clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
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

            if ((clicked && overContinue) || confirmed)
            {
                ResetNewGameConfirm();
                m_OnContinue.Invoke();
                return;
            }

            if (clicked && overNewGame)
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
