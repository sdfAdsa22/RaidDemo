namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的两个"外围服务"：状态页（Dashboard）与局域网发现应答。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要单独一层：</b>它们都不参与游戏规则——一个给运维看，一个给玩家省一次手输 IP。
    /// 两者失败都必须只降级、不影响联机本身（状态页端口被占用、网卡不允许广播都很常见），
    /// 因此放在同一个文件里统一处理"启动失败就记一行日志"这条策略。</para>
    ///
    /// <para><b>数据来源是同一份状态快照</b>（<c>ServerRuntime.Status.cs</c>）：
    /// 状态页要全量快照，发现只取房间公告那一小块。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        private ServerDashboard m_Dashboard;
        private LanDiscoveryResponder m_LanResponder;

        /// <summary>启动状态页与局域网发现（由大厅初始化调用）。</summary>
        private void InitializeServerIntegrations()
        {
            m_Dashboard = gameObject.AddComponent<ServerDashboard>();
            m_Dashboard.Initialize(
                m_Options.DashboardPort,
                BuildStatusSnapshot,
                HandleDashboardAction,
                m_Options.AdminToken);

            if (m_Options.DiscoveryPort > 0)
            {
                m_LanResponder = new LanDiscoveryResponder(
                    m_Options.DiscoveryPort,
                    BuildRoomAnnouncement,
                    message => m_Session?.Log.Info(message));
                m_LanResponder.Start();
            }
            else
            {
                m_Session?.Log.Info("[服务器] 局域网发现已关闭（-discoveryPort 0）：仍可手输地址加入。");
            }
        }

        /// <summary>关闭两个外围服务（可重复调用）。</summary>
        private void ShutdownServerIntegrations()
        {
            m_LanResponder?.Dispose();
            m_LanResponder = null;

            if (m_Dashboard != null)
            {
                m_Dashboard.Shutdown();
                m_Dashboard = null;
            }
        }

        /// <summary>每帧驱动"需要轮询"的外围服务（目前只有发现应答器）。</summary>
        /// <remarks>
        /// 状态页自己是 MonoBehaviour、由引擎驱动 Update；发现应答器是普通对象，
        /// 必须由这里驱动——漏掉这一步的症状是"服务器说发现已启用，但客户端怎么扫都扫不到"。
        /// </remarks>
        private void TickServerIntegrations()
        {
            m_LanResponder?.Poll();
        }

        /// <summary>状态页请求的运维动作；返回 null 表示成功。</summary>
        /// <param name="action">动作名（见 <see cref="DashboardRouter"/> 的常量）。</param>
        /// <param name="argument">动作参数（踢人时是目标玩家编号；其它动作忽略）。</param>
        /// <remarks>
        /// 权限判断在路由层（本机来源或口令正确），这里只负责执行：
        /// 把"能不能做"和"做什么"分开，安全边界就只有一处需要审查。
        /// </remarks>
        private string HandleDashboardAction(string action, string argument)
        {
            switch (action)
            {
                case DashboardRouter.StopRoomAction:
                    StopRoomFromDashboard();
                    return null;

                case DashboardRouter.KickPlayerAction:
                    return int.TryParse(argument, out var clientId)
                        ? KickPlayerFromDashboard(clientId)
                        : "缺少或非法的玩家编号。";

                case DashboardRouter.StopServerAction:
                    StopServerFromDashboard();
                    return null;

                default:
                    return $"未知动作：{action}";
            }
        }
    }
}
