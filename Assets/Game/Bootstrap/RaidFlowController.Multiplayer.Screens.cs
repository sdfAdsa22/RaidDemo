using RaidDemo.UI;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的联机界面映射部分：会话状态 →（联机界面 / 房间界面）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件（P-51 拆分的）：</b>联机主文件负责"接事件、切场景、发请求"，
    /// 本文件负责"把会话状态画成界面"。界面会随 UX 调整而频繁改动，场景切换只随流程变化——
    /// 两者的改动原因不同，放在一起会让每次调界面都要读一遍网络时序代码。</para>
    ///
    /// <para><b>界面只读会话状态：</b>这里所有方法都不改变网络行为，只做 SetVisible / SetStatus 这类调用，
    /// 唯一的例外是把会话成员列表拷贝进界面用的缓冲（<c>m_RoomMembers</c>）。</para>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        /// <summary>把当前会话状态映射到界面。</summary>
        private void OnSessionChanged()
        {
            if (m_Session == null || m_MultiplayerScreen == null)
            {
                return;
            }

            switch (m_Session.Phase)
            {
                case MultiplayerClientPhase.Connecting:
                case MultiplayerClientPhase.LoggingIn:
                    m_LobbyScreen.SetVisible(false);
                    m_MultiplayerScreen.SetVisible(true);
                    m_MultiplayerScreen.SetBusy(true);
                    m_MultiplayerScreen.SetStatus(m_Session.StatusText, false);
                    break;

                case MultiplayerClientPhase.Reconnecting:
                    // P-51：重连期间只有"玩家本来就在联机界面里"时才亮出联机界面（画面停滞的人
                    // 不该被弹窗打扰，他的场景恢复由 ResumedFromReconnect 负责）。
                    if (m_MultiplayerUiActive)
                    {
                        m_LobbyScreen.SetVisible(false);
                        m_MultiplayerScreen.SetVisible(true);
                        m_MultiplayerScreen.SetBusy(true);
                        m_MultiplayerScreen.SetStatus(m_Session.StatusText, false);
                    }

                    break;

                case MultiplayerClientPhase.InLobby:
                    ApplyLobbyScreen();
                    break;

                case MultiplayerClientPhase.InRoom:
                    // P4.5-b：进房即在共享安全屋里活动——房间界面收起（角标由安全屋界面负责），
                    // 玩家可以走动、整备，房主走到出口选图。
                    EnterSafeHouseFromRoom();
                    break;

                case MultiplayerClientPhase.InRaid:
                    m_MultiplayerScreen.SetVisible(false);
                    m_LobbyScreen.SetVisible(false);
                    break;

                default:
                    // 未连接：如果界面正开着（说明玩家来过联机），把失败原因显示出来。
                    if (m_MultiplayerUiActive && !m_LeavingMultiplayer)
                    {
                        ShowServersScreen(m_Session.LastError, m_Session.LastError == null ? "连接已断开。" : null);
                    }

                    break;
            }
        }

        /// <summary>显示联机界面（服务器地址、昵称、口令、扫描结果）。</summary>
        private void ShowServersScreen(string error, string status)
        {
            var nickname = string.Empty;
            var address = "127.0.0.1";
            var port = LaunchOptions.DefaultPort;

            if (ClientAccountStore.TryLoad(out var data))
            {
                nickname = data.Nickname ?? string.Empty;
                if (!string.IsNullOrEmpty(data.LastAddress))
                {
                    ParseAddress(data.LastAddress, out address, out port);
                }
            }

            // 启动器预填的地址优先于"上次用过的地址"：它代表玩家这次选的游戏服务器，
            // 而 LastAddress 只是历史记录。启动器因此不再需要 -connect 直接拉进联机流程
            // （那样会跳过主菜单——负责人反馈的云服务器问题），只做预填。
            m_ServerAddressHidden = false;
            m_HintedServerAddress = null;
            var hint = LaunchOptions.Current != null ? LaunchOptions.Current.ServerHostHint : null;
            if (!string.IsNullOrEmpty(hint))
            {
                ParseAddress(hint, out var hintedAddress, out var hintedPort);
                m_HintedServerAddress = hintedAddress;
                m_HintedServerPort = hintedPort;
                address = hintedAddress;
                port = hintedPort;

                // 隐藏源（云主机）：地址不上屏，连接时仍用上面记下的真实值。
                m_ServerAddressHidden = LaunchOptions.Current.HideServerAddress;
            }

            if (string.IsNullOrEmpty(nickname))
            {
                nickname = LobbyText.SuggestNickname();
            }

            m_MultiplayerScreen.SetDefaults(
                nickname,
                DefaultPassphrase,
                m_ServerAddressHidden ? HiddenAddressPlaceholder : address,
                port);
            m_MultiplayerScreen.SetBusy(false);
            m_MultiplayerScreen.SetScanning(false);
            // 「云主机」一键填入只在启动器提供了地址预填时出现（见 OnMultiplayerSelectCloudServer）。
            m_MultiplayerScreen.SetCloudServerAvailable(!string.IsNullOrEmpty(m_HintedServerAddress));
            m_MultiplayerScreen.SetStatus(
                string.IsNullOrEmpty(error) ? (status ?? string.Empty) : error,
                !string.IsNullOrEmpty(error));
            m_MultiplayerScreen.SetVisible(true);
            m_LobbyScreen.SetVisible(false);
            m_MultiplayerUiActive = true;
        }

        /// <summary>默认口令：与命令行验收用的默认值一致，方便第一次联机的人直接连上。</summary>
        private const string DefaultPassphrase = "123456";

        /// <summary>
        /// 点「云主机」：把启动器预填的服务器地址一键填进地址框。
        /// </summary>
        /// <remarks>
        /// <para>地址来源是启动器的 <c>-serverhost</c> 参数（本次启动的那份配置）；
        /// 隐藏源（云主机）在界面上仍然显示"已隐藏"占位符，连接时用内存里的真实地址——
        /// 与"预填"同一条规则，见 <see cref="OnMultiplayerConnect"/>。</para>
        ///
        /// <para>没有预填地址时按钮根本不显示（见 <c>SetCloudServerAvailable</c>），
        /// 这个方法只处理"有地址可填"的情形。</para>
        /// </remarks>
        private void OnMultiplayerSelectCloudServer()
        {
            if (string.IsNullOrEmpty(m_HintedServerAddress))
            {
                m_MultiplayerScreen.SetStatus(
                    "没有可用的云主机地址：请手动输入，或从启动器启动（启动器会带上服务器地址）。",
                    true);
                return;
            }

            m_ServerAddressHidden = LaunchOptions.Current != null && LaunchOptions.Current.HideServerAddress;
            m_MultiplayerScreen.SetAddress(
                m_ServerAddressHidden ? HiddenAddressPlaceholder : m_HintedServerAddress,
                m_HintedServerPort);
            m_MultiplayerScreen.SetStatus(
                m_ServerAddressHidden
                    ? "已选择云主机（地址已隐藏），点「连接并进入大厅」。"
                    : $"已选择云主机 {m_HintedServerAddress}:{m_HintedServerPort}。",
                false);
        }

        /// <summary>
        /// 联机客户端进程里拦下"继续游戏 / 新游戏"这两个单机入口。
        /// </summary>
        /// <returns>被拦下时返回 true（调用方直接返回）。</returns>
        /// <remarks>
        /// <para>联机客户端进程由服务器裁定一切：进"没有服务器世界"的安全屋之后，
        /// 移动与转向会被上行链路接管又无人应答——表现是完全不能动
        /// （负责人反馈的"继续游戏后进图不能移动"）。</para>
        ///
        /// <para>启动器改传 <c>-serverhost</c>（预填、不自动联机）之后，正常路径不再进入这里；
        /// 这个闸门留给"手动带 -connect 启动"的旁路与自动化场景。</para>
        /// </remarks>
        private bool BlockSinglePlayerEntryInMultiplayerProcess()
        {
            if (!ClientMode.IsActive)
            {
                return false;
            }

            m_MenuScreen?.SetNotice("当前是联机客户端：请从「联机」进入服务器，单机入口暂不可用。");
            return true;
        }

        /// <summary>显示房间界面（创建 / 加入 / 成员列表）。</summary>
        private void ApplyLobbyScreen()
        {
            m_MultiplayerScreen.SetVisible(false);
            m_LobbyScreen.SetVisible(true);
            m_MultiplayerUiActive = true;
            m_LobbyScreen.SetBusy(false);
            m_LobbyScreen.SetStatus(m_Session.LastError, !string.IsNullOrEmpty(m_Session.LastError));
            m_LobbyScreen.SetNickname(m_Session.Nickname);

            m_RoomMembers.Clear();
            if (m_Session.SelfInRoom)
            {
                ResetRoomMemberBuffer();
            }

            // 只有在房间里才显示成员列表；不在房间里时传空房间名，界面会回到"创建 / 加入"表单。
            var roomName = m_Session.SelfInRoom ? m_Session.RoomName : string.Empty;
            m_LobbyScreen.SetRoom(
                roomName,
                m_Session.RoomHasPassword,
                m_Session.IsHost,
                m_Session.RoomPhase == LobbyPhase.InRaid,
                m_RoomMembers);
        }

        /// <summary>把会话里的成员列表映射成界面结构。</summary>
        private void ResetRoomMemberBuffer()
        {
            var members = m_Session.RoomMembers;
            for (var i = 0; i < members.Count; i++)
            {
                m_RoomMembers.Add(new LobbyRoomMember
                {
                    Nickname = members[i].Nickname,
                    IsHost = members[i].IsHost,
                    IsSelf = members[i].ClientId == m_Session.LocalClientId,
                });
            }
        }
    }
}
