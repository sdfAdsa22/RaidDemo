using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 极简主菜单：一个出击按钮，加上一页操作说明。
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
        private RaidButtonWidget m_DeployButton;
        private Action m_OnDeploy;
        private bool m_IsVisible;

        /// <summary>构建界面。</summary>
        /// <param name="onDeploy">点击「出击」时执行的回调。</param>
        public void Initialize(Action onDeploy)
        {
            m_OnDeploy = onDeploy;

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

            m_DeployButton = RaidScreenFactory.CreateButton(
                panel,
                "出击（Enter）",
                new Vector2(48f, 170f),
                new Vector2(300f, 68f),
                ButtonColor,
                ButtonHoverColor);

            RaidScreenFactory.CreateLabel(
                panel,
                "在一块不大的安全屋里，你可以整理仓库、试枪、从出口选地图出击。\n"
                + "操作说明写在安全屋的墙上；出击前的准备也都在那里完成。",
                new Vector2(48f, 272f), new Vector2(700f, 80f),
                17, TextAnchor.UpperLeft, BodyColor);

            RaidScreenFactory.CreateLabel(
                panel,
                "单人 · Windows · 一局 8 分钟　｜　Esc 退出游戏",
                new Vector2(48f, 500f), new Vector2(700f, 26f),
                15, TextAnchor.MiddleLeft, HintColor);

            SetVisible(false);
        }

        /// <summary>显示或隐藏菜单。</summary>
        public void SetVisible(bool visible)
        {
            m_IsVisible = visible;
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
            if (!m_IsVisible || m_OnDeploy == null)
            {
                return;
            }

            var keyboard = Keyboard.current;
            var pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            var hovered = Mouse.current != null && m_DeployButton.Contains(pointer);
            m_DeployButton.SetHovered(hovered);

            var clicked = hovered && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            var confirmed = keyboard != null && keyboard.enterKey.wasPressedThisFrame;

            // Esc 退出游戏。编辑器里退出播放模式，构建里真正退出——
            // 否则在编辑器里点「退出」会毫无反应，看起来像坏了。
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }

            if (clicked || confirmed)
            {
                m_OnDeploy.Invoke();
            }
        }
    }
}
