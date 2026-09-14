using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 主线程卡顿看门狗：某一帧超过阈值就打一条带时长与场景名的警告。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它（P-48 的排查教训）：</b>联机里"对面掉线"这个现象可以由很多原因造成，
    /// 其中最容易被忽略的一类是**自己这边卡住了**——主线程一旦停几百毫秒到几秒，
    /// NGO/UTP 的心跳就会超时，对面看到的正是"你掉线了"。日志里当时完全没有这类线索，
    /// 只能靠事后猜；有了它，"那一刻有没有卡顿、卡了多久"变成一行可以直接读的事实。</para>
    ///
    /// <para><b>为什么阈值是 0.5 秒：</b>正常帧只有十几毫秒，几百毫秒的停顿已经足以让
    /// UTP 判定心跳丢失（默认心跳窗口在秒级）。低于它的抖动不值得刷日志。</para>
    ///
    /// <para>按 owner 分开记状态：服务器与客户端可能同时在同一个进程里跑（编辑器单机），
    /// 共用一个时间戳会互相干扰。</para>
    /// </remarks>
    internal static class FrameStallWatchdog
    {
        /// <summary>超过这个时长（秒）就算一次卡顿。</summary>
        public const float DefaultThresholdSeconds = 0.5f;

        private static readonly Dictionary<string, float> s_LastTick = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> s_StallCount = new Dictionary<string, int>();

        /// <summary>累计检测到的卡顿次数（诊断与验收可读）。</summary>
        public static int StallCount(string owner)
        {
            return owner != null && s_StallCount.TryGetValue(owner, out var count) ? count : 0;
        }

        /// <summary>
        /// 每帧调用一次（放在各自 Update 的最前面）。
        /// </summary>
        /// <param name="owner">宿主名（"服务器" / "客户端"）。</param>
        /// <param name="thresholdSeconds">判定阈值。</param>
        public static void Tick(string owner, float thresholdSeconds = DefaultThresholdSeconds)
        {
            if (string.IsNullOrEmpty(owner))
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (!s_LastTick.TryGetValue(owner, out var last))
            {
                s_LastTick[owner] = now;
                return;
            }

            s_LastTick[owner] = now;

            var delta = now - last;
            if (delta < thresholdSeconds)
            {
                return;
            }

            s_StallCount[owner] = StallCount(owner) + 1;

            // 用 Warning：这类日志必须在默认级别下可见（排障时不会有人记得先开 verbose）。
            Debug.LogWarning(
                $"[诊断] {owner} 主线程卡顿 {delta:F2} 秒（第 {StallCount(owner)} 次，"
                + $"帧 {Time.frameCount}，场景 {SceneManager.GetActiveScene().name}）。"
                + "这类停顿会让 NGO 心跳超时，对面看到的就是掉线。");
        }
    }
}
