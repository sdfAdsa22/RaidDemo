using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器侧的发现应答器：收到探测广播就单播回一条房间公告。
    /// </summary>
    /// <remarks>
    /// <para><b>它为什么不做定时广播：</b>定时广播会让所有服务器持续占用广播域，
    /// 而玩家只在打开联机界面时才会扫一次。应答式（探测→回包）把流量与"有人需要"绑定。</para>
    ///
    /// <para><b>失败一律降级：</b>端口被占用、网卡不可用、权限不足都只是"自动发现不可用"，
    /// 手输地址照样能连。因此这里不抛异常给调用方，只记一行日志并把组件置为停止。</para>
    ///
    /// <para>线程约束：只在主线程调用（<see cref="Poll"/> 由服务器每帧驱动）。</para>
    /// </remarks>
    public sealed class LanDiscoveryResponder : IDisposable
    {
        private readonly int m_Port;
        private readonly Func<LanRoomInfo> m_Provider;
        private readonly Action<string> m_Log;

        private UdpClient m_Socket;

        /// <summary>是否正在监听探测包。</summary>
        public bool IsRunning => m_Socket != null;

        /// <summary>监听端口。</summary>
        public int Port => m_Port;

        /// <summary>创建应答器（不启动）。</summary>
        /// <param name="port">发现端口（<c>-discoveryPort</c>）。</param>
        /// <param name="provider">取当前房间公告的回调（每次收到探测时调用，保证人数与阶段是最新的）。</param>
        /// <param name="log">日志出口。</param>
        public LanDiscoveryResponder(int port, Func<LanRoomInfo> provider, Action<string> log)
        {
            m_Port = port;
            m_Provider = provider;
            m_Log = log;
        }

        /// <summary>开始监听。失败时记录日志并保持停止状态。</summary>
        public void Start()
        {
            if (m_Socket != null)
            {
                return;
            }

            try
            {
                var socket = new UdpClient();
                socket.EnableBroadcast = true;

                // ReuseAddress 让"同一台机器上重启服务器"不会因为旧 socket 还在 TIME_WAIT 而起不来。
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, m_Port));
                socket.Client.Blocking = false;

                m_Socket = socket;
                m_Log?.Invoke($"[服务器] 局域网发现已启用：UDP {m_Port}（广播探测 → 回房间信息）。");
            }
            catch (Exception exception)
            {
                m_Socket = null;
                m_Log?.Invoke($"[服务器] 局域网发现启动失败（不影响手输地址加入）：{exception.Message}");
            }
        }

        /// <summary>停止监听并释放 socket。</summary>
        public void Stop()
        {
            if (m_Socket == null)
            {
                return;
            }

            try
            {
                m_Socket.Close();
            }
            catch (Exception)
            {
                // 关闭失败没有补救动作：socket 已经不可用，忽略即可。
            }

            m_Socket = null;
        }

        /// <summary>每帧处理收到的探测包（非阻塞）。</summary>
        public void Poll()
        {
            var socket = m_Socket;
            if (socket == null)
            {
                return;
            }

            // 一次最多处理若干条，避免广播风暴把一帧拖住。
            for (var i = 0; i < 16; i++)
            {
                if (socket.Available <= 0)
                {
                    return;
                }

                var remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] payload;

                try
                {
                    payload = socket.Receive(ref remote);
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var text = Encoding.UTF8.GetString(payload).Trim();
                if (!string.Equals(text, LanDiscoveryConstants.ProbeToken, StringComparison.Ordinal))
                {
                    continue;
                }

                SendReply(socket, remote);
            }
        }

        /// <summary>释放（可重复调用）。</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>给一个探测来源单播回包。</summary>
        private void SendReply(UdpClient socket, IPEndPoint remote)
        {
            var info = m_Provider != null ? m_Provider() : null;
            if (info == null)
            {
                return;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(LanDiscoveryCodec.EncodeReply(info));
                socket.Send(bytes, bytes.Length, remote);
            }
            catch (Exception exception)
            {
                // 回包失败不重试：客户端会在下一轮扫描里再问一次。
                m_Log?.Invoke($"[服务器] 发现回包发送失败（{remote}）：{exception.Message}");
            }
        }
    }
}
