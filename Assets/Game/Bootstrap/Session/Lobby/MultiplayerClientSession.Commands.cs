namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机会话上行的操作入口：建 / 加房、开局、离开。
    /// </summary>
    /// <remarks>
    /// <para>每个入口都是同一套三步：检查当前阶段是否允许 → 本地预校验输入格式 → 发请求。
    /// 本地预校验只为"立刻给出提示"（少一次往返），真正的判定始终在服务器：
    /// 客户端能改，服务器不能信。</para>
    ///
    /// <para>格式约束与服务器共用 <see cref="LobbyLimits"/>：两边规则不同会出现
    /// "界面允许输入、提交却被拒绝"这种最难向玩家解释的状态。</para>
    /// </remarks>
    public sealed partial class MultiplayerClientSession
    {
        /// <summary>创建房间。</summary>
        /// <param name="roomName">房间名（可空，空则用服务器默认名）。</param>
        /// <param name="password">房间密码（空字符串表示不设）。</param>
        public void CreateRoom(string roomName, string password)
        {
            if (!RequirePhase(MultiplayerClientPhase.InLobby))
            {
                return;
            }

            if (!LobbyLimits.IsValidRoomName(roomName))
            {
                SetError(
                    $"房间名需要 1~{LobbyLimits.MaxRoomNameLength} 个字符，"
                    + $"且不超过 {LobbyLimits.MaxFixedString64ByteCapacity} 字节（约 20 个汉字）。");
                return;
            }

            if (!LobbyLimits.IsValidRoomPassword(password))
            {
                SetError($"房间密码需要留空或 {LobbyLimits.RoomPasswordDigits} 位数字。");
                return;
            }

            ClearError();
            SetStatus("正在创建房间…");
            SendLobbyRequest(LobbyRequestKind.CreateRoom, roomName.Trim(), password ?? string.Empty);
        }

        /// <summary>加入房间。</summary>
        /// <param name="password">房间密码（房间未设密码时留空）。</param>
        public void JoinRoom(string password)
        {
            if (!RequirePhase(MultiplayerClientPhase.InLobby))
            {
                return;
            }

            if (!LobbyLimits.IsValidRoomPassword(password))
            {
                SetError($"房间密码需要留空或 {LobbyLimits.RoomPasswordDigits} 位数字。");
                return;
            }

            ClearError();
            SetStatus("正在加入房间…");
            SendLobbyRequest(LobbyRequestKind.JoinRoom, string.Empty, password ?? string.Empty);
        }

        /// <summary>
        /// 开始战局（仅房主）。
        /// </summary>
        /// <param name="mapSceneName">房主选择的地图场景名；留空表示由服务器决定（验收路径）。</param>
        /// <remarks>
        /// <para>P4.5-b 起，这条请求由**安全屋出口**发出：房主在出口选定地图、确认全员都在屋里之后，
        /// 客户端把地图名一起上行，服务器据此切换自己托管的世界。</para>
        ///
        /// <para>地图名走"字段 A"：大厅请求的结构体是复用的（见 <c>LobbyRequestMessage</c>），
        /// 新增一种含义不需要新增通道，也不必为它单独加一个字段。</para>
        /// </remarks>
        public void StartRaid(string mapSceneName = null)
        {
            if (!RequirePhase(MultiplayerClientPhase.InRoom))
            {
                return;
            }

            ClearError();
            SetStatus("正在前往战局…");
            SendLobbyRequest(LobbyRequestKind.StartRaid, mapSceneName ?? string.Empty, string.Empty);
        }

        /// <summary>离开房间（连接保持）。</summary>
        public void LeaveRoom()
        {
            if (!IsConnected)
            {
                SetError("尚未连接到服务器。");
                return;
            }

            ClearError();
            SendLobbyRequest(LobbyRequestKind.LeaveRoom, string.Empty, string.Empty);
        }

        /// <summary>
        /// 检查当前阶段是否允许该操作；不允许时给一句人话。
        /// </summary>
        /// <param name="required">要求的阶段。</param>
        private bool RequirePhase(MultiplayerClientPhase required)
        {
            if (!IsConnected)
            {
                SetError("尚未连接到服务器。");
                return false;
            }

            if (Phase != required)
            {
                SetError($"当前状态（{DescribePhase(Phase)}）不能执行这个操作。");
                return false;
            }

            return true;
        }
    }
}
