using System;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// "全员静默"看门狗：判断服务器的**上行接收路径**是不是整体哑了（排障手册 P-51 的应用层兜底）。
    /// </summary>
    /// <remarks>
    /// <para><b>要解决的问题（P-51）：</b>某个客户端进程被强制结束之后，服务器会在 1 秒内
    /// <b>收不到任何一个客户端的上行</b>——不是某个人掉线，而是整条接收链失效。服务器侧的表现是：
    /// 发得出去（无发送错误）、主线程没卡（看门狗全绿）、却收不进来，于是 10 秒后把所有人
    /// 一起按 <c>ProtocolTimeout</c> 踢掉。玩家看到的是"队友掉了，我也被踢，房间解散"。</para>
    ///
    /// <para><b>为什么判据是"全员静默"而不是"某个人静默"：</b>正常游玩时，每个人以 60 Hz 上行，
    /// 单人静默有很多无害的解释（加载、GC、窗口被挂起）；而"所有在线的人同时超过两秒没有一个包"
    /// 在正常游玩里不可能发生。把阈值放在"全员"这一侧，就不必去猜单人停顿的原因。</para>
    ///
    /// <para><b>为什么还要"世界里有人"这个前提：</b>大厅里等人、切界面时本来就没有上行，
    /// 只看"静默"会把正常挂机误判成故障、反复重建传输层。权威世界里有人就意味着
    /// "应当有 60 Hz 的上行"，静默才有意义。</para>
    ///
    /// <para><b>为什么要有冷却：</b>重建本身要几百毫秒到几秒（关闭、重新绑定端口、等客户端回来），
    /// 这段时间里"全员静默"依然成立。没有冷却的话看门狗会在重建过程中反复触发，
    /// 把一次自愈变成一串无效的重启。</para>
    ///
    /// <para><b>本类是纯逻辑</b>：只吃时间戳与三个布尔量，不碰网络，因此各种时序都能在 EditMode 里钉死
    /// （见 <c>Tests/EditMode/P51TransportWatchdogTests.cs</c>）。真正的重建动作在
    /// <c>ServerRuntime.TransportWatchdog.cs</c>。</para>
    /// </remarks>
    public sealed class TransportWatchdog
    {
        /// <summary>
        /// 默认的"全员静默"判定时长（秒）。
        /// </summary>
        /// <remarks>
        /// <para>取 2.5 秒：它必须明显大于"某个人短暂停顿"的量级（0.6 秒的加载停顿、1 秒的网络抖动），
        /// 又必须明显小于 NGO 的协议超时窗口（约 10 秒）——等到服务器按超时把人踢掉，
        /// 玩家的背包与位置就已经进了宽限流程，体验上从"卡一下恢复"变成"被踢了"。</para>
        ///
        /// <para>换句话说，2.5 秒是"足够确信"与"来得及自愈"之间的中点。</para>
        /// </remarks>
        public const float DefaultAllSilentSeconds = 2.5f;

        /// <summary>判定时长的下界（秒）。低于它，一次普通抖动就会被当成接收路径失效。</summary>
        public const float MinAllSilentSeconds = 1f;

        /// <summary>判定时长的上界（秒）。高于它，服务器会先撞上 NGO 的协议超时。</summary>
        public const float MaxAllSilentSeconds = 30f;

        /// <summary>两次重建之间的最小间隔（秒）。</summary>
        public const float DefaultRebuildCooldownSeconds = 15f;

        private readonly float m_AllSilentSeconds;
        private readonly float m_CooldownSeconds;

        /// <summary>本轮"全员静默"的起始时刻；&lt; 0 表示当前不处于静默观察中。</summary>
        private float m_AllSilentSince = -1f;

        /// <summary>冷却结束时刻；&lt; 0 表示从未重建过。</summary>
        private float m_CooldownUntil = -1f;

        /// <summary>
        /// 创建一个看门狗。
        /// </summary>
        /// <param name="allSilentSeconds">判定时长（秒），取值会被夹到上下界之间。</param>
        /// <param name="rebuildCooldownSeconds">两次重建之间的最小间隔（秒）。</param>
        public TransportWatchdog(
            float allSilentSeconds = DefaultAllSilentSeconds,
            float rebuildCooldownSeconds = DefaultRebuildCooldownSeconds)
        {
            m_AllSilentSeconds = Clamp(allSilentSeconds, MinAllSilentSeconds, MaxAllSilentSeconds);
            m_CooldownSeconds = rebuildCooldownSeconds > 0f
                ? rebuildCooldownSeconds
                : DefaultRebuildCooldownSeconds;
        }

        /// <summary>当前生效的判定时长（秒）。</summary>
        public float AllSilentSeconds => m_AllSilentSeconds;

        /// <summary>是否已经进入"全员静默"的观察窗口（供日志判断"这是刚开始静默还是仍在静默"）。</summary>
        public bool IsObservingSilence => m_AllSilentSince >= 0f;

        /// <summary>本次静默已经持续了多久（秒）；不在观察窗口内时为 0。</summary>
        /// <param name="now">当前时刻（<c>Time.realtimeSinceStartup</c>）。</param>
        public float SilentSeconds(float now)
        {
            return m_AllSilentSince < 0f ? 0f : Math.Max(0f, now - m_AllSilentSince);
        }

        /// <summary>
        /// 每帧投喂一次观察结果：现在是否该重建传输层。
        /// </summary>
        /// <param name="worldHasPlayers">权威世界里是否还有人（意味着"应当"有 60 Hz 上行）。</param>
        /// <param name="hasLiveClient">是否还有"已连接且不在宽限里"的客户端。</param>
        /// <param name="anyLiveClientHeardFrom">
        /// 是否有客户端在静默窗口内说过话。**刚连上还没说过话的客户端也算"说过话"**——
        /// 他正在加载场景，此时抑制判定，否则服务器会在每个客户端进图的那几秒重建传输层。
        /// </param>
        /// <param name="now">当前时刻（<c>Time.realtimeSinceStartup</c>）。</param>
        /// <returns>满足"全员静默且已过判定时长"时返回 true；调用方随即应调用 <see cref="NoteRebuildStarted"/>。</returns>
        /// <remarks>
        /// <para>本方法只判定、不改变"冷却"，因此可以在同一帧里被安全地重复调用（例如诊断代码再问一次
        /// "现在到底静默了多久"）。真正开始重建时必须调用 <see cref="NoteRebuildStarted"/>，
        /// 否则同一轮静默会在下一帧继续返回 true。</para>
        /// </remarks>
        public bool ShouldRebuildTransport(
            bool worldHasPlayers,
            bool hasLiveClient,
            bool anyLiveClientHeardFrom,
            float now)
        {
            // 没有人"应当"上行：不进观察窗口，也不残留上一轮的起始时刻。
            if (!worldHasPlayers || !hasLiveClient)
            {
                m_AllSilentSince = -1f;
                return false;
            }

            if (anyLiveClientHeardFrom)
            {
                // 只要还有人在说话，接收路径就是好的。清掉观察窗口：
                // 下一次静默要重新数满判定时长，而不是接着上一次的进度。
                m_AllSilentSince = -1f;
                return false;
            }

            if (m_CooldownUntil > 0f && now < m_CooldownUntil)
            {
                // 冷却期内不判定：上一步重建的收尾（客户端正在回来）本来就伴随着静默。
                m_AllSilentSince = -1f;
                return false;
            }

            if (m_AllSilentSince < 0f)
            {
                m_AllSilentSince = now;
                return false;
            }

            return now - m_AllSilentSince >= m_AllSilentSeconds;
        }

        /// <summary>
        /// 记下"重建已经开始"：清掉本轮静默并进入冷却。
        /// </summary>
        /// <param name="now">当前时刻（<c>Time.realtimeSinceStartup</c>）。</param>
        public void NoteRebuildStarted(float now)
        {
            m_AllSilentSince = -1f;
            m_CooldownUntil = now + m_CooldownSeconds;
        }

        /// <summary>
        /// 空房场景的判定：没有任何在线连接、但有人在宽限里等待重连时，也把传输层重建一次。
        /// </summary>
        /// <param name="waitingForReconnect">是否有人在掉线宽限中等待重连。</param>
        /// <param name="now">当前时刻（<c>Time.realtimeSinceStartup</c>）。</param>
        /// <returns>应当重建返回 true；调用方随即应调用 <see cref="NoteRebuildStarted"/>。</returns>
        /// <remarks>
        /// <para><b>为什么要这个场景：</b>P-51 的实测里有一条关键读数——接收路径被打哑之后，
        /// <b>新客户端也连不上</b>（握手包同样收不到）。也就是说"所有人都掉线、正在宽限里等重连"时，
        /// 如果不重建套接字，他们会在宽限期内全部重连失败，房间随之解散。
        /// 空房里没有任何在线连接，重建没有可打扰的对象，收益却是"让等待者有路可回"。</para>
        ///
        /// <para>与"全员静默"共用同一份冷却：触发本身没有成本压力，但也不能每帧都重建。</para>
        /// </remarks>
        public bool ShouldRebuildWhileEmpty(bool waitingForReconnect, float now)
        {
            if (!waitingForReconnect)
            {
                return false;
            }

            return m_CooldownUntil <= 0f || now >= m_CooldownUntil;
        }

        /// <summary>回到初始状态（服务器重新开始时用）。</summary>
        public void Reset()
        {
            m_AllSilentSince = -1f;
            m_CooldownUntil = -1f;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
