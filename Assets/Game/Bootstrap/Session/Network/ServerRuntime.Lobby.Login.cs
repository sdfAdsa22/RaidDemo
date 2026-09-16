using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的大厅登录编排：把 PBKDF2 口令校验挪到后台线程（AR-07）。
    /// </summary>
    /// <remarks>
    /// <para><b>问题现场：</b>登录/建号在主线程同步做 10 万次 PBKDF2，云主机（2 核）
    /// 实测每次 0.68~0.72 秒停顿；NGO 心跳在秒级判定超时，于是同一房间的其它玩家
    /// 被踢成"掉线重连"（本地 4 人局甚至连续出现 8~11 次卡顿）。</para>
    ///
    /// <para><b>分工：</b><see cref="ServerIdentityStore.BeginLogin"/> 在主线程做便宜判断
    /// （格式、token 快速路径、取盐）；本文件把"算哈希"投给线程池，算完后
    /// 由主线程在 Update 里收尾（查表/定长比较/建号落盘/回包）。哈希是纯函数，
    /// 不碰 Unity 对象与账号表，因此跨线程是安全的。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>后台算完、等待主线程收尾的一次登录。</summary>
        private sealed class CompletedLoginHash
        {
            /// <summary>发起登录的连接编号（连接断开后按编号丢弃结果）。</summary>
            public int ClientId;

            /// <summary>第一步的结果（含昵称、盐、是否建号）。</summary>
            public ServerLoginAttempt Attempt;

            /// <summary>后台算出的哈希（出错时为空）。</summary>
            public string HashHex;

            /// <summary>后台耗时（毫秒，用于验收日志）。</summary>
            public long ElapsedMs;

            /// <summary>后台异常信息（正常时为空）。</summary>
            public string Error;
        }

        /// <summary>后台线程写、主线程读的完成队列。</summary>
        private readonly ConcurrentQueue<CompletedLoginHash> m_CompletedLoginHashes =
            new ConcurrentQueue<CompletedLoginHash>();

        /// <summary>把一次"需要算哈希"的登录投给线程池。</summary>
        private void EnqueuePendingLogin(LobbyClient client, ServerLoginAttempt attempt)
        {
            var clientId = client.ClientId;
            Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var hash = ServerIdentityStore.ComputeHashHex(
                        attempt.Passphrase, attempt.Salt, attempt.Iterations);
                    stopwatch.Stop();
                    m_CompletedLoginHashes.Enqueue(new CompletedLoginHash
                    {
                        ClientId = clientId,
                        Attempt = attempt,
                        HashHex = hash,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    });
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    m_CompletedLoginHashes.Enqueue(new CompletedLoginHash
                    {
                        ClientId = clientId,
                        Attempt = attempt,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                        Error = exception.Message,
                    });
                }
            });
        }

        /// <summary>
        /// 主线程每帧收尾：把后台算好的哈希接回登录流程。
        /// </summary>
        /// <remarks>
        /// 连接已经消失、或者这条连接已经登录过（例如客户端超时重发）时直接丢弃结果，
        /// 不做任何回包——回给一个不存在的连接只会在日志里制造噪声。
        /// </remarks>
        private void TickLoginJobs()
        {
            while (m_CompletedLoginHashes.TryDequeue(out var completed))
            {
                if (completed.Attempt == null
                    || !m_LobbyClients.TryGetValue(completed.ClientId, out var client)
                    || client.LoggedIn)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(completed.Error))
                {
                    m_Session?.Log.Warning(
                        $"[服务器] 口令校验失败（后台线程，{completed.ElapsedMs} ms）：{completed.Error}");
                    SendLobbyLoginFailure(
                        client,
                        completed.Attempt.Nickname,
                        LobbyError.BadSecret,
                        "服务器校验口令时出错，请重试。");
                    continue;
                }

                // 这条日志是 AR-07 的验收证据：哈希耗时与"后台线程"都写在里面，
                // 只要它出现在日志里，就说明登录没有再阻塞主线程。
                m_Session?.Log.Info($"[服务器] 口令校验 {completed.ElapsedMs} ms（后台线程）。");

                if (!m_Identities.CompleteLogin(
                        completed.Attempt,
                        completed.HashHex,
                        out var error,
                        out var detail,
                        out var token,
                        out var created))
                {
                    SendLobbyLoginFailure(client, completed.Attempt.Nickname, error, detail);
                    continue;
                }

                FinishLobbyLogin(client, completed.Attempt.Nickname, detail, token, created);
            }
        }

        /// <summary>登录失败：回包 + 服务器日志。</summary>
        private void SendLobbyLoginFailure(LobbyClient client, string nickname, LobbyError error, string detail)
        {
            var reason = string.IsNullOrEmpty(detail) ? DescribeLoginError(error) : detail;
            SendLobbyResult(client.ClientId, LobbyRequestKind.Login, false, error, reason);
            m_Session?.Log.Info(
                $"[服务器] 客户端 {client.ClientId} 登录失败（{(nickname ?? string.Empty).Trim()}）：{reason}");
        }

        /// <summary>登录成功后的共用收尾：宽限接管、在线校验、回包、进度与房间状态下发。</summary>
        private void FinishLobbyLogin(LobbyClient client, string nickname, string detail, string token, bool created)
        {
            var displayName = (nickname ?? string.Empty).Trim();

            // 重连接管（P5）：如果这个昵称正处在掉线宽限里，本次登录就是"回来接管自己"，
            // 而不是一次新的登录。必须排在"昵称是否在线"之前判断——宽限中的那条记录
            // 在名册上仍然是 LoggedIn，不区分的话会被判成"昵称已被占用"。
            var graced = FindGracedClient(displayName, client.ClientId, out var graceKey);
            if (graced != null)
            {
                client.Nickname = displayName;
                client.LoggedIn = true;

                if (TryResumeGracedSession(graced, graceKey, client))
                {
                    SendLobbyResult(
                        client.ClientId,
                        LobbyRequestKind.Login,
                        true,
                        LobbyError.None,
                        "已重连回原来的房间。",
                        token);
                    return;
                }

                // 接管失败（例如房间已经解散）：按普通登录继续走下面的流程。
                client.Nickname = null;
                client.LoggedIn = false;
            }

            if (IsNicknameOnline(displayName, client.ClientId))
            {
                SendLobbyResult(client.ClientId, LobbyRequestKind.Login, false,
                    LobbyError.NicknameOnline, "该昵称已在服务器上游戏中，请换一个昵称。");
                return;
            }

            client.Nickname = displayName;
            client.LoggedIn = true;

            SendLobbyResult(
                client.ClientId,
                LobbyRequestKind.Login,
                true,
                LobbyError.None,
                string.IsNullOrEmpty(detail) ? "登录成功。" : detail,
                token);

            m_Session?.Log.Info(
                $"[服务器] 玩家 {client.ClientId} 以「{displayName}」登录{(created ? "（新建账号）" : "（老账号）")}。");

            // 登录即把该账号的进度（金币 / 任务 / 随身装备）从服务端存档里取出来（P5）。
            // 仓库是房间级共享的，不在这里取。
            var profile = ResolveProfile(displayName);
            if (profile != null)
            {
                m_Session?.Log.Info(
                    $"[服务器] 账号「{displayName}」的进度已就绪：金币 {profile.Money}，"
                    + $"随身背包 {profile.Loadout.Backpack.Items.Count} 件，"
                    + $"共享仓库 {profile.Stash.Items.Count} 件。");
            }
            else
            {
                // 目录还没交接到（极早期登录）：不阻塞登录，进度会在后续操作里按需补上。
                m_Session?.Log.Warning(
                    $"[服务器] 账号「{displayName}」的进度暂不可用（物品目录或存档未就绪），稍后重试。");
            }

            // 登录成功后才点对点发房间状态：此时对方的处理器一定已经注册好了（P-20）。
            SendRoomStateTo(client.ClientId);
        }
    }
}
