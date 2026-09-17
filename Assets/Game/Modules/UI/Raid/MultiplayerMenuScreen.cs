using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 扫描到的一条局域网房间（界面只读模型）。
    /// </summary>
    /// <remarks>
    /// 它由装配层从发现回包里整理好再交给界面：界面不认识网络协议，也不该认识——
    /// 换一种发现方式（手输、目录服务器）时，界面一行都不用改。
    /// </remarks>
    public struct MultiplayerMenuRoom
    {
        /// <summary>服务器地址。</summary>
        public string Address;

        /// <summary>游戏端口。</summary>
        public int Port;

        /// <summary>整行展示文本（例如「验收房间（1/4 · 等待中 · 有密码）」）。</summary>
        public string Description;

        /// <summary>当前是否可加入（等待中且未满员）。</summary>
        public bool Joinable;
    }

    /// <summary>
    /// 联机界面把玩家意图交回装配层的回调集合。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么界面只拿回调：</b>界面属于 <c>RaidDemo.UI</c>，而会话与网络在
    /// <c>RaidDemo.Bootstrap</c>；让界面直接引用后者会形成循环依赖。
    /// 回调 + 只读视图模型把"画"与"做"彻底分开，界面因此也能被单独替换。</para>
    /// </remarks>
    public struct MultiplayerMenuActions
    {
        /// <summary>连接并登录：地址、端口、昵称、口令。</summary>
        public Action<string, int, string, string> Connect;

        /// <summary>请求扫描局域网。</summary>
        public Action Scan;

        /// <summary>点击「云主机」：把启动器预填的服务器地址一键填进地址框。</summary>
        public Action SelectCloudServer;

        /// <summary>点击扫描结果：按该地址连接。</summary>
        public Action<string, int> JoinFound;

        /// <summary>返回主菜单。</summary>
        public Action Back;
    }

    /// <summary>
    /// 联机文本的格式与校验规则（界面侧）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这里也有一份限制常量：</b>服务器侧的权威规则在 <c>LobbyLimits</c>，
    /// 但界面程序集不能引用它（层次反了）。界面这份的作用只是"本地先拦一次"，
    /// 让玩家不必等一个来回才知道口令位数不对；真正的判定永远在服务器。</para>
    ///
    /// <para>两份必须保持一致：改动其中一份时，另一份要同步（<c>LobbyLimits</c> 的注释里也有这条）。</para>
    public sealed partial class MultiplayerMenuScreen : MonoBehaviour
    {
        /// <summary>面板尺寸（参考像素）。</summary>
        private static readonly Vector2 PanelSize = new Vector2(920f, 640f);

        /// <summary>内容区边距。</summary>
        private const float Padding = 36f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 84f;

        /// <summary>左栏宽度（输入框与按钮都排在这里）。</summary>
        private const float LeftColumnWidth = 470f;

        /// <summary>右栏（扫描结果）起点与宽度。</summary>
        private const float RightColumnX = 540f;
        private const float RightColumnWidth = 344f;

        /// <summary>扫描结果最多显示几行。</summary>
        private const int ScanRowCount = 6;

        /// <summary>地址框的默认占位提示（也是"本机测试"的示例地址）。</summary>
        private const string AddressPlaceholder = "127.0.0.1";

        private RectTransform m_Root;
        private TextMeshProUGUI m_StatusLabel;
        private TextMeshProUGUI m_RoomListLabel;
        private UiTextInput m_AddressInput;
        private UiTextInput m_PortInput;
        private UiTextInput m_NicknameInput;
        private UiTextInput m_PassphraseInput;
        private UiTextInput m_FocusedInput;
        private UiButton m_RandomNicknameButton;
        private UiButton m_ConnectButton;
        private UiButton m_ScanButton;
        private UiButton m_BackButton;
        private readonly UiButton[] m_RoomRows = new UiButton[ScanRowCount];
        private UiTextInput[] m_Fields;
        private MultiplayerMenuActions m_Actions;

        /// <summary>「云主机」按钮：启动器提供了预填地址时才显示。</summary>
        private UiButton m_CloudServerButton;

        /// <summary>
        /// 地址框是否处于"隐藏源"模式：此时地址允许留空，连接时用启动器预填的真实地址。
        /// </summary>
        private bool m_AddressHiddenMode;

        private IReadOnlyList<MultiplayerMenuRoom> m_Rooms = Array.Empty<MultiplayerMenuRoom>();
        private Keyboard m_SubscribedKeyboard;
        private bool m_IsVisible;

        /// <summary>界面当前是否可见（流程层据此判断"玩家是不是已经在联机页上了"）。</summary>
        public bool IsVisible => m_IsVisible;
        private bool m_IsBusy;
        private bool m_IsScanning;

        /// <summary>构建界面。</summary>
        /// <param name="actions">交给装配层的回调集合。</param>
        public void Initialize(MultiplayerMenuActions actions)
        {
            m_Actions = actions;

            m_Root = UiFactory.CreateCanvas(transform, "MultiplayerMenuCanvas", 340);
            UiFactory.CreateBackdrop(m_Root, "Backdrop", UiPalette.MenuBackdrop);

            var shadow = UiFactory.CreateCenteredPanel(m_Root, "PanelShadow", PanelSize, UiSprites.Card);
            shadow.anchoredPosition = new Vector2(0f, -12f);
            shadow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);

            var panel = UiFactory.CreateCenteredPanel(m_Root, "Panel", PanelSize, UiSprites.Card);
            BuildTitleBar(panel);
            BuildLeftColumn(panel);
            BuildRightColumn(panel);
            BuildStatusLine(panel);
            m_Fields = new[] { m_AddressInput, m_PortInput, m_NicknameInput, m_PassphraseInput };
            SetVisible(false);
        }

        /// <summary>填充默认值（来自启动参数或上次输入）。</summary>
        /// <param name="nickname">昵称。</param>
        /// <param name="passphrase">口令。</param>
        /// <param name="address">服务器地址。</param>
        /// <param name="port">服务器端口。</param>
        public void SetDefaults(string nickname, string passphrase, string address, int port)
        {
            if (!string.IsNullOrEmpty(address))
            {
                m_AddressInput.SetValue(address);
            }

            if (port > 0)
            {
                m_PortInput.SetValue(port.ToString());
            }

            if (!string.IsNullOrEmpty(nickname))
            {
                m_NicknameInput.SetValue(nickname);
            }

            // 输入框只收 ASCII：像"测试员"这类用 -nickname 传进来的中文昵称会被过滤成空字符串，
            // 留下一个通不过校验的空格子（U-99 的同类问题）。空就补一个建议值兜底——
            // 想用中文名仍可走启动参数。
            if (m_NicknameInput.Model.IsEmpty)
            {
                m_NicknameInput.SetValue(LobbyText.SuggestNickname());
            }

            if (!string.IsNullOrEmpty(passphrase))
            {
                m_PassphraseInput.SetValue(passphrase);
            }
        }

        /// <summary>显示或隐藏界面；隐藏时释放输入焦点。</summary>
        /// <param name="visible">是否显示。</param>
        public void SetVisible(bool visible)
        {
            m_IsVisible = visible;
            if (!visible)
            {
                Focus(null);
            }

            if (m_Root != null)
            {
                m_Root.gameObject.SetActive(visible);
            }
        }

        /// <summary>设置底部状态行。</summary>
        /// <param name="text">文本。</param>
        /// <param name="isError">是否错误（错误用红字）。</param>
        public void SetStatus(string text, bool isError)
        {
            m_StatusLabel.text = text ?? string.Empty;
            m_StatusLabel.color = isError ? UiPalette.Bad : UiPalette.InkSoft;
        }

        /// <summary>设置"正在扫描"状态。</summary>
        /// <param name="scanning">是否扫描中。</param>
        public void SetScanning(bool scanning)
        {
            m_IsScanning = scanning;
            m_ScanButton.Label.text = scanning ? "扫描中…" : "扫描局域网";
            RefreshInteractable();
        }

        /// <summary>替换扫描结果列表。</summary>
        /// <param name="rooms">房间列表（可为空）。</param>
        public void SetScanResults(IReadOnlyList<MultiplayerMenuRoom> rooms)
        {
            m_Rooms = rooms ?? Array.Empty<MultiplayerMenuRoom>();
            m_RoomListLabel.text = LobbyText.DescribeRoomList(m_Rooms.Count);

            for (var i = 0; i < m_RoomRows.Length; i++)
            {
                var row = m_RoomRows[i];
                if (i >= m_Rooms.Count)
                {
                    row.Rect.gameObject.SetActive(false);
                    continue;
                }

                row.Rect.gameObject.SetActive(true);
                row.Label.text = LobbyText.DescribeScanRow(m_Rooms[i]);
            }

            RefreshInteractable();
        }

        /// <summary>设置忙碌状态（连接 / 登录中）：所有输入与按钮禁用。</summary>
        /// <param name="busy">是否忙碌。</param>
        public void SetBusy(bool busy)
        {
            m_IsBusy = busy;
            if (busy)
            {
                Focus(null);
            }

            RefreshInteractable();
        }

        /// <summary>
        /// 是否显示「云主机」一键填入按钮。
        /// </summary>
        /// <param name="available">启动器提供了服务器地址预填时为 true。</param>
        /// <remarks>没有预填地址时（直接双击启动）按钮隐藏——没有可填的内容，
        /// 留着只会让玩家点出一个"没有云主机地址"的错误。</remarks>
        public void SetCloudServerAvailable(bool available)
        {
            if (m_CloudServerButton == null)
            {
                return;
            }

            m_CloudServerButton.Rect.gameObject.SetActive(available);
            RefreshInteractable();
        }

        /// <summary>把地址与端口填进输入框（一键选云主机 / 启动器预填时调用）。</summary>
        /// <param name="address">要填入的地址文本（隐藏时传占位符）。</param>
        /// <param name="port">端口；非正值时不动端口输入框。</param>
        public void SetAddress(string address, int port)
        {
            if (!string.IsNullOrEmpty(address))
            {
                m_AddressInput.SetValue(address);
            }

            if (port > 0)
            {
                m_PortInput.SetValue(port.ToString());
            }
        }

        /// <summary>
        /// 切换地址框的"隐藏源"模式。
        /// </summary>
        /// <param name="hidden">true = 隐藏源：值留空、用占位提示表示"已隐藏"，连接时用内存里的真实地址。</param>
        /// <param name="placeholder">隐藏时显示的占位提示（可以含中文——占位符不经过字符白名单）。</param>
        /// <param name="port">要一并填入的端口；非正值时不动端口输入框。</param>
        /// <remarks>
        /// <b>为什么把"已隐藏"放进占位符而不是值里（U-99）：</b>输入框只收 ASCII
        /// （见 <see cref="UiTextEditModel.IsAllowedCharacter"/>），把中文写进值里会被过滤成空字符串——
        /// 玩家看到的是一格空白，连接校验还会报"请先填写服务器地址"。占位符不经过过滤，正好放这类提示。
        /// </remarks>
        public void SetAddressHidden(bool hidden, string placeholder, int port = 0)
        {
            m_AddressHiddenMode = hidden;

            if (hidden)
            {
                m_AddressInput.SetValue(string.Empty);
                m_AddressInput.SetPlaceholder(placeholder);
            }
            else
            {
                m_AddressInput.SetPlaceholder(AddressPlaceholder);
            }

            if (port > 0)
            {
                m_PortInput.SetValue(port.ToString());
            }
        }

    }
}
