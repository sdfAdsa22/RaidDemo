using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 把服务器监听地址整理成「房主可以直接念给别人听」的文本。
    /// </summary>
    /// <remarks>
    /// <para>服务器启动后要回答的第一个问题是「别人该连哪里」。因此这里同时给出两个地址：
    /// <c>127.0.0.1</c>（同机测试）与第一个可用的局域网 IPv4（同网段联机）。
    /// 云主机还要额外告诉玩家公网 IP，而公网 IP 无法从进程内可靠推断，因此只提示、不猜测。</para>
    ///
    /// <para>全部读取失败时返回空列表而不是抛异常：取不到网卡不该让服务器起不来。</para>
    /// </remarks>
    public static class ServerAddressReporter
    {
        /// <summary>本机回环地址，供同机多开测试使用。</summary>
        public const string LoopbackAddress = "127.0.0.1";

        /// <summary>
        /// 生成多行监听地址报告。
        /// </summary>
        /// <param name="port">监听端口。</param>
        public static string BuildReport(int port)
        {
            var builder = new StringBuilder();
            builder.Append("监听地址：");
            builder.Append("\n  本机    ").Append(LoopbackAddress).Append(':').Append(port);

            var lan = EnumerateLanIPv4();
            if (lan.Count == 0)
            {
                builder.Append("\n  局域网  （未检测到可用网卡；云主机请填写公网 IP）");
            }
            else
            {
                foreach (var address in lan)
                {
                    builder.Append("\n  局域网  ").Append(address).Append(':').Append(port);
                }
            }

            builder.Append("\n（云主机部署时，请把上面的地址换成实例的公网 IP）");
            return builder.ToString();
        }

        /// <summary>
        /// 枚举本机可用的局域网 IPv4 地址。
        /// </summary>
        /// <remarks>
        /// <para>过滤规则：网卡处于 Up、非回环、非隧道、已分配 IPv4 单播地址。</para>
        ///
        /// <para><b>排序规则：有默认网关的网卡排前面。</b>开发机上常见 VPN、虚拟机、Docker 等虚拟网卡，
        /// 它们也有 IPv4 地址但不是同网段玩家能连上的入口。第一行地址通常是房主直接念给别人的那个，
        /// 因此让「有网关的物理网卡」优先，避免把 VPN 地址报到最前面。</para>
        /// </remarks>
        public static IReadOnlyList<string> EnumerateLanIPv4()
        {
            var preferred = new List<string>();
            var others = new List<string>();

            NetworkInterface[] interfaces;
            try
            {
                interfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch
            {
                // 取不到网卡列表时返回空集合：服务器应当照常启动，只是报不出局域网地址。
                return preferred;
            }

            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties properties;
                try
                {
                    properties = nic.GetIPProperties();
                }
                catch
                {
                    continue;
                }

                var hasGateway = properties.GatewayAddresses != null && properties.GatewayAddresses.Count > 0;
                var bucket = hasGateway ? preferred : others;

                foreach (var info in properties.UnicastAddresses)
                {
                    var address = info.Address;
                    if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var text = address.ToString();
                    if (text == LoopbackAddress || preferred.Contains(text) || others.Contains(text))
                    {
                        continue;
                    }

                    var ip = IPAddress.Parse(text);
                    if (IPAddress.IsLoopback(ip))
                    {
                        continue;
                    }

                    bucket.Add(text);
                }
            }

            preferred.AddRange(others);
            return preferred;
        }
    }
}
