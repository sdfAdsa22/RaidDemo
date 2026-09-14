using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 客户端侧的发现扫描器：广播探测、收集回包、按地址去重。
    /// </summary>
    /// <remarks>
    /// <para><b>扫描是一次一轮的：</b>每次 <see cref="StartScan"/> 清空结果并打开一个
    /// 1.5 秒的窗口，期间收到的回包进列表；窗口由 <see cref="Poll"/> 在每帧判定结束。
    /// 不用后台线程：主线程非阻塞轮询就够（每秒几十个包），
    /// 而后台线程会引入"集合正被别人改"的一整类问题。</para>
    ///
    /// <para><b>计时用 <see cref="Time.realtimeSinceStartup"/>：</b>主菜单会把
    /// <c>Time.timeScale</c> 压成 0，用 <c>Time.time</c> 的话扫描窗口永远不会结束。</para>
    ///
    /// <para>扫描失败（端口占用、无网卡）不抛异常：那样只会让"发现房间"这个便利功能
    /// 变成"联机界面崩了"，而手输地址本来就能用。</para>
    /// </remarks>
    public sealed class LanDiscoveryScanner : IDisposable
    {
        /// <summary>扫描窗口长度（秒）。</summary>
        public const float ScanWindowSeconds = 1.5f;

        /// <summary>窗口进行到一半时补发一次探测：Wi-Fi 丢包时提高命中率。</summary>
        private const float ResendAtSeconds = 0.6f;

        private readonly int m_Port;
        private readonly List<LanRoomInfo> m_Results = new List<LanRoomInfo>(4);

        private UdpClient m_Socket;
        private float m_Deadline = -1f;
        private float m_ResendAt = -1f;
        private bool m_Resent;

        /// <summary>本轮扫描结果（窗口结束后保留到下一次扫描）。</summary>
        public IReadOnlyList<LanRoomInfo> Results => m_Results;

        /// <summary>是否正在扫描。</summary>
        public bool IsScanning => m_Deadline >= 0f;

        /// <summary>创建扫描器。</summary>
        /// <param name="port">发现端口（与服务器 <c>-discoveryPort</c> 一致）。</param>
        public LanDiscoveryScanner(int port)
        {
            m_Port = port;
        }

        /// <summary>开始一轮扫描（清空上一轮结果并发探测广播）。</summary>
        public void StartScan()
        {
            if (!EnsureSocket())
            {
                m_Deadline = -1f;
                return;
            }

            m_Results.Clear();

            var now = Time.realtimeSinceStartup;
            m_Deadline = now + ScanWindowSeconds;
            m_ResendAt = now + ResendAtSeconds;
            m_Resent = false;

            SendProbe();
        }

        /// <summary>每帧推进：收包、补发、判定窗口结束。</summary>
        public void Poll()
        {
            if (m_Deadline < 0f)
            {
                return;
            }

            DrainReplies();

            var now = Time.realtimeSinceStartup;
            if (!m_Resent && now >= m_ResendAt)
            {
                m_Resent = true;
                SendProbe();
            }

            if (now >= m_Deadline)
            {
                m_Deadline = -1f;
            }
        }

        /// <summary>停止扫描并释放 socket。</summary>
        public void Dispose()
        {
            m_Deadline = -1f;

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
                // 无补救动作，忽略。
            }

            m_Socket = null;
        }

        /// <summary>建立 socket（失败时返回 false 并保持无 socket 状态）。</summary>
        private bool EnsureSocket()
        {
            if (m_Socket != null)
            {
                return true;
            }

            try
            {
                var socket = new UdpClient();
                socket.EnableBroadcast = true;
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // 绑定在发现端口上：服务器单播回给"探测的来源地址"，
                // 因此客户端的源端口就是它收回复的端口。
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, m_Port));
                socket.Client.Blocking = false;

                m_Socket = socket;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机] 局域网扫描不可用（可以手输地址）：{exception.Message}");
                m_Socket = null;
                return false;
            }
        }

        /// <summary>发送探测：同时发广播与回环，保证同机多进程也能互相发现。</summary>
        private void SendProbe()
        {
            var socket = m_Socket;
            if (socket == null)
            {
                return;
            }

            var payload = LanDiscoveryCodec.EncodeProbe();

            TrySend(socket, payload, new IPEndPoint(IPAddress.Broadcast, m_Port));
            TrySend(socket, payload, new IPEndPoint(IPAddress.Loopback, m_Port));
        }

        /// <summary>发一个包；失败时静默（下一轮扫描会再试）。</summary>
        private static void TrySend(UdpClient socket, byte[] payload, IPEndPoint target)
        {
            try
            {
                socket.Send(payload, payload.Length, target);
            }
            catch (SocketException)
            {
                // 某些网卡不允许广播：回环那一份仍然有效。
            }
        }

        /// <summary>把当前可读的回包全部收进来（按地址 + 游戏端口去重）。</summary>
        private void DrainReplies()
        {
            var socket = m_Socket;
            if (socket == null)
            {
                return;
            }

            for (var i = 0; i < 32; i++)
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
                if (!LanDiscoveryCodec.TryParseReply(text, remote.Address.ToString(), out var info))
                {
                    continue;
                }

                if (!Contains(info))
                {
                    m_Results.Add(info);
                }
            }
        }

        /// <summary>
        /// 已收录过同一个房间吗（地址 + 游戏端口）。
        /// </summary>
        /// <remarks>
        /// 同一次扫描可能收到同一个服务器的多条回包（广播 + 回环、以及补发的那次），
        /// 不去重的话列表里会出现几行一模一样的房间。
        /// </remarks>
        private bool Contains(in LanRoomInfo info)
        {
            for (var i = 0; i < m_Results.Count; i++)
            {
                if (m_Results[i].GamePort == info.GamePort
                    && string.Equals(m_Results[i].Address, info.Address, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
