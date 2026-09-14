using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 联机界面的构建与交互。
    /// </summary>
    /// <remarks>与公开 API 分开放：那条回答“装配层要调什么”，这条回答“界面怎么长”。</remarks>
    public sealed partial class MultiplayerMenuScreen
    {
        /// <summary>标题条。</summary>
        private static void BuildTitleBar(RectTransform panel)
        {
            UiFactory.CreatePanel(panel, "TitleBar", new Vector2(PanelSize.x, TitleBarHeight), UiSprites.CardDim, Vector2.zero);

            UiFactory.CreateLabel(
                panel, "联机", new Vector2(Padding, 16f), new Vector2(320f, 48f),
                UiPalette.TitleSize, TextAlignmentOptions.Left, UiPalette.Ink);

            UiFactory.CreateLabel(
                panel, "一台服务器 = 一个房间 · 2~4 人合作",
                new Vector2(Padding + 130f, 32f), new Vector2(460f, 30f),
                UiPalette.SubtitleSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
        }

        /// <summary>左栏：地址、昵称、口令与三个按钮。</summary>
        private void BuildLeftColumn(RectTransform panel)
        {
            var top = TitleBarHeight;

            UiFactory.CreateLabel(
                panel, "服务器地址", new Vector2(Padding, top + 16f), new Vector2(240f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_AddressInput = UiTextInput.Create(
                panel, "AddressInput", new Vector2(Padding, top + 42f), new Vector2(360f, 46f),
                "127.0.0.1", false, LobbyText.MaxAddressLength);

            UiFactory.CreateLabel(
                panel, "端口", new Vector2(Padding + 376f, top + 16f), new Vector2(120f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_PortInput = UiTextInput.Create(
                panel, "PortInput", new Vector2(Padding + 376f, top + 42f), new Vector2(94f, 46f),
                "7777", false, LobbyText.MaxPortLength, "7777");

            UiFactory.CreateLabel(
                panel, "昵称（两个客户端必须不同名）", new Vector2(Padding, top + 106f), new Vector2(360f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_NicknameInput = UiTextInput.Create(
                panel, "NicknameInput", new Vector2(Padding, top + 132f), new Vector2(300f, 46f),
                "Player1234", false, LobbyText.MaxNicknameLength);
            m_RandomNicknameButton = UiFactory.CreateButton(
                panel, "随机", new Vector2(Padding + 312f, top + 132f), new Vector2(158f, 46f));

            UiFactory.CreateLabel(
                panel, "口令（4~6 位数字；第一次就是建号）", new Vector2(Padding, top + 196f), new Vector2(400f, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);
            m_PassphraseInput = UiTextInput.Create(
                panel, "PassphraseInput", new Vector2(Padding, top + 222f), new Vector2(300f, 46f),
                "例：1234", true, LobbyText.MaxPassphraseDigits);

            m_ConnectButton = UiFactory.CreateButton(
                panel, "连接并进入大厅", new Vector2(Padding, top + 290f), new Vector2(300f, 62f), UiButtonKind.Primary);
            m_ScanButton = UiFactory.CreateButton(
                panel, "扫描局域网", new Vector2(Padding + 312f, top + 290f), new Vector2(158f, 62f));
            m_BackButton = UiFactory.CreateButton(
                panel, "返回主菜单（Esc）", new Vector2(Padding, top + 364f), new Vector2(240f, 52f));

            UiFactory.CreateLabel(
                panel,
                "服务器由「-server -port 7777」启动。\n中文昵称可在启动参数里用 -nickname 指定（本批输入框只收 ASCII）。",
                new Vector2(Padding, top + 428f),
                new Vector2(LeftColumnWidth, 64f),
                UiPalette.SmallSize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkDisabled,
                wrap: true);
        }

        /// <summary>右栏：局域网房间列表。</summary>
        private void BuildRightColumn(RectTransform panel)
        {
            var top = TitleBarHeight;

            m_RoomListLabel = UiFactory.CreateLabel(
                panel, "局域网房间", new Vector2(RightColumnX, top + 16f), new Vector2(RightColumnWidth, 24f),
                UiPalette.SmallSize, TextAlignmentOptions.Left, UiPalette.InkSoft);

            for (var i = 0; i < m_RoomRows.Length; i++)
            {
                var row = UiFactory.CreateButton(
                    panel, string.Empty, new Vector2(RightColumnX, top + 46f + (i * 58f)), new Vector2(RightColumnWidth, 50f));
                row.Rect.gameObject.SetActive(false);
                m_RoomRows[i] = row;
            }

            UiFactory.CreateLabel(
                panel,
                "发现只用来省去手输地址：密码与人数仍由服务器在加入时判定。",
                new Vector2(RightColumnX, top + 400f),
                new Vector2(RightColumnWidth, 60f),
                UiPalette.SmallSize,
                TextAlignmentOptions.TopLeft,
                UiPalette.InkDisabled,
                wrap: true);
        }

        /// <summary>底部状态行。</summary>
        private void BuildStatusLine(RectTransform panel)
        {
            m_StatusLabel = UiFactory.CreateLabel(
                panel, string.Empty, new Vector2(Padding, PanelSize.y - 46f), new Vector2(PanelSize.x - (Padding * 2f), 30f),
                UiPalette.BodySize, TextAlignmentOptions.Left, UiPalette.InkSoft);
        }

        /// <summary>
        /// 每帧轮询鼠标与键盘。
        /// </summary>
        /// <remarks>只在可见时读输入：界面隐藏后若继续读鼠标，玩家在战局里点一次左键就会触发连接。</remarks>
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

            var consumedByField = HandleFieldClicks(pointer, wasPressed);
            HandleButtons(pointer, isPressed, wasPressed, consumedByField);
            HandleRoomRows(pointer, isPressed, wasPressed, consumedByField);

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                // Esc 的第一层含义是"退出输入框"，第二层才是返回主菜单：
                // 否则玩家打字打到一半按 Esc 会直接离开这个界面。
                if (m_FocusedInput != null)
                {
                    Focus(null);
                }
                else
                {
                    m_Actions.Back?.Invoke();
                }
            }
        }

        /// <summary>点击输入框切换焦点；点空白处释放焦点。返回本帧是否被输入框消费。</summary>
        private bool HandleFieldClicks(Vector2 pointer, bool wasPressed)
        {
            var fields = m_Fields;
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].ApplyVisual(!m_IsBusy && fields[i].Contains(pointer));
            }

            if (m_FocusedInput != null)
            {
                m_FocusedInput.HandleSpecialKeys(Keyboard.current);
            }

            if (!wasPressed || m_IsBusy)
            {
                return false;
            }

            for (var i = 0; i < fields.Length; i++)
            {
                if (!fields[i].Contains(pointer))
                {
                    continue;
                }

                Focus(fields[i]);
                return true;
            }

            Focus(null);
            return false;
        }

        /// <summary>按钮的悬停与点击。</summary>
        private void HandleButtons(Vector2 pointer, bool isPressed, bool wasPressed, bool consumed)
        {
            var overRandom = !m_IsBusy && m_RandomNicknameButton.Contains(pointer);
            var overConnect = !m_IsBusy && m_ConnectButton.Contains(pointer);
            var overScan = !m_IsBusy && !m_IsScanning && m_ScanButton.Contains(pointer);
            var overBack = m_BackButton.Contains(pointer);

            m_RandomNicknameButton.SetHovered(overRandom);
            m_ConnectButton.SetHovered(overConnect);
            m_ScanButton.SetHovered(overScan);
            m_BackButton.SetHovered(overBack);
            m_RandomNicknameButton.ApplyVisual(overRandom && isPressed);
            m_ConnectButton.ApplyVisual(overConnect && isPressed);
            m_ScanButton.ApplyVisual(overScan && isPressed);
            m_BackButton.ApplyVisual(overBack && isPressed);

            if (!wasPressed || consumed)
            {
                return;
            }

            if (overRandom)
            {
                m_NicknameInput.SetValue(LobbyText.SuggestNickname());
                return;
            }

            if (overScan)
            {
                SetStatus("正在扫描局域网…", false);
                m_Actions.Scan?.Invoke();
                return;
            }

            if (overConnect)
            {
                TryConnect();
                return;
            }

            if (overBack)
            {
                m_Actions.Back?.Invoke();
            }
        }

        /// <summary>扫描结果行的悬停与点击。</summary>
        private void HandleRoomRows(Vector2 pointer, bool isPressed, bool wasPressed, bool consumed)
        {
            for (var i = 0; i < m_RoomRows.Length; i++)
            {
                var row = m_RoomRows[i];
                if (!row.Rect.gameObject.activeSelf)
                {
                    continue;
                }

                var joinable = i < m_Rooms.Count && m_Rooms[i].Joinable && !m_IsBusy;
                var over = joinable && row.Contains(pointer);
                row.SetHovered(over);
                row.ApplyVisual(over && isPressed);

                if (!wasPressed || consumed || !over)
                {
                    continue;
                }

                var room = m_Rooms[i];
                m_AddressInput.SetValue(room.Address);
                m_PortInput.SetValue(room.Port.ToString());
                SetStatus($"已选择 {room.Address}:{room.Port}，点「连接并进入大厅」。", false);
                m_Actions.JoinFound?.Invoke(room.Address, room.Port);
                return;
            }
        }

        /// <summary>校验输入并发出连接请求。</summary>
        private void TryConnect()
        {
            var address = (m_AddressInput.Model.Value ?? string.Empty).Trim();
            var nickname = (m_NicknameInput.Model.Value ?? string.Empty).Trim();
            var passphrase = m_PassphraseInput.Model.Value ?? string.Empty;

            if (address.Length == 0)
            {
                SetStatus("请先填写服务器地址（本机测试填 127.0.0.1）。", true);
                return;
            }

            if (!int.TryParse(m_PortInput.Model.Value, out var port) || port < 1 || port > 65535)
            {
                SetStatus("端口需要 1~65535 之间的数字。", true);
                return;
            }

            if (!LobbyText.IsValidNickname(nickname))
            {
                SetStatus($"昵称需要 1~{LobbyText.MaxNicknameLength} 个字符，且不能含竖线或控制字符。", true);
                return;
            }

            if (!LobbyText.IsValidPassphrase(passphrase))
            {
                SetStatus($"口令需要 {LobbyText.MinPassphraseDigits}~{LobbyText.MaxPassphraseDigits} 位数字。", true);
                return;
            }

            SetStatus($"正在连接 {address}:{port} …", false);
            m_Actions.Connect?.Invoke(address, port, nickname, passphrase);
        }

        /// <summary>按忙碌 / 扫描状态刷新可交互性。</summary>
        private void RefreshInteractable()
        {
            var fields = m_Fields;
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].Interactable = !m_IsBusy;
                fields[i].Refresh();
            }

            m_RandomNicknameButton.Interactable = !m_IsBusy;
            m_ConnectButton.Interactable = !m_IsBusy;
            m_ScanButton.Interactable = !m_IsBusy && !m_IsScanning;
            m_BackButton.Interactable = true;

            for (var i = 0; i < m_RoomRows.Length; i++)
            {
                var joinable = i < m_Rooms.Count && m_Rooms[i].Joinable;
                m_RoomRows[i].Interactable = joinable && !m_IsBusy;
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

        /// <summary>文本输入事件只在可见时接进来，隐藏后立刻断开。</summary>
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
            if (!m_IsVisible || m_IsBusy || m_FocusedInput == null)
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
