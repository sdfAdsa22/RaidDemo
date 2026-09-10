using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RaidDemo.Kernel
{
    /// <summary>日志级别。数值越大越严重。</summary>
    public enum LogLevel
    {
        /// <summary>详细调试信息，仅开发期使用。</summary>
        Verbose = 0,

        /// <summary>一般性信息，用于记录关键流程节点。</summary>
        Info = 1,

        /// <summary>警告：不影响运行，但行为可能不符合预期。</summary>
        Warning = 2,

        /// <summary>错误：功能已经失效，需要修复。</summary>
        Error = 3,

        /// <summary>致命错误：无法继续运行。</summary>
        Fatal = 4,

        /// <summary>关闭全部日志。</summary>
        Off = 5
    }

    /// <summary>
    /// 日志服务。
    /// </summary>
    /// <remarks>
    /// <para>为什么不直接使用 Debug.Log：</para>
    /// <list type="bullet">
    /// <item><description>无法按级别过滤。发行版本中希望保留警告与错误、丢掉调试信息，直接使用 Debug.Log 做不到。</description></item>
    /// <item><description>无法在测试中断言。日志服务可以注入到被测代码，从而验证某条失败路径确实记录了错误，
    /// 而不是靠肉眼检查控制台。</description></item>
    /// <item><description>无法统计。可以观察各级别日志数量，用于发现被忽略的问题。</description></item>
    /// </list>
    ///
    /// <para>性能注意：字符串插值在调用点就已经发生，即使该级别被过滤掉也已经产生了内存分配。
    /// 因此高频日志务必先用 <see cref="IsEnabled"/> 判断，再拼装消息。</para>
    /// </remarks>
    public sealed class LogService
    {
        /// <summary>日志前缀，便于在混有第三方输出的控制台中筛选本项目的日志。</summary>
        private const string Prefix = "[RaidDemo]";

        private readonly LogLevel m_MinimumLevel;
        private readonly bool m_IncludeTimestamp;

        private int m_VerboseCount;
        private int m_InfoCount;
        private int m_WarningCount;
        private int m_ErrorCount;

        /// <summary>
        /// 创建日志服务。
        /// </summary>
        /// <param name="minimumLevel">最低记录级别。低于该级别的调用会被直接丢弃。</param>
        /// <param name="includeTimestamp">是否在消息前附加时间戳。开发期建议开启，便于对齐事件发生时刻。</param>
        public LogService(LogLevel minimumLevel = LogLevel.Info, bool includeTimestamp = false)
        {
            m_MinimumLevel = minimumLevel;
            m_IncludeTimestamp = includeTimestamp;
        }

        /// <summary>指定级别当前是否会被记录。高频日志应先判断再拼装消息，避免无谓的字符串分配。</summary>
        public bool IsEnabled(LogLevel level)
        {
            return level >= m_MinimumLevel && level != LogLevel.Off;
        }

        /// <summary>开发期详细日志。发行构建中通常被最低级别过滤掉。</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Verbose(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Verbose))
            {
                return;
            }

            m_VerboseCount++;
            Debug.unityLogger.Log(LogType.Log, (object)Format(message), context);
        }

        /// <summary>一般信息。</summary>
        public void Info(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info))
            {
                return;
            }

            m_InfoCount++;
            Debug.unityLogger.Log(LogType.Log, (object)Format(message), context);
        }

        /// <summary>警告。</summary>
        public void Warning(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warning))
            {
                return;
            }

            m_WarningCount++;
            Debug.unityLogger.Log(LogType.Warning, (object)Format(message), context);
        }

        /// <summary>错误。</summary>
        public void Error(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error))
            {
                return;
            }

            m_ErrorCount++;
            Debug.unityLogger.Log(LogType.Error, (object)Format(message), context);
        }

        /// <summary>记录异常，保留完整堆栈。</summary>
        public void Exception(Exception exception, UnityEngine.Object context = null)
        {
            if (exception == null || !IsEnabled(LogLevel.Error))
            {
                return;
            }

            m_ErrorCount++;
            Debug.unityLogger.LogException(exception, context);
        }

        /// <summary>各级别累计记录条数，顺序为 Verbose、Info、Warning、Error。</summary>
        public (int Verbose, int Info, int Warning, int Error) GetCounts()
        {
            return (m_VerboseCount, m_InfoCount, m_WarningCount, m_ErrorCount);
        }

        /// <summary>清零各级别计数。用于测试用例之间隔离。</summary>
        public void ResetCounts()
        {
            m_VerboseCount = 0;
            m_InfoCount = 0;
            m_WarningCount = 0;
            m_ErrorCount = 0;
        }

        private string Format(string message)
        {
            if (!m_IncludeTimestamp)
            {
                return $"{Prefix} {message}";
            }

            // 使用游戏时间而非系统时间：排查逻辑问题时，游戏内时间轴比墙上时钟更容易对齐。
            return $"{Prefix} [{UnityEngine.Time.realtimeSinceStartup:F3}s] {message}";
        }
    }
}
