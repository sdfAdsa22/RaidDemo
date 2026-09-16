using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using RaidDemo.Kernel;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器侧账号存储：昵称 → 盐 + 口令哈希 + token。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么口令不能明文落库：</b>服务器存档文件会被复制、备份、上传到云主机，
    /// 明文口令一旦泄露就是"玩家在别处也用了同一个口令"的连锁事故。
    /// 只存盐与哈希之后，拿到文件也推不出口令（PBKDF2 每次尝试都要付出一次完整计算）。</para>
    ///
    /// <para><b>为什么要 token：</b>每次连接都让玩家敲一遍口令，会让"重连"变成惩罚。
    /// 建号时发一个随机 token 给客户端保存，之后自动登录用它——token 可以随时在服务器侧换掉，
    /// 而口令不行（它在玩家脑子里）。</para>
    ///
    /// <para><b>它与游戏进度无关：</b>本类只回答"你是谁"。仓库、金币、任务的落库属于 P5，
    /// 那部分与联机存档的隔离策略一起设计，不能顺手塞进这里。</para>
    ///
    /// <para><b>线程约束（AR-07 起）：</b>账号表与落盘只在主线程访问；
    /// 唯一的例外是 <see cref="ComputeHashHex"/>——它是纯函数，被服务器编排层
    /// 放到后台线程执行，避免 10 万次 PBKDF2 阻塞主线程（那会让 NGO 心跳超时）。
    /// 登录因此拆成 <see cref="BeginLogin"/> / <see cref="CompleteLogin"/> 两段，
    /// <see cref="TryLogin"/> 保留为同步包装（测试与简单调用方使用）。</para>
    /// </remarks>
    public sealed partial class ServerIdentityStore
    {
        /// <summary>默认文件名（放在服务器的存档目录下）。</summary>
        public const string DefaultFileName = "accounts.json";

        /// <summary>
        /// PBKDF2 迭代次数。
        /// </summary>
        /// <remarks>
        /// 取值是"安全"与"登录延迟"的折中：10 万次在开发机上约 50~150 毫秒，
        /// 玩家感知不到，而暴力枚举的代价被抬高数个数量级。
        /// </remarks>
        public const int DefaultHashIterations = 100_000;

        /// <summary>盐的字节数（128 位足够，且每个账号一份）。</summary>
        private const int SaltBytes = 16;

        /// <summary>token 的随机字节数：16 字节 = 32 个十六进制字符。</summary>
        private const int TokenBytes = LobbyLimits.TokenHexLength / 2;

        /// <summary>落盘用的账号记录。</summary>
        /// <remarks>
        /// 它是对外协议之外的另一份契约：字段名进了玩家服务器上的存档文件。
        /// 改名等于让旧存档读不出来，因此只加字段、不改名、不删除。
        /// </remarks>
        [Serializable]
        private sealed class AccountRecord
        {
            /// <summary>昵称（唯一键）。</summary>
            public string Nickname = string.Empty;

            /// <summary>盐（Base64）。</summary>
            public string Salt = string.Empty;

            /// <summary>口令哈希（Base64）。</summary>
            public string Hash = string.Empty;

            /// <summary>自动登录令牌（32 个十六进制字符）。</summary>
            public string Token = string.Empty;

            /// <summary>生成这条哈希时用的迭代次数（将来提高迭代次数时，旧账号仍能登录）。</summary>
            public int Iterations = DefaultHashIterations;
        }

        /// <summary>存档文档根。</summary>
        [Serializable]
        private sealed class StoreDocument
        {
            /// <summary>全部账号。</summary>
            public List<AccountRecord> Accounts = new List<AccountRecord>();
        }

        private readonly Dictionary<string, AccountRecord> m_Accounts =
            new Dictionary<string, AccountRecord>(StringComparer.Ordinal);

        private readonly SaveFileStore m_File;

        /// <summary>已建号的数量。仅供日志与状态页展示。</summary>
        public int AccountCount => m_Accounts.Count;

        /// <summary>账号文件的完整路径（日志与测试断言用）。</summary>
        public string FilePath => m_File.FilePath;

        /// <summary>
        /// 创建账号存储并读取已有文件。
        /// </summary>
        /// <param name="directory">存档目录（服务器用启动参数里的 <c>-saveDir</c>）。</param>
        /// <param name="fileName">文件名，默认 <see cref="DefaultFileName"/>。</param>
        /// <remarks>
        /// 文件不存在或损坏都不算失败：那意味着"这台服务器上还没有任何账号"，
        /// 第一个登录的人会重新建号。真正的失败（例如目录只读）会在登录写盘时暴露出来。
        /// </remarks>
        public ServerIdentityStore(string directory, string fileName = DefaultFileName)
        {
            m_File = new SaveFileStore(directory, string.IsNullOrEmpty(fileName) ? DefaultFileName : fileName);
            Load();
        }

        /// <summary>
        /// 登录（首次＝建号）。
        /// </summary>
        /// <param name="nickname">昵称。</param>
        /// <param name="secret">口令（4~6 位数字）或已保存的 token（32 位十六进制）。</param>
        /// <param name="error">失败原因；成功时 <see cref="LobbyError.None"/>。</param>
        /// <param name="detail">给玩家看的中文说明（成功时说明这次是新建还是老账号）。</param>
        /// <param name="token">该账号的自动登录令牌（成功时有效）。</param>
        /// <param name="createdAccount">本次是否新建了账号。</param>
        /// <returns>登录成功返回 true。</returns>
        /// <remarks>
        /// <para><b>为什么 token 与口令走同一个入口：</b>客户端不必先问"我这个字符串是口令还是 token"，
        /// 服务器按格式自行判断（32 位十六进制＝token，其余按口令处理）。
        /// 判断只发生在服务器一侧，客户端界面因此可以只有一个输入框。</para>
        ///
        /// <para><b>口令错误为什么回"昵称被占用"：</b>若回"口令错误"，攻击者就能用这个接口
        /// 逐个试出"哪些昵称存在"。回"昵称已被占用，口令不匹配"把两种情况合并，
        /// 对玩家也更好懂：要么换昵称，要么把口令输对。</para>
        /// </remarks>
        public bool TryLogin(
            string nickname,
            string secret,
            out LobbyError error,
            out string detail,
            out string token,
            out bool createdAccount)
        {
            // 同步路径：给测试与"没有后台编排"的调用方用。
            // 服务器实际走 BeginLogin / CompleteLogin 两段式（见 ServerRuntime.Lobby.Login.cs）。
            var attempt = BeginLogin(nickname, secret);
            if (attempt.IsImmediate)
            {
                error = attempt.ImmediateError;
                detail = attempt.ImmediateDetail;
                token = attempt.ImmediateToken;
                createdAccount = attempt.ImmediateCreated;
                return attempt.ImmediateSuccess;
            }

            var hash = ComputeHashHex(attempt.Passphrase, attempt.Salt, attempt.Iterations);
            return CompleteLogin(attempt, hash, out error, out detail, out token, out createdAccount);
        }

        /// <summary>
        /// 登录第一步（主线程、无重计算）：做便宜的判断，或者给出后台算哈希的输入。
        /// </summary>
        /// <param name="nickname">昵称。</param>
        /// <param name="secret">口令或 token。</param>
        /// <returns>两段式登录的第一步结果。</returns>
        /// <remarks>
        /// 这一步不做任何 PBKDF2 计算，因此可以在登录请求到达的那一帧直接调用而不卡顿。
        /// 昵称不合法、token 快速路径、格式错误都会在这里直接给出结论。
        /// </remarks>
        public ServerLoginAttempt BeginLogin(string nickname, string secret)
        {
            if (!LobbyLimits.IsValidNickname(nickname))
            {
                return ServerLoginAttempt.Immediate(
                    null,
                    false,
                    LobbyError.BadNickname,
                    $"昵称需要 1~{LobbyLimits.MaxNicknameLength} 个字符，且不能包含竖线。");
            }

            var key = nickname.Trim();
            if (!m_Accounts.TryGetValue(key, out var account))
            {
                // 首次登录＝建号：必须给出合法口令，token 不可能凭空出现。
                if (!LobbyLimits.IsValidPassphrase(secret))
                {
                    return ServerLoginAttempt.Immediate(
                        key,
                        false,
                        LobbyError.BadPasswordFormat,
                        $"第一次使用昵称「{key}」需要设置 "
                        + $"{LobbyLimits.MinPassphraseDigits}~{LobbyLimits.MaxPassphraseDigits} 位数字口令。");
                }

                // 盐在这里生成：后台线程只做纯计算，不碰随机源与共享状态。
                return ServerLoginAttempt.PendingHash(
                    key, secret, RandomBytes(SaltBytes), DefaultHashIterations, creatingAccount: true);
            }

            if (LobbyLimits.IsValidToken(secret))
            {
                if (string.Equals(account.Token, secret, StringComparison.OrdinalIgnoreCase))
                {
                    return ServerLoginAttempt.Immediate(
                        key, true, LobbyError.None, $"欢迎回来，{key}。", account.Token);
                }

                return ServerLoginAttempt.Immediate(
                    key, false, LobbyError.BadSecret, "登录令牌已失效，请输入口令重新登录。");
            }

            if (!LobbyLimits.IsValidPassphrase(secret))
            {
                return ServerLoginAttempt.Immediate(
                    key,
                    false,
                    LobbyError.BadPasswordFormat,
                    $"口令需要 {LobbyLimits.MinPassphraseDigits}~{LobbyLimits.MaxPassphraseDigits} 位数字，"
                    + "或直接回车使用上次保存的登录令牌。");
            }

            byte[] salt;
            try
            {
                salt = Convert.FromBase64String(account.Salt);
            }
            catch (FormatException)
            {
                // 存档里的盐坏了：按"口令不正确"处理，避免把内部状态暴露给客户端。
                return ServerLoginAttempt.Immediate(
                    key, false, LobbyError.NicknameTaken, DescribeWrongPassphrase(key));
            }

            var iterations = account.Iterations > 0 ? account.Iterations : DefaultHashIterations;
            return ServerLoginAttempt.PendingHash(key, secret, salt, iterations, creatingAccount: false);
        }

        /// <summary>
        /// 登录第二步（主线程）：用后台算好的哈希收尾。
        /// </summary>
        /// <param name="attempt">第一步的结果（必须是 PendingHash 形态）。</param>
        /// <param name="hashHex">后台线程算出的哈希（十六进制小写）。</param>
        /// <param name="error">失败原因。</param>
        /// <param name="detail">给玩家看的说明。</param>
        /// <param name="token">自动登录令牌（成功时）。</param>
        /// <param name="createdAccount">本次是否新建了账号。</param>
        /// <returns>登录成功返回 true。</returns>
        public bool CompleteLogin(
            ServerLoginAttempt attempt,
            string hashHex,
            out LobbyError error,
            out string detail,
            out string token,
            out bool createdAccount)
        {
            error = LobbyError.None;
            detail = null;
            token = null;
            createdAccount = false;

            if (attempt == null || attempt.IsImmediate)
            {
                error = LobbyError.BadSecret;
                detail = "登录流程状态异常，请重试。";
                return false;
            }

            var key = attempt.Nickname;
            if (attempt.CreatingAccount)
            {
                var account = CreateAccount(key, attempt.Salt, hashHex);
                m_Accounts[key] = account;
                createdAccount = true;
                token = account.Token;
                detail = $"已用昵称「{key}」建号，下次可自动登录。";

                if (!TrySave(out var saveError))
                {
                    // 写盘失败仍然让本次登录成立（账号在内存里），但要说清楚：
                    // 否则玩家下次启动发现"账号没了"，而日志里什么线索都没有。
                    detail += $"（账号文件写入失败：{saveError}）";
                }

                return true;
            }

            if (!m_Accounts.TryGetValue(key, out var existing)
                || !FixedTimeEquals(hashHex, existing.Hash))
            {
                error = LobbyError.NicknameTaken;
                detail = DescribeWrongPassphrase(key);
                return false;
            }

            token = existing.Token;
            detail = $"欢迎回来，{key}。";
            return true;
        }

        /// <summary>口令错误时给玩家的统一说明（不区分"账号不存在"与"口令不对"，防止昵称枚举）。</summary>
        private static string DescribeWrongPassphrase(string key)
        {
            return $"昵称「{key}」已被占用：口令不正确。请换个昵称，或输入正确口令。";
        }

        /// <summary>把账号写入磁盘。失败时返回 false 并给出原因。</summary>
        /// <param name="error">失败原因；成功时为 null。</param>
        public bool TrySave(out string error)
        {
            var document = new StoreDocument();
            foreach (var pair in m_Accounts)
            {
                document.Accounts.Add(pair.Value);
            }

            return m_File.Save(document, out error);
        }

        /// <summary>
        /// 计算口令哈希（十六进制小写）。
        /// </summary>
        /// <param name="passphrase">口令。</param>
        /// <param name="salt">盐。</param>
        /// <param name="iterations">迭代次数。</param>
        /// <remarks>公开给测试：它需要在不落盘的情况下验证"同一口令 + 同一盐 = 同一结果"。</remarks>
        public static string ComputeHashHex(string passphrase, byte[] salt, int iterations)
        {
            using var derive = new Rfc2898DeriveBytes(
                passphrase ?? string.Empty,
                salt,
                iterations,
                HashAlgorithmName.SHA256);
            return ToHex(derive.GetBytes(32));
        }

        /// <summary>读取账号文件；不存在或损坏时按"空账号表"处理。</summary>
        private void Load()
        {
            if (!m_File.Exists)
            {
                return;
            }

            if (!m_File.TryLoad<StoreDocument>(out var document, out var error) || document?.Accounts == null)
            {
                UnityEngine.Debug.LogWarning($"[服务器] 账号文件读取失败，将以空账号表启动：{error}");
                return;
            }

            for (var i = 0; i < document.Accounts.Count; i++)
            {
                var account = document.Accounts[i];
                if (account == null || string.IsNullOrEmpty(account.Nickname))
                {
                    continue;
                }

                if (account.Iterations <= 0)
                {
                    account.Iterations = DefaultHashIterations;
                }

                m_Accounts[account.Nickname] = account;
            }
        }

        /// <summary>用给定的盐与哈希生成一条新账号记录（哈希由调用方在后台算好）。</summary>
        private static AccountRecord CreateAccount(string nickname, byte[] salt, string hashHex)
        {
            return new AccountRecord
            {
                Nickname = nickname,
                Salt = Convert.ToBase64String(salt),
                Hash = hashHex,
                Token = ToHex(RandomBytes(TokenBytes)),
                Iterations = DefaultHashIterations,
            };
        }

    }
}
