using System.Collections.Generic;

namespace RaidDemo.Bootstrap
{
    /// <summary>房间成员（权威侧数据）。</summary>
    public sealed class LobbyMember
    {
        /// <summary>成员的网络标识（等于其连接编号）。</summary>
        public int ClientId;

        /// <summary>昵称。</summary>
        public string Nickname;

        /// <summary>是否房主。</summary>
        public bool IsHost;
    }

    /// <summary>
    /// 房间状态机：一台服务器同时只有一个房间，这就是"房间模型"的全部规则。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把规则做成不依赖网络的纯对象：</b>创建、加入、满员、密码错误、房主转移、
    /// 开局、解散——这些判断一旦混在 MonoBehaviour 里，就只能靠开三个进程去验证。
    /// 抽成纯逻辑之后，能在编辑模式里把每条规则连边界一起钉住，
    /// 网络层只剩下"把消息翻译成这里的调用"。</para>
    ///
    /// <para><b>阶段转移：</b></para>
    /// <list type="bullet">
    /// <item><description>空闲 → 等待中：某客户端创建房间，创建者成为房主；</description></item>
    /// <item><description>等待中 → 等待中：其他人加入 / 离开（房主离开时转移给剩余成员）；</description></item>
    /// <item><description>等待中 → 战局中：房主开局（此时不再接受加入）；</description></item>
    /// <item><description>战局中 → 等待中：全员结算，回到等待，房间保留；</description></item>
    /// <item><description>任意 → 空闲：最后一名成员离开（房间解散）。</description></item>
    /// </list>
    ///
    /// <para><b>房主只是管理员：</b>他负责开局与设置，没有任何裁判权。
    /// 房主掉线不影响正在进行的战局——权威始终在服务器进程里，这也是与"主机即玩家"最本质的区别。</para>
    ///
    /// <para>线程约束：只在主线程使用（与项目其余部分一致）。</para>
    /// </remarks>
    public sealed class LobbyRoom
    {
        private readonly List<LobbyMember> m_Members = new List<LobbyMember>(LobbyLimits.MaxPlayers);

        /// <summary>
        /// 创建房间状态机。
        /// </summary>
        /// <param name="defaultRoomName">
        /// 空闲阶段展示的房间名（服务器启动参数里的房间名）。
        /// 它只是"这台服务器叫什么"，玩家创建房间时会被真正的房间名覆盖。
        /// </param>
        /// <remarks>
        /// 空闲阶段也带一个名字，是为了让客户端在"还没人建房"时能显示
        /// 「服务器：验收房间」而不是一片空白——玩家需要知道自己在连哪台服务器。
        /// </remarks>
        public LobbyRoom(string defaultRoomName = null)
        {
            DefaultRoomName = string.IsNullOrWhiteSpace(defaultRoomName) ? string.Empty : defaultRoomName.Trim();
            RoomName = DefaultRoomName;
        }

        /// <summary>空闲阶段展示的房间名（来自服务器启动参数）。</summary>
        public string DefaultRoomName { get; }

        /// <summary>当前阶段。</summary>
        public LobbyPhase Phase { get; private set; } = LobbyPhase.Empty;

        /// <summary>房间名；空闲阶段为服务器默认名。</summary>
        public string RoomName { get; private set; } = string.Empty;

        /// <summary>房间密码；空字符串表示不设密码。</summary>
        public string Password { get; private set; } = string.Empty;

        /// <summary>房主的网络标识；空闲阶段为 -1。</summary>
        public int HostClientId { get; private set; } = -1;

        /// <summary>成员列表（只读视图）。</summary>
        public IReadOnlyList<LobbyMember> Members => m_Members;

        /// <summary>成员数量。</summary>
        public int MemberCount => m_Members.Count;

        /// <summary>是否设置了密码。</summary>
        public bool HasPassword => !string.IsNullOrEmpty(Password);

        /// <summary>房间是否存在（即是否已有人创建）。</summary>
        public bool Exists => Phase != LobbyPhase.Empty;

        /// <summary>
        /// 创建房间：仅当服务器空闲时成立，创建者成为房主。
        /// </summary>
        /// <param name="clientId">创建者的网络标识。</param>
        /// <param name="nickname">创建者昵称（已通过登录校验）。</param>
        /// <param name="roomName">房间名。</param>
        /// <param name="password">房间密码，空字符串表示不设密码。</param>
        /// <param name="error">失败原因。</param>
        public bool TryCreate(int clientId, string nickname, string roomName, string password, out LobbyError error)
        {
            error = LobbyError.None;

            if (Phase != LobbyPhase.Empty)
            {
                error = LobbyError.RoomExists;
                return false;
            }

            if (!LobbyLimits.IsValidRoomName(roomName))
            {
                error = LobbyError.BadRoomName;
                return false;
            }

            if (!LobbyLimits.IsValidRoomPassword(password))
            {
                error = LobbyError.BadPasswordFormat;
                return false;
            }

            RoomName = roomName.Trim();
            Password = password ?? string.Empty;
            HostClientId = clientId;
            Phase = LobbyPhase.Waiting;

            m_Members.Clear();
            AddMember(clientId, nickname, true);
            return true;
        }

