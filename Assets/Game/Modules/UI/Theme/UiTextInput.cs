using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;


namespace RaidDemo.UI
{
    /// <summary>
    /// 自绘输入框：一个方板 + 一行文字 + 一根竖线光标，由所属界面每帧轮询鼠标与键盘。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用 TMP_InputField：</b>它依赖 EventSystem 与 uGUI 事件通路，
    /// 而整个项目的点击都是自己轮询的（<see cref="UiButton"/>）。为四个输入框引入第二套
    /// 输入通路，最容易产生"点进去打不了字"这类只在实机暴露的问题。</para>
    ///
    /// <para><b>光标为什么画成文字：</b>没有事件系统也就没有原生光标；把 <c>|</c> 插进字符串
    /// 再交给同一个文本组件渲染，代价为零且一定与文字对齐。</para>
    /// </remarks>
    internal sealed class UiTextInput
    {
        /// <summary>文字距边框的内边距。</summary>
        private const float TextPadding = 12f;

        private readonly Image m_Background;
        private readonly TextMeshProUGUI m_Text;
        private string m_Placeholder;
        private readonly bool m_Masked;
        private bool m_Focused;
        private bool m_Hovered;

        private UiTextInput(
            RectTransform rect,
            Image background,
            TextMeshProUGUI text,
            string placeholder,
            bool masked,
            int maxLength,
            string initialValue)
        {
            Rect = rect;
            m_Background = background;
            m_Text = text;
            m_Placeholder = placeholder ?? string.Empty;
            m_Masked = masked;
            Model = new UiTextEditModel(initialValue, maxLength);
            Refresh();
        }

        /// <summary>输入框矩形。</summary>
        public RectTransform Rect { get; }

        /// <summary>编辑模型。</summary>
        public UiTextEditModel Model { get; }

        /// <summary>是否可交互；不可交互时点击无效且文字变灰。</summary>
        public bool Interactable { get; set; } = true;

        /// <summary>当前是否持有输入焦点。</summary>
        public bool IsFocused => m_Focused;

        /// <summary>在父节点上创建一个输入框。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">节点名。</param>
        /// <param name="topLeft">相对父节点的左上偏移。</param>
        /// <param name="size">尺寸。</param>
        /// <param name="placeholder">空文本且未聚焦时显示的提示。</param>
        /// <param name="masked">是否用圆点遮罩（口令、房间密码）。</param>
        /// <param name="maxLength">长度上限。</param>
        /// <param name="initialValue">初始文本。</param>
        public static UiTextInput Create(
            RectTransform parent,
            string name,
            Vector2 topLeft,
            Vector2 size,
            string placeholder,
            bool masked,
            int maxLength,
            string initialValue = null)
        {
            var rect = UiFactory.CreatePanel(parent, name, size, UiSprites.Cell, topLeft);
            var background = rect.GetComponent<Image>();

            var text = UiFactory.CreateLabel(
                rect,
                string.Empty,
                new Vector2(TextPadding, 0f),
                new Vector2(size.x - (TextPadding * 2f), size.y),
                UiPalette.BodySize,
                TextAlignmentOptions.Left,
                UiPalette.Ink);

            return new UiTextInput(rect, background, text, placeholder, masked, maxLength, initialValue);
        }

        /// <summary>鼠标位置是否落在输入框上。</summary>
        /// <param name="screenPoint">屏幕坐标。</param>
        public bool Contains(Vector2 screenPoint)
        {
            return Interactable && RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, null);
        }

        /// <summary>设置焦点（不可交互时无法获得焦点）。</summary>
        /// <param name="focused">是否聚焦。</param>
        public void SetFocus(bool focused)
        {
            var next = focused && Interactable;
            if (next == m_Focused)
            {
                return;
            }

            m_Focused = next;
            Refresh();
        }

        /// <summary>刷新悬停外观。</summary>
        /// <param name="hovered">鼠标是否在框上。</param>
        public void ApplyVisual(bool hovered)
        {
            m_Hovered = hovered && Interactable;
            ApplyColors();
        }

        /// <summary>把系统文本输入事件写进模型（仅在聚焦时）。</summary>
        /// <param name="c">字符。</param>
        /// <returns>是否发生了变化。</returns>
        public bool ApplyTextInput(char c)
        {
            if (!m_Focused || !Model.ApplyCharacter(c))
            {
                return false;
            }

            Refresh();
            return true;
        }

        /// <summary>
        /// 处理退格、Delete、方向键、Home/End。
        /// </summary>
        /// <param name="keyboard">当前键盘设备（可为 null）。</param>
        /// <returns>是否持有焦点（持有即表示这些按键不该再被界面其它逻辑使用）。</returns>
        public bool HandleSpecialKeys(Keyboard keyboard)
        {
            if (!m_Focused || keyboard == null)
            {
                return false;
            }

            var changed = false;
            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                changed |= Model.Backspace();
            }

            if (keyboard.deleteKey.wasPressedThisFrame)
            {
                changed |= Model.DeleteForward();
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                changed |= Model.MoveCaret(-1);
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                changed |= Model.MoveCaret(1);
            }

            if (keyboard.homeKey.wasPressedThisFrame)
            {
                changed |= Model.MoveCaretTo(0);
            }

            if (keyboard.endKey.wasPressedThisFrame)
            {
                changed |= Model.MoveCaretTo(Model.Value.Length);
            }

            if (changed)
            {
                Refresh();
            }

            return true;
        }

        /// <summary>整体替换文本。</summary>
        /// <param name="value">新文本。</param>
        public void SetValue(string value)
        {
            Model.SetValue(value);
            Refresh();
        }

        /// <summary>
        /// 替换占位提示（值为空且未聚焦时显示）。
        /// </summary>
        /// <remarks>
        /// <b>占位符不经过字符白名单：</b>输入值只收 ASCII（见 <see cref="UiTextEditModel.IsAllowedCharacter"/>），
        /// 而"已隐藏"这类中文提示只能放进占位符——写进值里会被过滤成空字符串（U-99 的实机问题）。
        /// </remarks>
        public void SetPlaceholder(string placeholder)
        {
            m_Placeholder = placeholder ?? string.Empty;
            Refresh();
        }

        /// <summary>重画文本、光标与底色。</summary>
        public void Refresh()
        {
            var shown = m_Masked ? new string('●', Model.Value.Length) : Model.Value;

            if (shown.Length == 0 && !m_Focused)
            {
                m_Text.text = m_Placeholder;
                m_Text.color = UiPalette.InkDisabled;
            }
            else
            {
                if (m_Focused)
                {
                    shown = shown.Insert(Mathf.Clamp(Model.CaretIndex, 0, shown.Length), "|");
                }

                m_Text.text = shown;
                m_Text.color = Interactable ? UiPalette.Ink : UiPalette.InkDisabled;
            }

            ApplyColors();
        }

        /// <summary>按 焦点 / 悬停 / 普通 / 禁用 四种状态上色。</summary>
        private void ApplyColors()
        {
            if (!Interactable)
            {
                m_Background.color = new Color(0.86f, 0.84f, 0.80f);
                return;
            }

            if (m_Focused)
            {
                m_Background.color = new Color(0.88f, 0.97f, 0.95f);
                return;
            }

            m_Background.color = m_Hovered ? UiPalette.ButtonHover : Color.white;
        }
    }
}
