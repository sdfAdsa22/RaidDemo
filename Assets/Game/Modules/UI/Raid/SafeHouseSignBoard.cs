using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 安全屋北墙上的操作说明牌：走近看键位，按 E 放大成一页说明。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么说明要挂在墙上，而不是只在菜单里：</b>这个 Demo 会被不认识它的人打开。
    /// 键位写在墙上是"空间里的一件东西"——玩家走过去、读到它、再出门，
    /// 比在主菜单里读一段会被跳过的小字更可能被真正看到。主菜单里那几行字保留作为兜底，
    /// 但完整的键位表只有这里一份。</para>
    ///
    /// <para><b>为什么还有一页放大版：</b>牌面在 9×3 米的墙上，靠远处看得清字看不清细节；
    /// 走近按 E 之后用屏幕空间重排成两栏（移动与战斗 / 局外流程与风险），
    /// 字号大、行距宽，站着读一遍就能出门。</para>
    ///
    /// <para><b>它不自己屏蔽移动：</b>放大页打开时通过 <see cref="SafeHouseUI.SetAuxiliaryPanelOpen"/>
    /// 把状态汇报给安全屋界面，装配层据此决定"这一帧不采移动与射击输入"。
    /// 这样解释权仍然只有一处——界面不需要知道谁在管输入，装配层也不需要认识说明牌。</para>
    ///
    /// <para>本组件只依赖 <c>RaidDemo.UI</c> 与 Unity 基础库：不引用 Presentation / Bootstrap，
    /// 因此它既能挂在生成器造出来的安全屋里，也不会把界面层的依赖方向弄反。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SafeHouseSignBoard : MonoBehaviour
    {
        /// <summary>
        /// 牌面与放大页共用的键位说明。
        /// </summary>
        /// <remarks>内容只有这一份：场景生成器建牌面文字时也读它，
        /// 因此改键位不会出现"墙上写了、放大页没写"的不一致。</remarks>
        public const string Instructions =
            "安全屋 · 出击准备\n\n"
            + "WASD 移动    鼠标 瞄准    左键 射击\n"
            + "R 装弹    滚轮 / 1 2 换武器    Tab 背包    E 交互\n"
            + "H 使用医疗品    右键菜单\n\n"
            + "走到仓库前按 E 整理装备，走到出口前按 E 选择地图出击。\n"
            + "阵亡会丢掉随身携带的一切，仓库里的东西永远安全。\n\n"
            + "【按 E 放大这页说明】";

        /// <summary>放大页的面板尺寸（参考像素）。</summary>
        private static readonly Vector2 PanelSize = new Vector2(1180f, 700f);

        /// <summary>内容区边距、标题条高度与行高。</summary>
        private const float Padding = 44f;

        private const float TitleBarHeight = 84f;

        private const float LineHeight = 46f;

        /// <summary>右栏相对左栏的横向偏移。</summary>
        private const float RightColumnX = 572f;

        /// <summary>左栏：移动与战斗的键位。</summary>
        private static readonly string[] s_MoveKeys =
        {
            "WASD", "Shift", "鼠标", "左键", "R", "滚轮 / 1 2", "H", "Tab", "右键",
        };

        private static readonly string[] s_MoveTexts =
        {
            "移动", "奔跑", "瞄准", "射击", "装弹", "切换武器", "使用医疗品", "背包与仓库", "物品菜单",
        };

        /// <summary>右栏：局外流程与风险。</summary>
        private static readonly string[] s_FlowKeys =
        {
            "E", "仓库", "商人", "出口", "Esc", "阵亡", "仓库",
        };

        private static readonly string[] s_FlowTexts =
        {
            "与设施交互",
            "停在仓库前按 E 整理装备",
            "停在商人前按 E 买卖与接任务",
            "停在出口前按 E 选择地图出击",
            "暂停 / 关闭当前面板",
            "随身携带的一切都会丢失",
            "仓库里的物品永远安全",
        };

        /// <summary>玩家根节点。由场景生成器写入；为空时退回主相机。</summary>
        [SerializeField] private Transform m_Player;

        /// <summary>触发提示与放大的水平距离（米）。</summary>
        [SerializeField] private float m_InteractRange = 3f;

        /// <summary>牌面上的世界空间文字。由场景生成器写入；为空时本组件自己建一块。</summary>
        [SerializeField] private TextMeshProUGUI m_WallText;

        private RectTransform m_HintChip;
        private GameObject m_PanelScreen;
        private SafeHouseUI m_SafeHouseUi;
        private bool m_IsOpen;
        private bool m_WarnedMissingPlayer;

        /// <summary>放大页是否正在显示。</summary>
        public bool IsOpen
        {
            get { return m_IsOpen; }
        }

        private void Awake()
        {
            EnsureWallText();
            BuildOverlay();
        }

        /// <summary>
        /// 组件被禁用或随场景卸载时，不要把"界面还开着"的状态留给安全屋界面。
        /// </summary>
        /// <remarks>这里刻意不去关面板对象：对象可能正在被销毁，只改状态最安全。</remarks>
        private void OnDisable()
        {
            if (!m_IsOpen)
            {
                return;
            }

            m_IsOpen = false;
            m_SafeHouseUi?.SetAuxiliaryPanelOpen(false);
        }

        private void Update()
        {
            if (m_IsOpen)
            {
                UpdateHint(false);
                var close = Keyboard.current;
                if (close != null && (close.escapeKey.wasPressedThisFrame || close.eKey.wasPressedThisFrame))
                {
                    ClosePanel();
                }

                return;
            }

            var canInteract = CanInteract();
            UpdateHint(canInteract && IsPlayerNear());

            if (!canInteract || !IsPlayerNear())
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
            {
                OpenPanel();
            }
        }

        /// <summary>
        /// 现在能不能与说明牌交互。
        /// </summary>
        /// <remarks>
        /// <para>三个条件缺一不可：游戏没有暂停（暂停时 <c>Time.timeScale</c> 为 0）、
        /// 光标处于锁定状态（背包 / 商人 / 地图这些面板打开时装配层会解锁光标）、
        /// 以及安全屋界面自己没有别的面板在占屏幕。</para>
        /// <para>用"光标是否锁定"而不是去引用背包与商人的具体类型，是因为界面层的依赖方向不允许
        /// 它认识那些类；而光标状态正是装配层已经维护好的"玩家是否处在可操作世界"的信号。</para>
        /// </remarks>
        private bool CanInteract()
        {
            if (Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked)
            {
                return false;
            }

            var ui = ResolveSafeHouseUi();
            return ui == null || !ui.IsOpen;
        }

        /// <summary>玩家是否站在牌子前方。</summary>
        /// <remarks>只比水平距离：牌子挂在 2.2 米高的墙上，玩家根节点在脚底，
        /// 带上高度差会让"站在牌子正下方"反而判定为更远。</remarks>
        private bool IsPlayerNear()
        {
            var source = m_Player != null
                ? m_Player
                : Camera.main != null
                    ? Camera.main.transform
                    : null;

            if (source == null)
            {
                if (!m_WarnedMissingPlayer)
                {
                    m_WarnedMissingPlayer = true;
                    Debug.LogWarning("[RaidDemo] 说明牌没有玩家引用，靠近提示不会出现。" +
                                     "请重新执行菜单 RaidDemo/生成安全屋场景，或在检视面板里指定 m_Player。");
                }

                return false;
            }

            var delta = source.position - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= m_InteractRange * m_InteractRange;
        }

        private SafeHouseUI ResolveSafeHouseUi()
        {
            if (m_SafeHouseUi == null)
            {
                m_SafeHouseUi = FindFirstObjectByType<SafeHouseUI>();
            }

            return m_SafeHouseUi;
        }

        private void OpenPanel()
        {
            m_IsOpen = true;
            if (m_PanelScreen != null)
            {
                m_PanelScreen.SetActive(true);
            }

            ResolveSafeHouseUi()?.SetAuxiliaryPanelOpen(true);
            UiAudio.Play(UiCue.PanelOpen);
        }

        private void ClosePanel()
        {
            m_IsOpen = false;
            if (m_PanelScreen != null)
            {
                m_PanelScreen.SetActive(false);
            }

            ResolveSafeHouseUi()?.SetAuxiliaryPanelOpen(false);
            UiAudio.Play(UiCue.PanelClose);
        }

        private void UpdateHint(bool visible)
        {
            if (m_HintChip != null && m_HintChip.gameObject.activeSelf != visible)
            {
                m_HintChip.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 牌面的世界空间文字：正常情况下由场景生成器建好并写入引用。
        /// </summary>
        /// <remarks>
        /// 兜底分支是为了"有人手搓安全屋场景、只挂了组件"的情况——这时文字仍然会出现，
        /// 而不是变成一块空牌子（一块没有字的说明牌比没有牌子更让人困惑）。
        /// </remarks>
        private void EnsureWallText()
        {
            if (m_WallText != null)
            {
                return;
            }

            var canvasHost = new GameObject("SignText", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)canvasHost.transform;
            rect.SetParent(transform, worldPositionStays: false);
            rect.sizeDelta = new Vector2(900f, 300f);
            // 等比缩小 100 倍：世界空间画布的 1 单位 = 1 米，900×300 的文本要变成 9×3 米。
            rect.localScale = Vector3.one * 0.01f;

            var textHost = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textHost.transform;
            textRect.SetParent(rect, worldPositionStays: false);
            UiFactory.Stretch(textRect);

            var text = textHost.GetComponent<TextMeshProUGUI>();
            text.text = Instructions;
            text.fontSize = 30f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = UiPalette.Paper;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            m_WallText = text;
        }

        /// <summary>靠近提示 + 放大页。</summary>
        private void BuildOverlay()
        {
            var canvas = UiFactory.CreateCanvas(transform, "SignBoardCanvas", 155);

            // 靠近提示挂在屏幕下方，但比安全屋的交互提示再高一层：
            // 两者可能同时出现（站在牌子前、附近又有个设施），错开高度就不会叠在一起。
            m_HintChip = UiFactory.CreateAnchored(
                canvas, "SignHintChip", UiSprites.Chip,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 286f), new Vector2(420f, 46f));
            UiFactory.CreateLabel(
                m_HintChip, "按 E 查看操作说明", new Vector2(10f, 0f), new Vector2(400f, 46f),
                UiPalette.BodySize, TextAlignmentOptions.Center, UiPalette.Ink);
            m_HintChip.gameObject.SetActive(false);

            var screen = UiFactory.CreateRect(canvas, "SignPanelScreen");
            UiFactory.Stretch(screen);
            UiFactory.CreateVeil(screen, "Veil");

            var panel = UiFactory.CreateCenteredPanel(screen, "SignPanel", PanelSize, UiSprites.Card);
            BuildPanelHeader(panel);
            BuildColumns(panel);

            var footer = UiFactory.CreateAnchored(
                panel, "FooterChip", UiSprites.Chip,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(360f, 48f));
            UiFactory.CreateLabel(
                footer, "按 E 或 Esc 关闭", new Vector2(10f, 0f), new Vector2(340f, 48f),
                UiPalette.BodySize, TextAlignmentOptions.Center, UiPalette.Ink);

            m_PanelScreen = screen.gameObject;
            m_PanelScreen.SetActive(false);
        }

        private static void BuildPanelHeader(RectTransform panel)
        {
            UiFactory.CreatePanel(
                panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            UiFactory.CreateLabel(
                panel, "安全屋 · 操作说明",
                new Vector2(Padding, 22f), new Vector2(640f, 42f),
                30f, TextAlignmentOptions.Left, UiPalette.Ink);

            UiFactory.CreateLabel(
                panel, "读完再出门",
                new Vector2(PanelSize.x - Padding - 320f, 30f), new Vector2(320f, 26f),
                UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft);
        }

        /// <summary>两栏键位表。</summary>
        private static void BuildColumns(RectTransform panel)
        {
            const float headerTop = TitleBarHeight + 26f;
            const float firstLineTop = headerTop + 50f;

            UiFactory.CreateLabel(
                panel, "移动与战斗", new Vector2(Padding, headerTop), new Vector2(320f, 34f),
                24f, TextAlignmentOptions.Left, UiPalette.Teal);
            UiFactory.CreateLabel(
                panel, "局外流程与风险", new Vector2(Padding + RightColumnX, headerTop), new Vector2(340f, 34f),
                24f, TextAlignmentOptions.Left, UiPalette.Teal);

            for (var i = 0; i < s_MoveKeys.Length; i++)
            {
                BuildLine(panel, Padding, firstLineTop + (i * LineHeight), s_MoveKeys[i], s_MoveTexts[i], 340f);
            }

            for (var i = 0; i < s_FlowKeys.Length; i++)
            {
                BuildLine(
                    panel, Padding + RightColumnX, firstLineTop + (i * LineHeight),
                    s_FlowKeys[i], s_FlowTexts[i], PanelSize.x - RightColumnX - (Padding * 2f) - 160f);
            }
        }

        /// <summary>一行：左格是按键、右格是说明。</summary>
        private static void BuildLine(
            RectTransform panel, float x, float top, string key, string text, float textWidth)
        {
            UiFactory.CreateLabel(
                panel, key, new Vector2(x, top), new Vector2(150f, 30f),
                UiPalette.BodySize, TextAlignmentOptions.Right, UiPalette.Ink);
            UiFactory.CreateLabel(
                panel, text, new Vector2(x + 166f, top), new Vector2(textWidth, 30f),
                UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.InkSoft);
        }
    }
}
