using System.Collections.Generic;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器状态快照：Dashboard（状态页）展示所需的全部数据。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是"快照"而不是直接读服务器运行时：</b>状态页在另一个线程（HTTP 请求线程）里读取数据，
    /// 而房间、玩家、日志都只在主线程被改写。让状态页直接引用那些可变对象会造成跨线程读写，
    /// 症状是随机崩溃或读到半更新的列表。快照是一次性的只读拷贝：
    /// 主线程生成、HTTP 线程只读，之后两者再无关系。</para>
    ///
    /// <para><b>为什么要单独建类型而不是直接序列化内部状态：</b>快照是对外契约——
    /// 状态页、日志、将来的监控都读它。内部结构（房间、成员、导航）随时会改，
    /// 改了只有一处需要同步更新，不会让状态页跟着编译失败。</para>
    /// </remarks>
    [System.Serializable]
    public sealed class ServerStatusSnapshot
    {
        /// <summary>进程启动到现在的秒数。</summary>
        public float UptimeSeconds;

        /// <summary>监听端口。</summary>
        public int Port;

        /// <summary>服务器是否正在监听。</summary>
        public bool Listening;

        /// <summary>本机可用的连接地址（本机回环 + 局域网 IPv4）。</summary>
        public List<string> Addresses = new List<string>();

        /// <summary>服务端存档目录（相对路径）。</summary>
        public string SaveDirectory = string.Empty;

        /// <summary>房间阶段（<see cref="LobbyPhase"/> 的字节值）。</summary>
        public byte Phase;

        /// <summary>阶段的中文名，直接用于展示。</summary>
        public string PhaseText = "空闲";

        /// <summary>房间名；空闲阶段为空。</summary>
        public string RoomName = string.Empty;

        /// <summary>是否设置了房间密码。</summary>
        public bool HasPassword;

        /// <summary>房主昵称；空闲阶段为空。</summary>
        public string HostNickname = string.Empty;

        /// <summary>服务器当前托管的世界（共享安全屋 / 战局 / 未托管），P4.5-b 起有值。</summary>
        public string WorldText = "未托管";

        /// <summary>当前世界的场景名（服务器在安全屋与战局之间切换，这里显示它现在在哪张图上）。</summary>
        public string WorldScene = string.Empty;

        /// <summary>已经在权威世界里的玩家数（安全屋门禁用它判断"全员是否回屋"）。</summary>
        public int PlayersInWorld;

        /// <summary>已连接但未加入房间的客户端数。</summary>
        public int ConnectedPlayerCount;

        /// <summary>房间成员列表（含未加入房间的在线玩家时以 <see cref="ServerStatusMember.InRoom"/> 区分）。</summary>
        public List<ServerStatusMember> Members = new List<ServerStatusMember>();

        /// <summary>本局已进行的秒数；不在战局中时为 0。</summary>
        public float RaidElapsedSeconds;

        /// <summary>已广播的快照批数（含"无人"的保活批次），用于联机掉线排查。</summary>
        /// <remarks>
        /// 它把"服务器还在不在发包"变成可从外部观测的数字：两支诊断采样一减，
        /// 就能区分"服务器停发导致客户端超时"与"服务器在发但客户端没收到/没在发"。
        /// 日志会因进程被强杀而丢缓冲，这个计数走状态页内存读取，不受影响。
        /// </remarks>
        public int SnapshotBatches;

        /// <summary>最近的服务端日志（新的在前）。</summary>
        public List<ServerStatusLogEntry> Logs = new List<ServerStatusLogEntry>();
    }

    /// <summary>状态页里的一名在线玩家。</summary>
    [System.Serializable]
    public sealed class ServerStatusMember
    {
        /// <summary>网络标识（连接编号）。</summary>
        public int ClientId;

        /// <summary>昵称；未登录时为空。</summary>
        public string Nickname = string.Empty;

        /// <summary>是否房主。</summary>
        public bool IsHost;

        /// <summary>是否已加入房间。</summary>
        public bool InRoom;

        /// <summary>状态的中文描述（已登录 / 未登录 / 战局中 …），直接用于展示。</summary>
        public string StateText = string.Empty;
    }

    /// <summary>状态页里的一条日志。</summary>
    [System.Serializable]
    public sealed class ServerStatusLogEntry
    {
        /// <summary>记录时刻（进程运行秒数，保留两位小数）。</summary>
        public string Time = string.Empty;

        /// <summary>级别名（信息 / 警告 / 错误 / 详细）。</summary>
        public string Level = string.Empty;

        /// <summary>日志正文。</summary>
        public string Message = string.Empty;
    }
}
