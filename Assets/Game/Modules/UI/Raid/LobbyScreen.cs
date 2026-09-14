using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 房间内的一名成员（界面只读模型）。
    /// </summary>
    public struct LobbyRoomMember
    {
        /// <summary>昵称。</summary>
        public string Nickname;

        /// <summary>是否房主。</summary>
        public bool IsHost;

        /// <summary>是否本机玩家。</summary>
        public bool IsSelf;
    }

    /// <summary>
    /// 房间界面把玩家意图交回装配层的回调集合。
    /// </summary>
    public struct LobbyRoomActions
    {
        /// <summary>创建房间：房间名、房间密码（可空）。</summary>
        public Action<string, string> CreateRoom;

        /// <summary>加入房间：房间密码（可空）。</summary>
        public Action<string> JoinRoom;

        /// <summary>开始战局（仅房主）。</summary>
        public Action StartRaid;

        /// <summary>离开房间。</summary>
        public Action LeaveRoom;

        /// <summary>
        /// 返回上一界面（联机界面·服务器列表）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么必须单独有一条退出路径：</b>这个界面在"未进房"形态下既没有离开房间的对象
        /// （还没进去），也没有返回按钮——玩家点进来就出不去了（P4 用户反馈的"图1 无法返回"）。
        /// Esc 原本只在"已在房间"分支里生效，同样救不了他这个处境。</para>
        /// </remarks>
        public Action Back;
    }

    /// <summary>
    /// 房间界面：未进房时是"创建 / 加入"，进房后是"成员列表 + 开局 / 离开"。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么两种形态放在同一个界面里：</b>它们是同一个决策的两面——"进哪个房间"。
    /// 分成两个界面后，玩家点错一次就要退回上一层，而这一层的全部内容只有几个控件。</para>
    ///
    /// <para><b>界面不判断"我是不是房主"：</b>能不能开局由服务器判定（<c>LobbyError.NotHost</c>）；
    /// 界面只是按服务器广播的名册把按钮画成可用或禁用。这样即使广播延迟，
    /// 也不会出现"界面允许点、服务器拒绝"的分裂。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class LobbyScreen : MonoBehaviour
    {
        /// <summary>面板尺寸（参考像素）。</summary>
        private static readonly Vector2 PanelSize = new Vector2(680f, 560f);

        /// <summary>内容区边距。</summary>
        private const float Padding = 36f;

        /// <summary>标题条高度。</summary>
        private const float TitleBarHeight = 84f;

        /// <summary>成员槽位数（与服务器房间上限一致）。</summary>
        private const int MemberRowCount = 4;

        private RectTransform m_Root;
        private RectTransform m_JoinPanel;
        private RectTransform m_RoomPanel;
        private TextMeshProUGUI m_StatusLabel;
        private TextMeshProUGUI m_NicknameLabel;
        private TextMeshProUGUI m_RoomTitleLabel;
        private TextMeshProUGUI m_RoomHintLabel;
        private readonly TextMeshProUGUI[] m_MemberLabels = new TextMeshProUGUI[MemberRowCount];
        private UiTextInput m_RoomNameInput;
        private UiTextInput m_PasswordInput;
        private UiTextInput m_FocusedInput;
        private UiButton m_CreateButton;
        private UiButton m_JoinButton;
        private UiButton m_StartButton;
        private UiButton m_LeaveButton;
        private UiButton m_BackButton;
        private LobbyRoomActions m_Actions;
        private Keyboard m_SubscribedKeyboard;
        private bool m_IsVisible;

        /// <summary>界面当前是否可见（流程层据此判断"玩家是不是已经在房间页上了"）。</summary>
        public bool IsVisible => m_IsVisible;
        private bool m_IsBusy;
        private bool m_InRoom;
        private bool m_IsHost;
        private bool m_IsInRaid;

        /// <summary>构建界面。</summary>
        /// <param name="actions">交给装配层的回调集合。</param>
        public void Initialize(LobbyRoomActions actions)
        {
            m_Actions = actions;

            m_Root = UiFactory.CreateCanvas(transform, "LobbyScreenCanvas", 345);
            UiFactory.CreateBackdrop(m_Root, "Backdrop", UiPalette.MenuBackdrop);

            var shadow = UiFactory.CreateCenteredPanel(m_Root, "PanelShadow", PanelSize, UiSprites.Card);
            shadow.anchoredPosition = new Vector2(0f, -10f);
            shadow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);

            var panel = UiFactory.CreateCenteredPanel(m_Root, "Panel", PanelSize, UiSprites.Card);
            BuildTitleBar(panel);
            BuildJoinPanel(panel);
            BuildRoomPanel(panel);
            BuildStatusLine(panel);

            ShowJoinForm();
            SetVisible(false);
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
        /// <param name="isError">是否错误。</param>
        public void SetStatus(string text, bool isError)
        {
            m_StatusLabel.text = text ?? string.Empty;
            m_StatusLabel.color = isError ? UiPalette.Bad : UiPalette.InkSoft;
        }

        /// <summary>顶部显示本机昵称，提醒玩家"我是谁"。</summary>
        /// <param name="nickname">昵称。</param>
        public void SetNickname(string nickname)
        {
            m_NicknameLabel.text = string.IsNullOrEmpty(nickname) ? string.Empty : "我是 " + nickname;
        }

        /// <summary>
        /// 按服务器广播的房间状态刷新界面。
        /// </summary>
        /// <param name="roomName">房间名；null 或空表示"还没进房间"。</param>
        /// <param name="hasPassword">房间是否设了密码。</param>
        /// <param name="isHost">本机是否房主。</param>
        /// <param name="isInRaid">房间是否已开局。</param>
        /// <param name="members">成员列表（可为空）。</param>
        public void SetRoom(string roomName, bool hasPassword, bool isHost, bool isInRaid, IReadOnlyList<LobbyRoomMember> members)
        {
            var count = members?.Count ?? 0;
            m_IsHost = isHost;
            m_IsInRaid = isInRaid;

            if (string.IsNullOrEmpty(roomName))
            {
                ShowJoinForm();
                return;
            }

            m_InRoom = true;
            m_JoinPanel.gameObject.SetActive(false);
            m_RoomPanel.gameObject.SetActive(true);

            m_RoomTitleLabel.text = LobbyText.DescribeRoomState(roomName, hasPassword, isInRaid, count);

            for (var i = 0; i < m_MemberLabels.Length; i++)
            {
                if (i >= count)
                {
                    m_MemberLabels[i].text = "（空位）";
                    m_MemberLabels[i].color = UiPalette.InkDisabled;
                    continue;
                }

                var member = members[i];
                m_MemberLabels[i].text = LobbyText.DescribeMember(member.Nickname, member.IsHost, member.IsSelf);
                m_MemberLabels[i].color = member.IsSelf ? UiPalette.TealDark : UiPalette.Ink;
            }

            m_StartButton.Label.text = isInRaid ? "战局进行中…" : (isHost ? "开始战局" : "等待房主开局");
            m_RoomHintLabel.text = isInRaid
                ? "战局结束后全体回到这个房间，房主可以再开一局。"
                : "房主开局后，所有成员一起进入地图。离开房间不会退出登录。";

            RefreshInteractable();
        }

        /// <summary>设置忙碌状态（请求往返中）：按钮与输入框禁用。</summary>
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

    }
}
