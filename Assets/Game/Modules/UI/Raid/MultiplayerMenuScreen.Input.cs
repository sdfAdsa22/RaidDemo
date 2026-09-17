using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 联机界面的交互部分：鼠标 / 键盘轮询、输入框焦点与文本输入、按钮点击与连接校验。
    /// </summary>
    /// <remarks>与 <c>MultiplayerMenuScreen.Layout.cs</c> 分开成两个 partial：前者回答「界面怎么长」，
    /// 本文件回答「玩家操作怎么处理」——两者的改动原因不同，分开后每次只用读一半。</remarks>
    public sealed partial class MultiplayerMenuScreen
    {
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
            // Esc 的第一层含义是"退出输入框"，第二层才是返回主菜单——否则打字打到一半按 Esc 会直接离开界面。
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
            var overCloud = !m_IsBusy && m_CloudServerButton.Rect.gameObject.activeSelf && m_CloudServerButton.Contains(pointer);

            m_RandomNicknameButton.SetHovered(overRandom);
            m_ConnectButton.SetHovered(overConnect);
            m_ScanButton.SetHovered(overScan);
            m_BackButton.SetHovered(overBack);
            m_CloudServerButton.SetHovered(overCloud);
            m_RandomNicknameButton.ApplyVisual(overRandom && isPressed);
            m_ConnectButton.ApplyVisual(overConnect && isPressed);
            m_ScanButton.ApplyVisual(overScan && isPressed);
            m_BackButton.ApplyVisual(overBack && isPressed);
            m_CloudServerButton.ApplyVisual(overCloud && isPressed);

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

            if (overCloud)
            {
                m_Actions.SelectCloudServer?.Invoke();
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

            // 隐藏源（云主机）：地址框留空是正常状态——真实地址只在内存里（见 OnMultiplayerConnect），
            // 不能因为"看不见地址"就拦下玩家（U-99）。
            if (address.Length == 0 && !m_AddressHiddenMode)
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

            SetStatus(
                m_AddressHiddenMode && address.Length == 0
                    ? "正在连接云主机（地址已隐藏）…"
                    : $"正在连接 {address}:{port} …",
                false);
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
            m_CloudServerButton.Interactable = !m_IsBusy;
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
