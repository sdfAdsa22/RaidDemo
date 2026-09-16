namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 两段式登录的第一步结果（AR-07）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把登录拆成两段：</b>口令校验用 PBKDF2（10 万次迭代），
    /// 在服务器主线程上同步执行会造成 0.7 秒级卡顿——足以让 NGO 心跳超时、
    /// 把同房间的其它玩家踢成"掉线"（云主机 2 人局实测 0.68~0.72 秒）。
    /// 拆成"主线程做便宜判断 → 后台线程算哈希 → 主线程收尾"之后，主线程不再被哈希阻塞。</para>
    ///
    /// <para><b>两种形态：</b>要么结论已经确定（昵称不合法 / token 快速路径 / 格式错误），
    /// 要么给出后台算哈希所需的全部输入（昵称、口令、盐、迭代次数、是否建号）。
    /// 它不持有任何会话状态，可以安全地跨线程传递。</para>
    /// </remarks>
    public sealed class ServerLoginAttempt
    {
        /// <summary>结论是否已确定（无需后台计算）。</summary>
        public bool IsImmediate { get; private set; }

        /// <summary>立即结论：是否登录成功。</summary>
        public bool ImmediateSuccess { get; private set; }

        /// <summary>立即结论：失败原因。</summary>
        public LobbyError ImmediateError { get; private set; }

        /// <summary>立即结论：给玩家看的说明。</summary>
        public string ImmediateDetail { get; private set; }

        /// <summary>立即结论：自动登录令牌（成功时）。</summary>
        public string ImmediateToken { get; private set; }

        /// <summary>立即结论：本次是否新建了账号。</summary>
        public bool ImmediateCreated { get; private set; }

        /// <summary>昵称（已 Trim；两种形态都有效）。</summary>
        public string Nickname { get; private set; }

        /// <summary>待校验的口令（需要后台哈希时有效）。</summary>
        public string Passphrase { get; private set; }

        /// <summary>盐（需要后台哈希时有效）。</summary>
        public byte[] Salt { get; private set; }

        /// <summary>迭代次数（需要后台哈希时有效）。</summary>
        public int Iterations { get; private set; }

        /// <summary>本次是不是"首次登录＝建号"（建号时哈希结果直接成为账号的哈希）。</summary>
        public bool CreatingAccount { get; private set; }

        /// <summary>构造一个"结论已确定"的尝试。</summary>
        internal static ServerLoginAttempt Immediate(
            string nickname,
            bool success,
            LobbyError error,
            string detail,
            string token = null,
            bool created = false)
        {
            return new ServerLoginAttempt
            {
                IsImmediate = true,
                ImmediateSuccess = success,
                ImmediateError = error,
                ImmediateDetail = detail,
                ImmediateToken = token,
                ImmediateCreated = created,
                Nickname = nickname,
            };
        }

        /// <summary>构造一个"需要后台算哈希"的尝试。</summary>
        internal static ServerLoginAttempt PendingHash(
            string nickname,
            string passphrase,
            byte[] salt,
            int iterations,
            bool creatingAccount)
        {
            return new ServerLoginAttempt
            {
                IsImmediate = false,
                Nickname = nickname,
                Passphrase = passphrase,
                Salt = salt,
                Iterations = iterations,
                CreatingAccount = creatingAccount,
            };
        }
    }
}
