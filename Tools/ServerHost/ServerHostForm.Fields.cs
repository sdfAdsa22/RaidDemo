using System.Windows.Forms;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 面板上"一个配置项"的控件工厂（标签 + 输入框 + 悬停提示）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一个文件：</b>配置项的控件与"窗体有哪些区域"是两件事：
    /// 前者随配置字段增减（11 个字段各一行代码就够了），后者是窗口骨架。
    /// 混在一起会让布局文件顶过工程规范的单文件 400 行上限（规范 5.2），
    /// 也让"加一个配置项要改哪里"变得不明确。</para>
    ///
    /// <para><b>取值范围放在提示里而不是标签里：</b>全塞进标签会让
    /// "自愈看门狗（0 或 1~30 秒）"这类文字折成两行，整个配置区高低不齐；
    /// 而玩家真正需要范围的时候，通常正是鼠标停在那一格的时候。</para>
    /// </remarks>
    internal sealed partial class ServerHostForm
    {
        /// <summary>加一行"标签 + 输入框"（指定列起点，用于两列并排）。</summary>
        private TextBox AddField(TableLayoutPanel table, int row, int column, FieldHint field)
        {
            AddCaption(table, row, column, field);

            var box = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Width = InputColumnWidth,
            };

            table.Controls.Add(box, column + 1, row);
            m_Hints.SetToolTip(box, field.Hint);
            return box;
        }

        /// <summary>加"日志等级"下拉框。</summary>
        private ComboBox AddLogLevelField(TableLayoutPanel table, int row, int column, FieldHint field)
        {
            AddCaption(table, row, column, field);

            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Width = InputColumnWidth,
            };
            box.Items.AddRange(new object[] { "verbose", "info", "warning", "error" });

            table.Controls.Add(box, column + 1, row);
            m_Hints.SetToolTip(box, field.Hint);
            return box;
        }

        /// <summary>加字段标签（提示同时挂到标签上，鼠标停在文字上也能看到）。</summary>
        private void AddCaption(TableLayoutPanel table, int row, int column, FieldHint field)
        {
            var caption = new Label
            {
                Text = field.Label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, Gap, Gap, 0),
            };

            table.Controls.Add(caption, column, row);
            m_Hints.SetToolTip(caption, field.Hint);
        }

        /// <summary>统一按钮样式。</summary>
        private static Button CreateButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, Gap, 0),
                Padding = new Padding(Gap, 2, Gap, 2),
            };
        }
    }
}
