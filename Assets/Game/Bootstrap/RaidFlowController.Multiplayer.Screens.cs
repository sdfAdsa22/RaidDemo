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

            if (string.IsNullOrEmpty(nickname))
            {
                nickname = LobbyText.SuggestNickname();
            }

            m_MultiplayerScreen.SetDefaults(nickname, DefaultPassphrase, address, port);
            m_MultiplayerScreen.SetBusy(false);
            m_MultiplayerScreen.SetScanning(false);
            m_MultiplayerScreen.SetStatus(
                string.IsNullOrEmpty(error) ? (status ?? string.Empty) : error,
                !string.IsNullOrEmpty(error));
            m_MultiplayerScreen.SetVisible(true);
            m_LobbyScreen.SetVisible(false);
            m_MultiplayerUiActive = true;
        }

        /// <summary>默认口令：与命令行验收用的默认值一致，方便第一次联机的人直接连上。</summary>
        private const string DefaultPassphrase = "123456";

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
