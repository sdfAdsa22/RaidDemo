using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;


namespace RaidDemo.UI
{
    /// <summary>
    /// 文本编辑模型：光标、插入、删除与长度上限，全部是纯逻辑，不碰任何 Unity 组件。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把编辑规则与画面拆开：</b>「退格删哪个字符」「到上限还收不收字符」
    /// 「光标能不能越过两端」都是规则，规则可以在 EditMode 测试里一条条钉住；
    /// 而画框、画光标那部分只能靠肉眼。拆开之后，输入行为的改动不再需要启动游戏验证。</para>
    ///
    /// <para><b>只接受可打印 ASCII：</b>工程没有 EventSystem，也就没有 uGUI 输入框与输入法通路
    /// （见 <see cref="UiButton"/> 的注释）。中文昵称走启动参数 <c>-nickname</c>；
    /// 为输入法引入整套事件系统，代价远大于收益。这是本批的已知边界。</para>
    /// </remarks>
    public sealed class UiTextEditModel
    {
        /// <summary>长度硬上限：界面参数写错时也不会变成"无限长"。</summary>
        private const int HardMaxLength = 64;

        private string m_Value = string.Empty;
        private int m_Caret;

        /// <summary>建立一个编辑模型。</summary>
        /// <param name="initialValue">初始文本；其中的非法字符会被丢掉。</param>
        /// <param name="maxLength">长度上限（至少 1，至多 64）。</param>
        public UiTextEditModel(string initialValue, int maxLength)
        {
            SetMaxLength(maxLength);
            SetValue(initialValue);
        }

        /// <summary>长度上限。</summary>
        public int MaxLength { get; private set; } = 1;

        /// <summary>当前文本。</summary>
        public string Value => m_Value;

        /// <summary>光标位置（0 表示在最前，等于文本长度表示在最后）。</summary>
        public int CaretIndex => m_Caret;

        /// <summary>是否为空文本。</summary>
        public bool IsEmpty => m_Value.Length == 0;

        /// <summary>调整长度上限；文本与光标会被一并收敛到新上限内。</summary>
        /// <param name="maxLength">新的长度上限。</param>
        public void SetMaxLength(int maxLength)
        {
            MaxLength = Mathf.Clamp(maxLength, 1, HardMaxLength);
            if (m_Value.Length <= MaxLength)
            {
                return;
            }

            m_Value = m_Value.Substring(0, MaxLength);
            m_Caret = Mathf.Min(m_Caret, m_Value.Length);
        }

        /// <summary>该字符是否允许进入输入框。</summary>
        /// <param name="c">待判定的字符。</param>
        /// <remarks>允许空格与 <c>- _ .</c>：昵称、房间名与 IP 地址都要用到它们。</remarks>
        public static bool IsAllowedCharacter(char c)
        {
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }

            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }

            if (c >= '0' && c <= '9')
            {
                return true;
            }

            return c == ' ' || c == '-' || c == '_' || c == '.';
        }

        /// <summary>整体替换文本（非法字符被过滤、超长被截断），光标移到末尾。</summary>
        /// <param name="value">新文本。</param>
        /// <returns>文本是否发生了变化。</returns>
        public bool SetValue(string value)
        {
            var filtered = Filter(value);
            if (filtered.Length > MaxLength)
            {
                filtered = filtered.Substring(0, MaxLength);
            }

            var changed = !string.Equals(filtered, m_Value, System.StringComparison.Ordinal);
            m_Value = filtered;
            m_Caret = m_Value.Length;
            return changed;
        }

        /// <summary>在光标处插入一个字符。</summary>
        /// <param name="c">字符。</param>
        /// <returns>是否插入成功（非法字符或已达上限时为 false）。</returns>
        public bool ApplyCharacter(char c)
        {
            if (!IsAllowedCharacter(c) || m_Value.Length >= MaxLength)
            {
                return false;
            }

            m_Value = m_Value.Insert(m_Caret, c.ToString());
            m_Caret++;
            return true;
        }

        /// <summary>删除光标前一个字符（退格）。</summary>
        /// <returns>是否发生了变化。</returns>
        public bool Backspace()
        {
            if (m_Caret <= 0)
            {
                return false;
            }

            m_Value = m_Value.Remove(m_Caret - 1, 1);
            m_Caret--;
            return true;
        }

        /// <summary>删除光标处字符（Delete）。</summary>
        /// <returns>是否发生了变化。</returns>
        public bool DeleteForward()
        {
            if (m_Caret >= m_Value.Length)
            {
                return false;
            }

            m_Value = m_Value.Remove(m_Caret, 1);
            return true;
        }

        /// <summary>左右移动光标。</summary>
        /// <param name="delta">位移（正数向右）。</param>
        /// <returns>是否移动了位置。</returns>
        public bool MoveCaret(int delta)
        {
            return MoveCaretTo(m_Caret + delta);
        }

        /// <summary>把光标移到指定位置（会被夹到 [0, 长度]）。</summary>
        /// <param name="index">目标位置。</param>
        /// <returns>是否移动了位置。</returns>
        public bool MoveCaretTo(int index)
        {
            var clamped = Mathf.Clamp(index, 0, m_Value.Length);
            if (clamped == m_Caret)
            {
                return false;
            }

            m_Caret = clamped;
            return true;
        }

        /// <summary>过滤掉所有非法字符。</summary>
        private static string Filter(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (IsAllowedCharacter(value[i]))
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }
    }
}
