using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的联机退出部分：回到服务器列表、回到主菜单、以及"被管理员移出房间"的落地。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>退出路径是一组相互调用的方法（离开确认 → 断开 → 重载场景），
    /// 与"连接与开局"的改动原因不同；文件也有 400 行上限，拆开后主文件保持可读。</para>
    ///
    /// <para><b>三种退出不是同一件事：</b>返回服务器列表只断开这一条会话；
    /// 返回主菜单要改进程身份并重载安全屋；被管理员移出房间则要跳过"离开房间"握手
    /// （服务器已经把我们移出去了）并把原因留给主菜单显示。</para>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        /// <summary>
        /// 房间界面点「返回」：断开会话并回到联机界面（服务器列表）。
        /// </summary>
        /// <remarks>
        /// 断开而不是"保留连接回到列表"：联机界面上的「连接」是唯一的入口，
        /// 保留一条已建立的连接会让那个按钮处于"已经连上了"的哑火状态，玩家只能靠猜。
        /// 离开房间走显式的 LeaveRoom（而不是靠断线被当作退出）——
        /// 后者会进 60 秒宽限，玩家重新连接时被接管回旧房间。
        /// </remarks>
        private void OnMultiplayerBackToServers()
        {
            LeaveRoomThen(() =>
            {
                m_Session?.Disconnect();
                ShowServersScreen(null, "已返回服务器列表。");
            });
        }

        /// <summary>联机界面点「返回主菜单」：断开连接并回到主菜单。</summary>
        /// <param name="skipLeaveRoom">跳过"离开房间"握手（被管理员移出房间时用）。</param>
        private void ReturnToMainMenuFromMultiplayer(bool skipLeaveRoom = false)
        {
            m_LeavingMultiplayer = true;
            m_MultiplayerUiActive = false;
            m_MultiplayerScreen?.SetVisible(false);
            m_LobbyScreen?.SetVisible(false);

            // 先"离开房间"再断开：直接断开会进 60 秒宽限，宽限期内重进会被接管回旧房间
            // （负责人反馈的"战局进行中，进不去"）。
            BeginExitTransition(ExitMultiplayerToMainMenu, skipLeaveRoom);
        }

        /// <summary>退出联机的收尾：断开 + 改进程身份 + 重载安全屋回主菜单。</summary>
        /// <remarks>
        /// 与"暂停菜单 → 返回主菜单"走同一条路：只切状态的话，安全屋身上还挂着已经断开的移动/容器链路，
        /// 命令处理器也停在"只上行"的版本上——玩家会看到主菜单回来了，但人物不动、仓库点不动
        /// （同一类缺陷的另一个入口）。
        /// </remarks>
        private void ExitMultiplayerToMainMenu()
        {
            m_Session?.Disconnect();
            ClientMode.Deactivate();
            m_LeavingMultiplayer = false;
            HidePauseMenu();
            State = FlowState.MainMenu;
            Time.timeScale = 1f;
            SceneManager.LoadScene(GameScenes.SafeHouse);
        }

        /// <summary>
        /// 服务器把本机玩家移出房间（管理面板踢人 / 解散房间）。
        /// </summary>
        /// <param name="reason">给玩家看的提示文案。</param>
        /// <remarks>
        /// <para>战局里没有能显示提示的联机界面：把原因存进一次性提示槽、走"返回主菜单"，
        /// 主菜单重新显示时再取出来——否则玩家只会发现自己突然回到菜单，不知道发生了什么。</para>
        ///
        /// <para>大厅 / 安全屋里则把原因贴到当前可见界面上即可：会话状态已经回到"已登录"，
        /// 玩家可以立刻重新进房（管理面板的语义就是不封禁）。</para>
        /// </remarks>
        private void OnSessionForcedOut(string reason)
        {
            var inRaid = m_Session != null && m_Session.Phase == MultiplayerClientPhase.InRaid;
            if (!inRaid)
            {
                m_MultiplayerScreen?.SetStatus(reason, true);
                m_LobbyScreen?.SetStatus(reason, true);
                return;
            }

            SessionNotice.Set(reason);
            ReturnToMainMenuFromMultiplayer(skipLeaveRoom: true);
        }
    }
}
