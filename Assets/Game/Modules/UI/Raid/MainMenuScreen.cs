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

        /// <summary>操作说明文本。</summary>
        /// <remarks>
        /// 写在代码里而不是做成可配置文本：它与键位绑定是同源的，
        /// 拆成两份迟早会出现「说明写着 R 装弹、实际改了键」的不一致。
        /// </remarks>
        private const string ControlsText =
            "WASD          移动\n" +
            "Shift           奔跑（消耗体力）\n" +
            "鼠标左键     射击\n" +
            "R                装弹（只从弹药挂取弹）\n" +
            "滚轮 / 1 2    切换武器\n" +
            "E                搜刮容器（读条 2 秒）\n" +
            "Tab             背包（双击快速搬运）\n" +
            "F1 / F2         开发者可视化与数据面板\n" +
            "Enter           出击";

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
                panel, ControlsText, new Vector2(400f, 162f), new Vector2(360f, 300f),
                17, TextAnchor.UpperLeft, BodyColor);

            RaidScreenFactory.CreateLabel(
                panel,
                "目标：搜刮物资，然后活着从绿色撤离点离开。贪得越多，风险越大。",
                new Vector2(48f, 272f), new Vector2(320f, 160f),
                18, TextAnchor.UpperLeft, BodyColor);

            RaidScreenFactory.CreateLabel(
                panel,
                "出击前按 Tab 打开仓库：把装备与弹药拖到身上，再按 Enter 出发。\n"
                + "阵亡会连身上带的一起丢，所以「带什么出门」就是这一局的赌注。",
                new Vector2(48f, 420f), new Vector2(700f, 60f),
                16, TextAnchor.UpperLeft, HintColor);

            RaidScreenFactory.CreateLabel(
                panel,
                "单人 · Windows · 一局 8 分钟",
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

            if (clicked || confirmed)
            {
                m_OnDeploy.Invoke();
            }
        }
    }
}