        /// <summary>
        /// 加入房间：仅当房间处于等待中且密码正确、人数未满时成立。
        /// </summary>
        /// <param name="clientId">加入者的网络标识。</param>
        /// <param name="nickname">加入者昵称。</param>
        /// <param name="password">尝试使用的房间密码。</param>
        /// <param name="error">失败原因。</param>
        /// <remarks>
        /// "已经在这个房间里"不算失败路径里最优先的判断：先判定阶段与密码，
        /// 这样重复提交的加入请求在战局开始后会得到"战局进行中"而不是"已在房间"，
        /// 与玩家的真实处境（这一局已经开始，回不去了）一致。
        /// </remarks>
        public bool TryJoin(int clientId, string nickname, string password, out LobbyError error)
        {
            error = LobbyError.None;

            if (Phase == LobbyPhase.Empty)
            {
                error = LobbyError.RoomNotFound;
                return false;
            }

            if (Find(clientId) != null)
            {
                error = LobbyError.AlreadyInRoom;
                return false;
            }

            if (Phase == LobbyPhase.InRaid)
            {
                error = LobbyError.RaidRunning;
                return false;
            }

            if (m_Members.Count >= LobbyLimits.MaxPlayers)
            {
                error = LobbyError.RoomFull;
                return false;
            }

            if (!MatchesPassword(password))
            {
                error = LobbyError.WrongPassword;
                return false;
            }

            AddMember(clientId, nickname, false);
            return true;
        }

        /// <summary>
        /// 离开房间。
        /// </summary>
        /// <param name="clientId">离开者。</param>
        /// <param name="roomEnded">房间是否因此解散（最后一人离开）。</param>
        /// <returns>该客户端原本确实在房间里时返回 true。</returns>
        /// <remarks>
        /// 房主离开时把房主转交给剩余的第一名成员：不转交的话，
        /// 房间会卡在"没人能开局"的死局里，玩家只能全部退出重来。
        /// </remarks>
        public bool TryLeave(int clientId, out bool roomEnded)
        {
            roomEnded = false;

            var member = Find(clientId);
            if (member == null)
            {
                return false;
            }

            m_Members.Remove(member);

            if (m_Members.Count == 0)
            {
                ResetToEmpty();
                roomEnded = true;
                return true;
            }

            if (member.IsHost || HostClientId == clientId)
            {
                m_Members[0].IsHost = true;
                HostClientId = m_Members[0].ClientId;
            }

            return true;
        }

        /// <summary>
        /// 开始战局：仅房主、且房间处于等待中时成立。
        /// </summary>
        /// <param name="clientId">发起者。</param>
        /// <param name="error">失败原因。</param>
        /// <remarks>
        /// 允许单人开局（1 人也能进战局）：两人合作是主要玩法，但"一个人进图试试手里的枪"
        /// 是开发期最常用的路径，禁掉它会让每次联机验证都要凑两个人。
        /// </remarks>
        public bool TryStartRaid(int clientId, out LobbyError error)
        {
            error = LobbyError.None;

            if (Phase == LobbyPhase.Empty)
            {
                error = LobbyError.RoomNotFound;
                return false;
            }

            if (Find(clientId) == null)
            {
                error = LobbyError.NotInRoom;
                return false;
            }

            if (clientId != HostClientId)
            {
                error = LobbyError.NotHost;
                return false;
            }

            if (Phase == LobbyPhase.InRaid)
            {
                error = LobbyError.StartRejected;
                return false;
            }

            if (m_Members.Count == 0)
            {
                error = LobbyError.StartRejected;
                return false;
            }

            Phase = LobbyPhase.InRaid;
            return true;
        }

        /// <summary>
        /// 战局结束（全员结算）：回到等待阶段，成员保留，可以再开一局。
        /// </summary>
        public void EndRaid()
        {
            if (Phase != LobbyPhase.InRaid)
            {
                return;
            }

            Phase = LobbyPhase.Waiting;
        }

        /// <summary>按网络标识查找成员；不存在返回 null。</summary>
        /// <param name="clientId">网络标识。</param>
        public LobbyMember Find(int clientId)
        {
            for (var i = 0; i < m_Members.Count; i++)
            {
                if (m_Members[i].ClientId == clientId)
                {
                    return m_Members[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 把当前状态整理成广播消息。
        /// </summary>
        /// <remarks>
        /// 放在状态机里而不是服务器运行时里：消息格式与房间数据是同一件事，
        /// 分开写会出现"加了成员字段但广播忘了填"这类不一致。
        /// </remarks>
        public RoomStateMessage ToMessage()
        {
            var message = new RoomStateMessage
            {
                Phase = (byte)Phase,
                RoomName = RoomName ?? string.Empty,
                HasPassword = HasPassword,
                HostClientId = HostClientId,
                MemberCount = m_Members.Count,
            };

            for (var i = 0; i < m_Members.Count && i < LobbyLimits.MaxPlayers; i++)
            {
                var member = m_Members[i];
                message.SetMember(i, new LobbyMemberEntry
                {
                    ClientId = member.ClientId,
                    Nickname = member.Nickname ?? string.Empty,
                    IsHost = member.IsHost,
                });
            }

            return message;
        }

        /// <summary>密码是否匹配（未设密码时任何输入都视为匹配）。</summary>
        private bool MatchesPassword(string attempt)
        {
            if (!HasPassword)
            {
                return true;
            }

            return string.Equals(Password, attempt ?? string.Empty, System.StringComparison.Ordinal);
        }

        /// <summary>把状态复位成空闲。</summary>
        private void ResetToEmpty()
        {
            m_Members.Clear();
            Phase = LobbyPhase.Empty;
            HostClientId = -1;
            Password = string.Empty;
            RoomName = DefaultRoomName;
        }

        /// <summary>加入成员列表。</summary>
        private void AddMember(int clientId, string nickname, bool isHost)
        {
            m_Members.Add(new LobbyMember
            {
                ClientId = clientId,
                Nickname = nickname,
                IsHost = isHost,
            });
        }
    }
}
