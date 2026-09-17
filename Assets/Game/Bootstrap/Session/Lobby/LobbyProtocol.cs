namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 大厅（登录、房间、等待、开局）网络通道的消息名。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把消息名集中在一个类型里：</b>两端都要用同一批字符串，
    /// 任何一处拼写不同都会表现为「消息发出去没人处理」，而框架不会报错。
    /// 常量集中之后，改名只有一处，编译器保证两端一致。</para>
    ///
    /// <para><b>通道划分：</b>请求是上行（客户端表达意图），结果是点对点下行（只回给发起者，
    /// 例如登录失败的原因），房间状态与开局广播是全体下行。分开之后，
    /// 「谁该收到什么」在通道层就确定，不需要在业务代码里判断接收者。</para>
    /// </remarks>
    public static class LobbyChannel
    {
        /// <summary>客户端 → 服务器：一条大厅请求（登录 / 创建 / 加入 / 开局 / 离开）。</summary>
        public const string RequestMessageName = "RaidDemo.Lobby.Request";

        /// <summary>服务器 → 单个客户端：请求的处理结果（成功或失败原因）。</summary>
        public const string ResultMessageName = "RaidDemo.Lobby.Result";

        /// <summary>服务器 → 全体客户端：房间当前状态（阶段、成员列表、房主）。</summary>
        public const string RoomStateMessageName = "RaidDemo.Lobby.RoomState";

        /// <summary>服务器 → 房间成员：战局开始，请加载地图并进入战局。</summary>
        public const string RaidStartMessageName = "RaidDemo.Lobby.RaidStart";

        /// <summary>服务器 → 房间成员：这一局结束，请加载安全屋场景（P4.5-b 的战后回屋）。</summary>
        public const string RaidEndMessageName = "RaidDemo.Lobby.RaidEnd";
    }

    /// <summary>大厅请求的种类。</summary>
    /// <remarks>
    /// 所有请求共用一条通道与一个结构体：字段按种类复用，
    /// 这样新增一种请求只需要加一个枚举值与一段处理器，不必新增通道。
    /// </remarks>
    public enum LobbyRequestKind : byte
    {
        /// <summary>登录 / 建号：字段 A = 昵称，字段 B = 口令或 token。</summary>
        Login = 1,

        /// <summary>创建房间：字段 A = 房间名，字段 B = 房间密码（可空）。</summary>
        CreateRoom = 2,

        /// <summary>加入房间：字段 B = 房间密码（可空）。</summary>
        JoinRoom = 3,

        /// <summary>开始战局（仅房主）。</summary>
        StartRaid = 4,

        /// <summary>离开房间（回到"未加入"状态，连接保持）。</summary>
        LeaveRoom = 5,
    }

    /// <summary>房间所处阶段。一台服务器同时只有一个房间，因此这就是服务器的状态。</summary>
    public enum LobbyPhase : byte
    {
        /// <summary>空闲：还没有人创建房间。</summary>
        Empty = 0,

        /// <summary>等待中：房间已成立，尚未开局，可以加入。</summary>
        Waiting = 1,

        /// <summary>战局中：已开局，不再接受新成员，等全员结算后回到等待。</summary>
        InRaid = 2,
    }

    /// <summary>
    /// 大厅请求的失败原因。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用错误码而不是只回一句话：</b>界面要针对不同原因给出不同引导
    /// （密码错 → 提示重输；房间满 → 提示换服务器；昵称被占用 → 提示改名）。
    /// 只回字符串的话，客户端只能靠匹配文本，改一句话就失效。</para>
    ///
    /// <para>码值一经发布不要改动：它进了网络协议，改动会让新旧版本之间出现"错误码不认识的错"。</para>
    /// </remarks>
    public enum LobbyError : byte
    {
        /// <summary>没有错误。</summary>
        None = 0,

        /// <summary>口令或 token 不正确。</summary>
        BadSecret = 1,

        /// <summary>昵称已是别人的账号名（登录时口令不匹配）。</summary>
        NicknameTaken = 2,

        /// <summary>该操作需要先登录。</summary>
        NotLoggedIn = 3,

        /// <summary>已经登录过，重复登录被拒绝。</summary>
        AlreadyLoggedIn = 4,

        /// <summary>服务器上已经有房间了（一台服务器一个房间）。</summary>
        RoomExists = 5,

        /// <summary>还没有房间（加入者先于创建者到达）。</summary>
        RoomNotFound = 6,

        /// <summary>房间人数已满。</summary>
        RoomFull = 7,

        /// <summary>房间密码不正确。</summary>
        WrongPassword = 8,

        /// <summary>该操作只有房主可以做。</summary>
        NotHost = 9,

        /// <summary>战局进行中，不能加入。</summary>
        RaidRunning = 10,

        /// <summary>已经在这个房间里了。</summary>
        AlreadyInRoom = 11,

        /// <summary>昵称格式不合法（长度或字符）。</summary>
        BadNickname = 12,

        /// <summary>密码 / 口令格式不合法（房间密码 4 位数字，口令 4~6 位数字）。</summary>
        BadPasswordFormat = 13,

        /// <summary>房间名格式不合法。</summary>
        BadRoomName = 14,

        /// <summary>不在任何房间里。</summary>
        NotInRoom = 15,

        /// <summary>当前阶段不允许开局（例如已经在战局中）。</summary>
        StartRejected = 16,

        /// <summary>该昵称已在服务器上游戏中（同一时刻不允许两个同名玩家）。</summary>
        NicknameOnline = 17,

        /// <summary>
        /// 客户端版本与服务器不一致（M10 版本握手）。说明文案里带着两个版本号，
        /// 玩家据此知道"该更新哪一端"。
        /// </summary>
        VersionMismatch = 18,

        /// <summary>
        /// 被服务器管理员强制移出房间（管理面板的"踢出玩家 / 解散房间"）。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="NotInRoom"/> 分开：后者是"你本来就不在房间里"（重复提交退房），
        /// 前者是"你被移出来了"——客户端据此显示不同的提示，将来也可以据此做禁入名单。
        /// </remarks>
        KickedOut = 19,
    }

    /// <summary>
    /// 大厅的取值约束。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么集中在这里：</b>服务器校验、客户端预校验、单元测试三处都要用同一套规则。
    /// 分头写的话，典型故障是"客户端允许输入、服务器拒绝"——玩家看到的是输入框接受了却提交失败。</para>
    ///
    /// <para>约束刻意保守（昵称 1~16 字符、房间名 1~24 字符、密码全数字）：
    /// 它们同时决定了网络消息里的固定字符串长度，放宽会导致协议字段装不下。</para>
    /// </remarks>
    public static class LobbyLimits
    {
        /// <summary>房间可容纳的最大人数（含房主）。</summary>
        public const int MaxPlayers = 4;

        /// <summary>
        /// 定长字符串字段的可用字节数上限（<c>FixedString64Bytes</c> 的容量）。
        /// </summary>
        /// <remarks>
        /// 与界面层的预校验共用同一条规则（见 <c>RaidDemo.Shared.LobbyTextBudget</c>）：
        /// 两边各写一份的话，会出现"界面允许输入、提交却被拒绝"。
        /// </remarks>
        public const int MaxFixedString64ByteCapacity = RaidDemo.Shared.LobbyTextBudget.FixedString64ByteCapacity;

        /// <summary>昵称最大长度（按字符数；同时受 <see cref="MaxFixedString64ByteCapacity"/> 约束）。</summary>
        public const int MaxNicknameLength = 16;

        /// <summary>房间名最大长度（按字符数；同时受 <see cref="MaxFixedString64ByteCapacity"/> 约束）。</summary>
        public const int MaxRoomNameLength = 24;

        /// <summary>房间密码位数（可选：留空表示不设密码）。</summary>
        public const int RoomPasswordDigits = 4;

        /// <summary>账号口令的最少位数。</summary>
        public const int MinPassphraseDigits = 4;

        /// <summary>账号口令的最多位数。</summary>
        public const int MaxPassphraseDigits = 6;

        /// <summary>token 的十六进制长度（32 个字符 = 16 字节随机数）。</summary>
        public const int TokenHexLength = 32;

        /// <summary>
        /// 校验昵称是否合法。
        /// </summary>
        /// <remarks>
        /// 只禁止控制字符与竖线：竖线是局域网发现文本协议的分隔符，
        /// 放进昵称会破坏那条通道的解析（房间名同理）。
        /// </remarks>
        public static bool IsValidNickname(string nickname)
        {
            if (!IsValidText(nickname, 1, MaxNicknameLength, MaxFixedString64ByteCapacity))
            {
                return false;
            }

            return true;
        }

        /// <summary>校验房间名是否合法。</summary>
        public static bool IsValidRoomName(string roomName)
        {
            return IsValidText(roomName, 1, MaxRoomNameLength, MaxFixedString64ByteCapacity);
        }

        /// <summary>
        /// 校验房间密码：留空（不设密码）或恰好 4 位数字。
        /// </summary>
        public static bool IsValidRoomPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return true;
            }

            return IsDigits(password, RoomPasswordDigits, RoomPasswordDigits);
        }

        /// <summary>校验账号口令：4~6 位数字。</summary>
        public static bool IsValidPassphrase(string passphrase)
        {
            return IsDigits(passphrase, MinPassphraseDigits, MaxPassphraseDigits);
        }

        /// <summary>token 的格式：32 个十六进制字符。</summary>
        public static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length != TokenHexLength)
            {
                return false;
            }

            for (var i = 0; i < token.Length; i++)
            {
                var c = token[i];
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 文本合法性：去空白后长度在范围内、UTF-8 字节数不超协议容量，且不含分隔符与控制字符。
        /// </summary>
        /// <param name="text">待校验文本。</param>
        /// <param name="minLength">最少字符数。</param>
        /// <param name="maxLength">最多字符数。</param>
        /// <param name="maxBytes">写进协议字段时的 UTF-8 字节上限。</param>
        /// <returns>合法返回 true。</returns>
        private static bool IsValidText(string text, int minLength, int maxLength, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (trimmed.Length < minLength || trimmed.Length > maxLength)
            {
                return false;
            }

            // 字符数与字节数是两把尺子：'A' 是 1 字节，汉字通常 3 字节。
            // 协议字段按字节计容量，因此两道都要过。
            if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > maxBytes)
            {
                return false;
            }

            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (c == '|' || char.IsControl(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>全部为 ASCII 数字且位数在范围内。</summary>
        private static bool IsDigits(string text, int minLength, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length < minLength || text.Length > maxLength)
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
