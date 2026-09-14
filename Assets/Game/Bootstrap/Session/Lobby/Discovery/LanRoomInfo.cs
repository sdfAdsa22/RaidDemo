namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 局域网发现里"一个房间"的公开信息。
    /// </summary>
    /// <remarks>
    /// <para><b>它只是公告，不是权威数据：</b>服务器把它放进广播回包里，
    /// 客户端拿到的只是"那里有个房间、大概什么状态"。真正能不能加入由加入时的
    /// 大厅请求决定（密码、人数、阶段都会被重新校验）。因此这里的数据允许短暂过期。</para>
    ///
    /// <para><b>为什么不带密码：</b>发现的目的是让玩家少输地址，不是免密码。
    /// 回包里只给出"有没有密码"，密码仍要走大厅通道。</para>
    /// </remarks>
    public sealed class LanRoomInfo
    {
        /// <summary>回包来源地址（由扫描器填入，不是包里的内容——包里写地址可以被伪造）。</summary>
        public string Address = string.Empty;

        /// <summary>房间名。</summary>
        public string RoomName = string.Empty;

        /// <summary>游戏监听端口（大厅与战局走的那个 UDP 端口）。</summary>
        public int GamePort;

        /// <summary>当前人数。</summary>
        public int PlayerCount;

        /// <summary>人数上限。</summary>
        public int MaxPlayers = LobbyLimits.MaxPlayers;

        /// <summary>是否设置了房间密码。</summary>
        public bool HasPassword;

        /// <summary>房间阶段（<see cref="LobbyPhase"/> 的字节值）。</summary>
        public byte Phase;

        /// <summary>列表去重与显示的键：地址 + 游戏端口。</summary>
        public string Key => Address + ":" + GamePort;

        /// <summary>是否处于可以加入的状态（等待中且未满员）。</summary>
        public bool IsJoinable => Phase == (byte)LobbyPhase.Waiting && PlayerCount < MaxPlayers;

        /// <summary>整理成一行展示文本。</summary>
        public string Describe()
        {
            string phase;
            switch ((LobbyPhase)Phase)
            {
                case LobbyPhase.InRaid:
                    phase = "战局中";
                    break;
                case LobbyPhase.Waiting:
                    phase = "等待中";
                    break;
                default:
                    phase = "空闲（可创建）";
                    break;
            }

            var password = HasPassword ? " · 有密码" : string.Empty;
            return $"{RoomName}（{PlayerCount}/{MaxPlayers} · {phase}{password}）";
        }
    }

    /// <summary>
    /// 局域网发现协议的公开常量。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么与游戏端口分开：</b>发现走的是"广播 + 回包"，游戏走的是点对点长连接。
    /// 两者混在一个端口上时，服务器进程必须先判断"这个包是探测还是连接"，
    /// 而那块判断会与网络框架的收包路径打架。分开端口之后，发现模块可以完全独立地被替换掉。</para>
    ///
    /// <para>默认端口取 47777：与游戏端口（7777）不同，且落在常用端口区间之外，冲突概率低。
    /// 同一台机器上跑多个服务器进程时，第二个进程的发现端口可用启动参数改掉或关掉，
    /// 关掉只影响"自动发现"，不影响手输地址加入。</para>
    /// </remarks>
    public static class LanDiscoveryConstants
    {
        /// <summary>发现通道默认使用的 UDP 端口。</summary>
        public const int DefaultPort = 47777;

        /// <summary>
        /// 探测包的开头标记，用来把本协议的包与其他广播流量区分开。
        /// </summary>
        /// <remarks>带版本号：将来协议变化时，旧客户端发的探测包会被新服务器直接忽略，而不是解析错误。</remarks>
        public const string ProbeToken = "RAIDDEMO-LAN-1";

        /// <summary>回包的开头标记。</summary>
        public const string ReplyToken = "RAIDDEMO-LAN-1-ROOM";
    }
}
