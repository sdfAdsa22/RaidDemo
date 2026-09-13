using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 安全屋的界面：金币条、交互提示、短提示，以及出口的地图选择。
    /// </summary>
    /// <remarks>
    /// <para>安全屋没有战局 HUD（没有生命、弹药、倒计时），因此它需要自己的轻量界面。
    /// 三样东西合成一个组件：它们都属于「安全屋说了什么」，而分开三个文件只会让装配更碎。</para>
    ///
    /// <para>地图选择用键盘数字键而不是可点击按钮：出口是一个**走近再按 E** 的地方，
    /// 此时玩家的手已经在键盘上了。1 个可用 + 2 个上锁，让「选择」这件事在演示里看得见。</para>
    ///
    /// <para><b>M7 批次 4 换皮：</b>从旧 <c>Canvas</c> + 旧版 <c>Text</c> 迁到
    /// <c>UiFactory</c> + TextMeshPro。压在世界上的元素用半透明深色底板（HudPlate）保证白字可读，
    /// 面板类元素用奶油纸面——两者都是主题层里的既有配方，这里不再自造颜色。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SafeHouseUI : MonoBehaviour
    {
        /// <summary>地图选择面板尺寸（参考像素）。</summary>
        private static readonly Vector2 MapPanelSize = new Vector2(660f, 420f);

        /// <summary>提示条自动消失的时间（秒）。</summary>
        private const float HintDuration = 2.5f;

        private Action m_OnDeploy;
        private RectTransform m_PromptRoot;
        private TextMeshProUGUI m_PromptLabel;
        private GameObject m_HintRoot;
        private TextMeshProUGUI m_HintLabel;
        private TextMeshProUGUI m_MoneyLabel;
        private GameObject m_MapScreen;
        private float m_HintRemaining;
        private bool m_AuxiliaryPanelOpen;

        /// <summary>
        /// 是否有任何界面正在占据安全屋。
        /// </summary>
        /// <remarks>
        /// <para>装配层（<c>SafeHouseBootstrap</c>）用它决定"这一帧要不要屏蔽移动与射击"，
        /// 因此它必须涵盖所有会挡住世界的界面，而不只是地图面板。</para>
        /// <para>墙上说明板的放大页由 <see cref="SafeHouseSignBoard"/> 通过
        /// <see cref="SetAuxiliaryPanelOpen"/> 汇报，二者不互相持有引用：
        /// 说明板只需要说"我开了 / 我关了"，这里也只关心"还有没有面板在占屏幕"。</para>
        /// </remarks>
        public bool IsOpen
        {
            get { return m_AuxiliaryPanelOpen || (m_MapScreen != null && m_MapScreen.activeSelf); }
        }

        /// <summary>构建界面。</summary>
        /// <param name="onDeploy">在地图面板上按 1 出击时执行的回调。</param>
        public void Initialize(Action onDeploy)
        {
            m_OnDeploy = onDeploy;

            var canvas = UiFactory.CreateCanvas(transform, "SafeHouseCanvas", 150);

            BuildMoneyChip(canvas);
            BuildPrompt(canvas);
            BuildHint(canvas);
            BuildMapPanel(canvas);
        }

        /// <summary>
        /// 汇报「外部模态面板」的开合状态。
        /// </summary>
        /// <param name="open">面板是否打开。</param>
        /// <remarks>
        /// 说明板的放大页属于安全屋界面，但它自己负责键盘与排版。
        /// 让它把状态汇报到这里，是为了让**装配层的 UI 屏蔽判断**保持只有一个入口——
        /// 否则玩家在读说明时还能走动开枪，Esc 也会被暂停菜单抢走。
        /// </remarks>
        public void SetAuxiliaryPanelOpen(bool open)
        {
            m_AuxiliaryPanelOpen = open;
        }

        /// <summary>刷新右上角金币显示。</summary>
        public void SetMoney(int money)
        {
            if (m_MoneyLabel != null)
            {
                m_MoneyLabel.text = $"金币 {money:N0}";
            }
        }

        /// <summary>设置交互提示；传 null 表示隐藏。</summary>
        public void SetPrompt(string prompt)
        {
            if (m_PromptRoot == null)
            {
                return;
            }

            var has = !string.IsNullOrEmpty(prompt);
            m_PromptRoot.gameObject.SetActive(has);
            if (has)
            {
                m_PromptLabel.text = prompt;
            }
        }

        /// <summary>显示一条短提示，几秒后自动消失。</summary>
        public void ShowHint(string hint)
        {
            if (m_HintRoot == null)
            {
                return;
            }

            m_HintLabel.text = hint;
            m_HintRoot.SetActive(true);
            m_HintRemaining = HintDuration;
        }

        /// <summary>打开地图选择。</summary>
        public void ShowMap()
        {
            if (m_MapScreen != null && !m_MapScreen.activeSelf)
            {
                m_MapScreen.SetActive(true);
                UiAudio.Play(UiCue.PanelOpen);
            }
        }

        /// <summary>关闭地图选择。</summary>
        public void HideMap()
        {
            if (m_MapScreen != null && m_MapScreen.activeSelf)
            {
                m_MapScreen.SetActive(false);
                UiAudio.Play(UiCue.PanelClose);
            }
        }

        /// <summary>右上角金币条：局外系统的核心问题是「家底在变好还是变差」。</summary>
        private void BuildMoneyChip(RectTransform canvas)
        {
            var chip = UiFactory.CreateAnchored(
                canvas, "MoneyChip", UiSprites.Chip,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(300f, 50f));

            m_MoneyLabel = UiFactory.CreateLabel(
                chip,
                "金币 0",
                new Vector2(12f, 0f),
                new Vector2(276f, 50f),
                22f,
                TextAlignmentOptions.Center,
                UiPalette.Money);
        }

        /// <summary>屏幕下方的交互提示：一条深色底板 + 白字，保证压在草地上也读得清。</summary>
        private void BuildPrompt(RectTransform canvas)
        {
            var plate = UiFactory.CreateAnchored(
                canvas, "PromptPlate", UiSprites.Plate,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 168f), new Vector2(860f, 52f));
            m_PromptRoot = plate;

            m_PromptLabel = UiFactory.CreateLabel(
                plate,
                string.Empty,
                new Vector2(12f, 0f),
                new Vector2(836f, 52f),
                20f,
                TextAlignmentOptions.Center,
                UiPalette.Paper);

            m_PromptRoot.gameObject.SetActive(false);
        }

        /// <summary>短提示（"这张地图还没有开放"）：白底小胶囊，压在交互提示上方。</summary>
        private void BuildHint(RectTransform canvas)
        {
            var chip = UiFactory.CreateAnchored(
                canvas, "HintChip", UiSprites.Chip,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 232f), new Vector2(560f, 46f));

            m_HintLabel = UiFactory.CreateLabel(
                chip,
                string.Empty,
                new Vector2(10f, 0f),
                new Vector2(540f, 46f),
                18f,
                TextAlignmentOptions.Center,
                UiPalette.Ink);

            m_HintRoot = chip.gameObject;
            m_HintRoot.SetActive(false);
        }

        /// <summary>地图选择面板：1 个可用 + 2 个上锁。</summary>
        private void BuildMapPanel(RectTransform canvas)
        {
            // 遮罩与面板同一个根一起显隐：遮罩单独挂在画布上的话，
            // 关掉面板后世界会一直暗着（这批已经踩过一次，见排障记录）。
            var screen = UiFactory.CreateRect(canvas, "MapScreen");
            UiFactory.Stretch(screen);
            UiFactory.CreateVeil(screen, "Veil");

            var panel = UiFactory.CreateCenteredPanel(screen, "MapPanel", MapPanelSize, UiSprites.Card);
            var width = MapPanelSize.x - 56f;

            UiFactory.CreatePanel(
                panel, "TitleBar", new Vector2(MapPanelSize.x, 72f), UiSprites.CardDim, Vector2.zero);
            UiFactory.CreateLabel(
                panel, "选择出击地图", new Vector2(28f, 18f), new Vector2(360f, 36f),
                26f, TextAlignmentOptions.Left, UiPalette.Ink);
            UiFactory.CreateLabel(
                panel, "1 可用 · 2 上锁", new Vector2(MapPanelSize.x - 328f, 26f), new Vector2(300f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft);

            BuildMapRow(panel, 0, "1", "工业区与集装箱仓库", "可用 · 已解锁", true, 92f, width);
            BuildMapRow(panel, 1, "2", "港口", "未开放", false, 170f, width);
            BuildMapRow(panel, 2, "3", "农场", "未开放", false, 248f, width);

            UiFactory.CreateLabel(
                panel, "按 1 出击　·　按 Esc 返回",
                new Vector2(28f, 344f), new Vector2(width, 30f),
                UiPalette.BodySize, TextAlignmentOptions.Center, UiPalette.InkSoft);

            m_MapScreen = screen.gameObject;
            m_MapScreen.SetActive(false);
        }

        /// <summary>地图列表的一行：按键胶囊 + 名称 + 状态（可用的那行右侧给出出击提示）。</summary>
        private static void BuildMapRow(
            RectTransform panel,
            int index,
            string key,
            string name,
            string state,
            bool unlocked,
            float top,
            float width)
        {
            var row = UiFactory.CreatePanel(
                panel, $"MapRow{index}", new Vector2(width, 66f), UiSprites.CardDim, new Vector2(28f, top));

            var chip = UiFactory.CreateAnchored(
                row, "KeyChip", UiSprites.Chip,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(46f, 46f));
            UiFactory.CreateLabel(
                chip, key, Vector2.zero, new Vector2(46f, 46f),
                22f, TextAlignmentOptions.Center, unlocked ? UiPalette.Ink : UiPalette.InkDisabled);

            UiFactory.CreateLabel(
                row, name, new Vector2(72f, 12f), new Vector2(320f, 24f),
                20f, TextAlignmentOptions.Left, unlocked ? UiPalette.Ink : UiPalette.InkDisabled);
            UiFactory.CreateLabel(
                row, state, new Vector2(72f, 36f), new Vector2(320f, 20f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, unlocked ? UiPalette.Ok : UiPalette.InkDisabled);

            if (unlocked)
            {
                UiFactory.CreateLabel(
                    row, "按 1 出击", new Vector2(width - 228f, 22f), new Vector2(200f, 24f),
                    17f, TextAlignmentOptions.Right, UiPalette.Teal);
            }
        }

        /// <summary>处理地图选择与提示计时。</summary>
        private void Update()
        {
            if (m_HintRemaining > 0f)
            {
                m_HintRemaining -= Time.unscaledDeltaTime;
                if (m_HintRemaining <= 0f && m_HintRoot != null)
                {
                    m_HintRoot.SetActive(false);
                }
            }

            // 说明板的放大页打开时，这里的数字键与 Esc 全部让路：
            // 玩家此刻在读键位说明，按 1 不应该直接把他送进战局。
            if (m_AuxiliaryPanelOpen || !IsOpen)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                UiAudio.Play(UiCue.Cancel);
                HideMap();
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                UiAudio.Play(UiCue.Confirm);
                HideMap();
                m_OnDeploy?.Invoke();
                return;
            }

            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.digit3Key.wasPressedThisFrame)
            {
                UiAudio.Play(UiCue.Locked);
                ShowHint("这张地图还没有开放");
            }
        }
    }
}
