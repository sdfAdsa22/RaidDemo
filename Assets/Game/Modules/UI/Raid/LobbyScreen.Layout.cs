using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 房间界面的构建与交互。
    /// </summary>
    /// <remarks>与公开 API 分开放：那条回答“装配层要调什么”，这条回答“界面怎么长”。</remarks>
    public sealed partial class LobbyScreen
    {
        /// <summary>标题条。</summary>
        private static void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            UiFactory.CreateLabel(
                panel, "房间", new Vector2(Padding, 16f), new Vector2(200f, 48f),
                UiPalette.TitleSize, TextAlignmentOptions.Left, UiPalette.Ink);
        }

        /// <summary>未进房形态：房间名、密码与创建 / 加入。</summary>
        private void BuildJoinPanel(RectTransform panel)
        {
            var top = TitleBarHeight;
            m_JoinPanel = UiFactory.CreateRect(panel, "JoinPanel");
            UiFactory.Stretch(m_JoinPanel);

            UiFactory.CreateLabel(
                m_JoinPanel, "创建房间", new Vector2(Padding, top + 24f), new Vector2(300f, 28f),
                UiPalette.SubtitleSize, TextAlignmentOptions.Left, UiPalette.Ink);

            UiFactory.CreateLabel(
                m_JoinPanel, "房间名", new Vector2(Padding, top + 62f), new Vector2(240f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_RoomNameInput = UiTextInput.Create(
                m_JoinPanel, "RoomNameInput", new Vector2(Padding, top + 88f), new Vector2(300f, 46f),
                "例：验收房间", false, LobbyText.MaxRoomNameLength);

            UiFactory.CreateLabel(
                m_JoinPanel, "房间密码（可留空 = 不设密码，设了就是 4 位数字）",
                new Vector2(Padding, top + 152f), new Vector2(420f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_PasswordInput = UiTextInput.Create(
                m_JoinPanel, "RoomPasswordInput", new Vector2(Padding, top + 178f), new Vector2(300f, 46f),
                "可留空", true, LobbyText.RoomPasswordDigits);

            m_CreateButton = UiFactory.CreateButton(
                m_JoinPanel, "创建房间", new Vector2(Padding, top + 246f), new Vector2(220f, 54f), UiButtonKind.Primary);
            m_JoinButton = UiFactory.CreateButton(
                m_JoinPanel, "加入房间", new Vector2(Padding + 232f, top + 246f), new Vector2(220f, 54f));

            UiFactory.CreateLabel(
                m_JoinPanel,
                "一台服务器同时只有一个房间：没有房间时创建，已经有了就加入。\n"
                + "加入需要知道房间密码；密码错、房间满、已开局都会由服务器给出具体原因。",
                new Vector2(Padding, top + 322f),
                new Vector2(PanelSize.x - (Padding * 2f), 76f),
                UiPalette.SmallSize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkDisabled,
                wrap: true);
        }

        /// <summary>进房形态：成员列表与开局 / 离开。</summary>
        private void BuildRoomPanel(RectTransform panel)
        {
            var top = TitleBarHeight;
            m_RoomPanel = UiFactory.CreateRect(panel, "RoomPanel");
            UiFactory.Stretch(m_RoomPanel);

            m_RoomTitleLabel = UiFactory.CreateLabel(
                m_RoomPanel, string.Empty, new Vector2(Padding, top + 22f), new Vector2(PanelSize.x - (Padding * 2f), 32f),
                UiPalette.SubtitleSize, TextAlignmentOptions.Left, UiPalette.Ink);

            UiFactory.CreateLabel(
                m_RoomPanel, "成员", new Vector2(Padding, top + 66f), new Vector2(200f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);

            for (var i = 0; i < m_MemberLabels.Length; i++)
            {
                m_MemberLabels[i] = UiFactory.CreateLabel(
                    m_RoomPanel,
                    "（空位）",
                    new Vector2(Padding, top + 94f + (i * 40f)),
                    new Vector2(PanelSize.x - (Padding * 2f), 34f),
                    UiPalette.BodySize,
                    TextAlignmentOptions.Left,
                    UiPalette.InkDisabled);
            }

            m_StartButton = UiFactory.CreateButton(
                m_RoomPanel, "开始战局", new Vector2(Padding, top + 268f), new Vector2(260f, 54f), UiButtonKind.Primary);
            m_LeaveButton = UiFactory.CreateButton(
                m_RoomPanel, "离开房间（Esc）", new Vector2(Padding + 272f, top + 268f), new Vector2(240f, 54f));

            m_RoomHintLabel = UiFactory.CreateLabel(
                m_RoomPanel, string.Empty, new Vector2(Padding, top + 336f), new Vector2(PanelSize.x - (Padding * 2f), 60f),
                UiPalette.SmallSize, TextAlignmentOptions.TopLeft, UiPalette.InkDisabled, wrap: true);
        }

        /// <summary>底部状态行与"我是谁"。</summary>
        private void BuildStatusLine(RectTransform panel)
        {
            m_NicknameLabel = UiFactory.CreateLabel(
                panel, string.Empty, new Vector2(PanelSize.x - 300f, 30f), new Vector2(264f, 28f),
                UiPalette.SmallSize, TextAlignmentOptions.Right, UiPalette.InkSoft);

            m_StatusLabel = UiFactory.CreateLabel(
                panel, string.Empty, new Vector2(Padding, PanelSize.y - 46f), new Vector2(PanelSize.x - (Padding * 2f), 30f),
                UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.InkSoft);
        }

        /// <summary>每帧轮询鼠标与键盘（只在可见时）。</summary>
        private void Update()
        {
            if (!m_IsVisible)
            {
                return;
            }

            var keyboard = Keyboard.current;
            EnsureTextSubscription(keyboard);

            var mouse = Mouse.current;
            var pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            var isPressed = mouse != null && mouse.leftButton.isPressed;
            var wasPressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

            var consumed = HandleFields(pointer, wasPressed);
            HandleButtons(pointer, isPressed, wasPressed, consumed);

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (m_FocusedInput != null)
                {
                    Focus(null);
                }
                else if (m_InRoom)
                {
                    m_Actions.LeaveRoom?.Invoke();
                }
            }
        }

        /// <summary>输入框焦点与特殊键；返回本帧是否被输入框消费。</summary>
        private bool HandleFields(Vector2 pointer, bool wasPressed)
        {
            if (!m_InRoom)
            {
                m_RoomNameInput.ApplyVisual(!m_IsBusy && m_RoomNameInput.Contains(pointer));
                m_PasswordInput.ApplyVisual(!m_IsBusy && m_PasswordInput.Contains(pointer));
            }

            if (m_FocusedInput != null)
            {
                m_FocusedInput.HandleSpecialKeys(Keyboard.current);
            }

            if (!wasPressed || m_IsBusy || m_InRoom)
            {
                return false;
            }

            if (m_RoomNameInput.Contains(pointer))
            {
                Focus(m_RoomNameInput);
                return true;
            }

            if (m_PasswordInput.Contains(pointer))
            {
                Focus(m_PasswordInput);
                return true;
            }

            Focus(null);
            return false;
        }

        /// <summary>按钮的悬停与点击。</summary>
        private void HandleButtons(Vector2 pointer, bool isPressed, bool wasPressed, bool consumed)
        {
            if (m_InRoom)
            {
                var canStart = !m_IsBusy && m_IsHost && !m_IsInRaid;
                var overStart = canStart && m_StartButton.Contains(pointer);
                var overLeave = !m_IsBusy && m_LeaveButton.Contains(pointer);

                m_StartButton.SetHovered(overStart);
                m_LeaveButton.SetHovered(overLeave);
                m_StartButton.ApplyVisual(overStart && isPressed);
                m_LeaveButton.ApplyVisual(overLeave && isPressed);

                if (!wasPressed || consumed)
                {
                    return;
                }

                if (overStart)
                {
                    m_Actions.StartRaid?.Invoke();
                    return;
                }

                if (overLeave)
                {
                    m_Actions.LeaveRoom?.Invoke();
                }

                return;
            }

            var overCreate = !m_IsBusy && m_CreateButton.Contains(pointer);
            var overJoin = !m_IsBusy && m_JoinButton.Contains(pointer);

            m_CreateButton.SetHovered(overCreate);
            m_JoinButton.SetHovered(overJoin);
            m_CreateButton.ApplyVisual(overCreate && isPressed);
            m_JoinButton.ApplyVisual(overJoin && isPressed);

            if (!wasPressed || consumed)
            {
                return;
            }

            var password = m_PasswordInput.Model.Value ?? string.Empty;
            if (overCreate)
            {
                var roomName = (m_RoomNameInput.Model.Value ?? string.Empty).Trim();
                if (!LobbyText.IsValidRoomName(roomName))
                {
                    SetStatus($"房间名需要 1~{LobbyText.MaxRoomNameLength} 个字符。", true);
                    return;
                }

                if (!LobbyText.IsValidRoomPassword(password))
                {
                    SetStatus($"房间密码留空或填 {LobbyText.RoomPasswordDigits} 位数字。", true);
                    return;
                }

                SetStatus($"正在创建房间「{roomName}」…", false);
                m_Actions.CreateRoom?.Invoke(roomName, password);
                return;
            }

            if (overJoin)
            {
                if (!LobbyText.IsValidRoomPassword(password))
                {
                    SetStatus($"房间密码留空或填 {LobbyText.RoomPasswordDigits} 位数字。", true);
                    return;
                }

                SetStatus("正在加入房间…", false);
                m_Actions.JoinRoom?.Invoke(password);
            }
        }

        /// <summary>切回"未进房"形态。</summary>
        private void ShowJoinForm()
        {
            m_InRoom = false;
            m_IsHost = false;
            m_IsInRaid = false;
            if (m_JoinPanel != null)
            {
                m_JoinPanel.gameObject.SetActive(true);
                m_RoomPanel.gameObject.SetActive(false);
            }

            Focus(null);
            RefreshInteractable();
        }

        /// <summary>按忙碌 / 房间状态刷新可交互性。</summary>
        private void RefreshInteractable()
        {
            if (m_JoinPanel != null)
            {
                m_RoomNameInput.Interactable = !m_IsBusy;
                m_PasswordInput.Interactable = !m_IsBusy;
                m_RoomNameInput.Refresh();
                m_PasswordInput.Refresh();
            }

            if (m_CreateButton != null)
            {
                m_CreateButton.Interactable = !m_IsBusy;
                m_JoinButton.Interactable = !m_IsBusy;
                m_StartButton.Interactable = !m_IsBusy && m_IsHost && !m_IsInRaid;
                m_LeaveButton.Interactable = !m_IsBusy;
            }
        }

        /// <summary>切换输入焦点。</summary>
        private void Focus(UiTextInput input)
        {
            if (m_FocusedInput == input)
            {
                return;
            }

            m_FocusedInput?.SetFocus(false);
            m_FocusedInput = input;
            m_FocusedInput?.SetFocus(true);
        }

        /// <summary>文本输入事件只在可见时接进来。</summary>
        private void EnsureTextSubscription(Keyboard keyboard)
        {
            if (keyboard == m_SubscribedKeyboard)
            {
                return;
            }

            if (m_SubscribedKeyboard != null)
            {
                m_SubscribedKeyboard.onTextInput -= OnTextInput;
            }

            m_SubscribedKeyboard = keyboard;
            if (m_SubscribedKeyboard != null)
            {
                m_SubscribedKeyboard.onTextInput += OnTextInput;
            }
        }

        /// <summary>系统文本输入：只写给当前聚焦的输入框。</summary>
        private void OnTextInput(char c)
        {
            if (!m_IsVisible || m_IsBusy || m_FocusedInput == null || m_InRoom)
            {
                return;
            }

            if (c == '\n' || c == '\r' || c == '\t')
            {
                return;
            }

            m_FocusedInput.ApplyTextInput(c);
        }

        private void OnDestroy()
        {
            if (m_SubscribedKeyboard != null)
            {
                m_SubscribedKeyboard.onTextInput -= OnTextInput;
                m_SubscribedKeyboard = null;
            }
        }
    }
}
