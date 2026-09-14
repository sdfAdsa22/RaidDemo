using System.Collections.Generic;
using RaidDemo.Shared;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 玩家在一局里的生命状态：存活 / 失能（倒地待救）/ 死亡。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有"失能"这个中间态：</b>联机合作里"被打倒"与"被打死"必须是两件事——
    /// 前者给队友一个救人的窗口，后者才是这一局的终点。少了中间态，团队玩法就只剩"谁先死"。</para>
    ///
    /// <para><b>为什么放在逻辑层而不是服务端里：</b>它是一个纯状态机（带计时与进度），
    /// 没有网络、没有场景、没有引擎依赖，因此可以在 EditMode 里逐条验证——
    /// 而"倒地多久转死亡""救多久能起来"这类规则正是最需要被测试钉住的东西。</para>
    ///
    /// <para><b>单机为什么不受影响：</b>是否进入失能由调用方决定（只有一名玩家时没有队友可救，
    /// 直接按死亡结算）。跟踪器本身不关心人数。</para>
    /// </remarks>
    public sealed class PlayerLifeStateTracker
    {
        /// <summary>倒地后无人施救的存活时限（秒）。</summary>
        public const float BleedOutSeconds = 60f;

        /// <summary>施救需要的持续时长（秒）。</summary>
        public const float ReviveSeconds = 3f;

        /// <summary>施救的有效距离（米）。</summary>
        public const float ReviveRangeMeters = 2.5f;

        /// <summary>被救起后恢复的生命值。</summary>
        public const float RevivedHealth = 40f;

        /// <summary>一名玩家的生命状态。</summary>
        private sealed class Entry
        {
            /// <summary>当前状态。</summary>
            public PlayerLifeState State = PlayerLifeState.Alive;

            /// <summary>倒地剩余时间（秒），仅失能时有意义。</summary>
            public float BleedOutRemaining;

            /// <summary>当前施救进度（秒）。</summary>
            public float ReviveProgress;
        }

        private readonly Dictionary<int, Entry> m_Entries = new Dictionary<int, Entry>();

        /// <summary>登记一名玩家（重开一局时也用它复位）。</summary>
        /// <param name="playerId">玩家编号。</param>
        public void Register(int playerId)
        {
            m_Entries[playerId] = new Entry();
        }

        /// <summary>移除一名玩家（断线时调用）。</summary>
        /// <param name="playerId">玩家编号。</param>
        public bool Remove(int playerId)
        {
            return m_Entries.Remove(playerId);
        }

        /// <summary>清空全部状态（重开一局）。</summary>
        public void Clear()
        {
            m_Entries.Clear();
        }

        /// <summary>取一名玩家的状态；未登记时按"存活"处理。</summary>
        /// <param name="playerId">玩家编号。</param>
        public PlayerLifeState GetState(int playerId)
        {
            return m_Entries.TryGetValue(playerId, out var entry) ? entry.State : PlayerLifeState.Alive;
        }

        /// <summary>倒地剩余时间（秒）；不在失能状态时为 0。</summary>
        /// <param name="playerId">玩家编号。</param>
        public float GetBleedOutRemaining(int playerId)
        {
            return m_Entries.TryGetValue(playerId, out var entry)
                ? (entry.State == PlayerLifeState.Downed ? entry.BleedOutRemaining : 0f)
                : 0f;
        }

        /// <summary>当前的施救进度（秒）。</summary>
        /// <param name="playerId">玩家编号。</param>
        public float GetReviveProgress(int playerId)
        {
            return m_Entries.TryGetValue(playerId, out var entry) ? entry.ReviveProgress : 0f;
        }

        /// <summary>
        /// 把一名玩家打成"失能"。
        /// </summary>
        /// <param name="playerId">玩家编号。</param>
        /// <returns>状态确实发生了迁移（存活 → 失能）时返回 true。</returns>
        public bool MarkDowned(int playerId)
        {
            if (!m_Entries.TryGetValue(playerId, out var entry)
                || entry.State != PlayerLifeState.Alive)
            {
                return false;
            }

            entry.State = PlayerLifeState.Downed;
            entry.BleedOutRemaining = BleedOutSeconds;
            entry.ReviveProgress = 0f;
            return true;
        }

        /// <summary>
        /// 推进一帧：倒计时与施救进度。
        /// </summary>
        /// <param name="deltaSeconds">时间步长（秒）。</param>
        /// <param name="bleedOutNow">输出：这一帧因倒计时耗尽而死亡的玩家。</param>
        /// <param name="revivedNow">输出：这一帧被救起的玩家。</param>
        /// <remarks>
        /// 输出用调用方给的列表而不是返回新列表：每帧都可能调用，
        /// 反复分配短命集合会让帧时间出现周期性尖刺（与寻路那边同一条理由）。
        /// </remarks>
        public void Tick(
            float deltaSeconds,
            List<int> bleedOutNow,
            List<int> revivedNow)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            foreach (var pair in m_Entries)
            {
                var entry = pair.Value;
                if (entry.State != PlayerLifeState.Downed)
                {
                    continue;
                }

                if (entry.ReviveProgress >= ReviveSeconds)
                {
                    entry.State = PlayerLifeState.Alive;
                    entry.BleedOutRemaining = 0f;
                    entry.ReviveProgress = 0f;
                    revivedNow?.Add(pair.Key);
                    continue;
                }

                entry.BleedOutRemaining -= deltaSeconds;
                if (entry.BleedOutRemaining > 0f)
                {
                    continue;
                }

                entry.State = PlayerLifeState.Dead;
                entry.BleedOutRemaining = 0f;
                bleedOutNow?.Add(pair.Key);
            }
        }

        /// <summary>
        /// 累加施救进度（队友正在施救时由服务端每帧调用）。
        /// </summary>
        /// <param name="playerId">被救的玩家。</param>
        /// <param name="deltaSeconds">本帧推进的秒数。</param>
        /// <returns>确实累加了返回 true（目标不在失能状态时不累加）。</returns>
        public bool AddReviveProgress(int playerId, float deltaSeconds)
        {
            if (deltaSeconds <= 0f
                || !m_Entries.TryGetValue(playerId, out var entry)
                || entry.State != PlayerLifeState.Downed)
            {
                return false;
            }

            entry.ReviveProgress += deltaSeconds;
            return true;
        }

        /// <summary>
        /// 中断施救（施救者松手或走远）。
        /// </summary>
        /// <param name="playerId">被救的玩家。</param>
        /// <remarks>
        /// 进度清零而不是回退：回退会让"救一下再松手"变成一种可刷的操作，
        /// 而清零让施救变成"必须连续做完"的承诺——这正是它作为团队压力来源的意义。
        /// </remarks>
        public void InterruptRevive(int playerId)
        {
            if (m_Entries.TryGetValue(playerId, out var entry) && entry.State == PlayerLifeState.Downed)
            {
                entry.ReviveProgress = 0f;
            }
        }

        /// <summary>
        /// 判断施救者能否对目标施救：目标是失能、施救者存活、且两人足够近。
        /// </summary>
        /// <param name="rescuerId">施救者。</param>
        /// <param name="targetId">被救者。</param>
        /// <param name="rescuerPosition">施救者的平面位置。</param>
        /// <param name="targetPosition">被救者的平面位置。</param>
        /// <returns>可以施救返回 true。</returns>
        public bool CanRevive(
            int rescuerId,
            int targetId,
            Vector2F rescuerPosition,
            Vector2F targetPosition)
        {
            if (rescuerId == targetId || GetState(rescuerId) != PlayerLifeState.Alive)
            {
                return false;
            }

            if (GetState(targetId) != PlayerLifeState.Downed)
            {
                return false;
            }

            var dx = rescuerPosition.X - targetPosition.X;
            var dy = rescuerPosition.Y - targetPosition.Y;
            return ((dx * dx) + (dy * dy)) <= ReviveRangeMeters * ReviveRangeMeters;
        }
    }

    /// <summary>玩家在这一局里的生命状态。</summary>
    public enum PlayerLifeState
    {
        /// <summary>存活。</summary>
        Alive = 0,

        /// <summary>失能：倒地待救，倒计时走完即死亡。</summary>
        Downed,

        /// <summary>死亡：本局结束。</summary>
        Dead,
    }
}
