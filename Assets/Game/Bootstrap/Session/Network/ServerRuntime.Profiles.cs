using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Meta;
using RaidDemo.Raid;
using Unity.Collections;
using Unity.Netcode;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的进度存档部分（P5）：共享仓库的注册与账号进度的取用。
    /// </summary>
    /// <remarks>
    /// <para><b>三条边界：</b></para>
    /// <list type="number">
    /// <item><description><b>读盘要等物品目录</b>：账号进度里存的是物品 ID，没有目录就无法还原，
    /// 因此 <see cref="ServerProfileStore.Configure"/> 在目录到位前一直返回 false，
    /// 本类每帧重试一次；</description></item>
    /// <item><description><b>共享仓库要注册进容器注册表</b>：客户端在安全屋里拖动的
    /// "3 号容器"就是它，注册之后既有的命令通道与内容下发全部照旧工作；</description></item>
    /// <item><description><b>落盘是节流 + 关键点立即写</b>：拖动物品标脏、节拍刷新；
    /// 撤离结算与关服各立刻写一次（那两次的钱与物品必须落定）。</description></item>
    /// </list>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>服务端进度存档（共享仓库 + 账号进度）。</summary>
        private ServerProfileStore m_Profiles;

        /// <summary>共享仓库是否已经注册进容器注册表。</summary>
        private bool m_SharedStashRegistered;

        /// <summary>读盘问题是否已经打过日志（只打一次，避免每帧刷屏）。</summary>
        private bool m_ProfileProblemsReported;

        /// <summary>服务端进度存档；未初始化时为 null。</summary>
        internal ServerProfileStore Profiles
        {
            get { return m_Profiles; }
        }

        /// <summary>
        /// 每帧推进：目录到位后读盘、注册共享仓库、按节拍落盘。
        /// </summary>
        private void TickProfiles(float deltaTime)
        {
            if (m_Profiles == null)
            {
                return;
            }

            if (!m_Profiles.IsReady && !m_Profiles.Configure(ServerMode.SceneItemCatalog))
            {
                return;
            }

            RegisterSharedStashIfNeeded();
            ReportProfileProblemsOnce();
            m_Profiles.Tick(deltaTime);
        }

        /// <summary>
        /// 把房间共享仓库注册进容器注册表（只做一次）。
        /// </summary>
        /// <remarks>
        /// 编号用 <see cref="ContainerIds.ServerSharedStash"/>（5），刻意落在场景容器段（100~999）
        /// 之外：切图时会整段注销场景容器，共享仓库显然不该跟着地图一起消失。
        /// </remarks>
        private void RegisterSharedStashIfNeeded()
        {
            if (m_SharedStashRegistered || m_Containers == null)
            {
                return;
            }

            var registered = m_Containers.Register(
                m_Profiles.SharedStash, ContainerKind.Stash, ContainerIds.ServerSharedStash);
            if (registered == 0)
            {
                // 已经注册过（例如热重载）：不重复登记，但也不要每帧重试。
                m_SharedStashRegistered = true;
                return;
            }

            m_SharedStashRegistered = true;
            m_Session?.Log.Info(
                $"[服务器] 房间共享仓库已就绪：{m_Profiles.SharedStash.Items.Count} 件物品"
                + $"（存档 {m_Profiles.FilePath}）。");
        }

        /// <summary>读盘问题汇总成一条日志。</summary>
        private void ReportProfileProblemsOnce()
        {
            if (m_ProfileProblemsReported)
            {
                return;
            }

            m_ProfileProblemsReported = true;

            var problems = m_Profiles.LoadProblems;
            for (var i = 0; i < problems.Count; i++)
            {
                m_Session?.Log.Warning("[服务器] 进度存档：" + problems[i]);
            }
        }

        /// <summary>
        /// 取某个账号的进度（必要时新建）。
        /// </summary>
        /// <param name="nickname">账号昵称。</param>
        /// <returns>进度对象；存档尚未就绪时返回 null。</returns>
        internal MetaProgress ResolveProfile(string nickname)
        {
            if (m_Profiles == null)
            {
                return null;
            }

            if (!m_Profiles.IsReady && !m_Profiles.Configure(ServerMode.SceneItemCatalog))
            {
                return null;
            }

            return m_Profiles.GetOrCreate(nickname);
        }

        /// <summary>标记进度有改动（节流落盘）。</summary>
        internal void MarkProfilesDirty()
        {
            m_Profiles?.MarkDirty();
        }

        /// <summary>立刻把进度写盘（结算、关服）。</summary>
        internal void FlushProfiles()
        {
            m_Profiles?.SaveAll();
        }

        /// <summary>
        /// 按玩家编号取他所在账号的进度。
        /// </summary>
        /// <param name="playerId">玩家编号（= 连接编号）。</param>
        /// <returns>进度对象；未登录 / 存档未就绪时返回 null。</returns>
        /// <remarks>
        /// 玩家编号与账号的联系在大厅侧：登录时把昵称记在连接上（<c>LobbyClient.Nickname</c>），
        /// 这里做一次翻译。因此**任何与账号有关的判定都要经过登录**——
        /// 这也正是"进度与身份绑在一起"的应有形态。
        /// </remarks>
        internal MetaProgress ResolveProfileForPlayer(int playerId)
        {
            if (!m_LobbyClients.TryGetValue(playerId, out var client)
                || string.IsNullOrEmpty(client.Nickname))
            {
                return null;
            }

            return ResolveProfile(client.Nickname);
        }

        /// <summary>
        /// 把金币等进度摘要发给这名玩家本人。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="reason"><see cref="ProfileStateReasons"/> 中的一个。</param>
        /// <remarks>物品那一半走容器批次，这里只发"客户端画不出来的东西"（金币）。</remarks>
        internal void SendProfileStateTo(int playerId, byte reason)
        {
            var manager = m_Network;
            var profile = ResolveProfileForPlayer(playerId);
            if (manager == null || manager.CustomMessagingManager == null || profile == null)
            {
                return;
            }

            if (!IsClientConnected((ulong)playerId))
            {
                return;
            }

            var message = new ProfileStateMessage { Money = profile.Money, Reason = reason };
            using (var writer = new FastBufferWriter(16, Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(
                    ProfileNetworkChannel.StateMessageName,
                    (ulong)playerId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            m_Session?.Log.Info($"[服务器] 已下发玩家 {playerId} 的进度：金币 {profile.Money}。");
        }

        /// <summary>
        /// 把一局的结果落到账号进度上（P5 的"撤离结算落库"）。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <param name="outcome">战局结果。</param>
        /// <param name="carriedValue">带出 / 损失的价值（任务判定用）。</param>
        /// <remarks>
        /// <para><b>规则与单机完全一致</b>（M6 批次 2 定稿、本批次搬到服务器执行）：</para>
        /// <list type="bullet">
        /// <item><description>撤离成功 → 随身装备与背包里的东西<strong>全部入共享仓库</strong>，
        /// 并推进"撤离类"任务；</description></item>
        /// <item><description>阵亡 / 超时 → 随身的东西<strong>直接丢弃</strong>，仓库不受影响。</description></item>
        /// </list>
        ///
        /// <para><b>为什么结算后立刻写盘：</b>这一笔是"跨局资产"的边界——
        /// 服务器若在写入前崩溃，玩家会看到"装备没了、仓库也没有"。节流落盘只用于
        /// 安全屋里的日常拖动，结算必须落定。</para>
        /// </remarks>
        internal void ApplyOutcomeToProfile(int playerId, RaidOutcome outcome, int carriedValue)
        {
            var profile = ResolveProfileForPlayer(playerId);
            if (profile == null)
            {
                m_Session?.Log.Warning(
                    $"[服务器] 玩家 {playerId} 结算时没有账号进度：结果不会落库（只影响这一局）。");
                return;
            }

            if (outcome == RaidOutcome.Extracted)
            {
                var deposited = profile.DepositLoadoutToStash();
                if (profile.LastDepositFailures > 0)
                {
                    m_Session?.Log.Warning(
                        $"[服务器] 玩家 {playerId} 撤离时共享仓库放不下："
                        + $"{profile.LastDepositFailures} 件物品未能入库。");
                }

                // 只有活着带出来才算任务进度（与单机同一条规则）。
                profile.Quests.ReportExtraction(carriedValue, TookDamageThisRaid(playerId));

                m_Session?.Log.Info(
                    $"[服务器] 玩家 {playerId} 撤离入库：{deposited} 件物品，"
                    + $"共享仓库现有 {profile.Stash.Items.Count} 件，金币 {profile.Money}。");
            }
            else
            {
                // 阵亡与超时同档：随身的东西没了，仓库绝对安全。
                profile.ClearLoadout();
                m_Session?.Log.Info($"[服务器] 玩家 {playerId} 阵亡：随身携带物已清空（仓库不受影响）。");
            }

            // 结算的账必须立刻落定，然后把这个账号的新余额发回客户端。
            FlushProfiles();
            SendProfileStateTo(
                playerId,
                outcome == RaidOutcome.Extracted ? ProfileStateReasons.Extracted : ProfileStateReasons.Killed);

            // P5.5：撤离任务进度也是在这一刻推进的（ReportExtraction），把任务快照一并发回，
            // 否则玩家回到安全屋后打开任务面板看到的还是战局前的状态。
            SendQuestStateTo(playerId);
        }

        /// <summary>本局这名玩家是否挨过打（任务里的"无伤撤离"判定）。</summary>
        private bool TookDamageThisRaid(int playerId)
        {
            return m_Combat != null && m_Combat.PlayerTookDamage(playerId);
        }
    }
}
