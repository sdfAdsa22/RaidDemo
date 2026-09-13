using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 客户端与服务器共用的网络配置。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须只有一份：</b>NGO 在握手时会比对双方的 <see cref="NetworkConfig"/>，
    /// 不一致就直接断开，并且只在 <c>ConnectionRequestMessage.Deserialize</c> 里留一条
    /// 「NetworkConfig mismatch」的警告——症状是「客户端一直连不上、服务器显示在线 0 人」，
    /// 很难从现象反推。把配置收成一个工厂函数之后，两端不可能再各写一套。</para>
    ///
    /// <para><b>具体踩过的坑：</b>服务器写了 <c>TickRate = 60</c>、客户端用默认值 30，
    /// 于是每条连接请求都在服务器侧被判为配置不匹配并断开，
    /// 客户端只表现为「连不上」。见 M9 排障手册的对应条目。</para>
    ///
    /// <para>新增配置项时的规矩：<b>只在这里加</b>，两端自动一致；
    /// 若某项确实要分角色（例如只有服务器需要的连接审批），也要显式标注原因。</para>
    /// </remarks>
    public static class NetworkConfigFactory
    {
        /// <summary>仿真频率（Hz）。与移动权威世界的固定步长保持一致。</summary>
        public const uint TickRate = 60;

        /// <summary>创建两端一致的网络配置。</summary>
        /// <param name="transport">传输层组件。</param>
        public static NetworkConfig Create(NetworkTransport transport)
        {
            return new NetworkConfig
            {
                NetworkTransport = transport,
                TickRate = TickRate,

                // P0 不做入房审批：密码校验与账号登录排在 P4。
                // 人数上限（2~4 人）届时由审批回调拒绝超额连接。
                ConnectionApproval = false,

                // 场景由两端各自加载（地图由构建器生成），不需要引擎同步场景事件。
                EnableSceneManagement = false,
            };
        }
    }
}
