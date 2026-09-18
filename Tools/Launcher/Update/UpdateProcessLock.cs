using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RaidDemo.Launcher.Update
{
    /// <summary>
    /// 同一安装根的跨进程更新互斥锁。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-01。两个启动器同时更新同一安装根时，
    /// 暂存区互相清空、校验读到对方写入的字节，最终报出 <c>Could not find file …</c>
    /// 这类原始英文错误，安装目录还可能停在半新半旧的状态。</para>
    ///
    /// <para><b>为什么键是"安装根哈希"而不是进程名：</b>互斥的粒度必须是"同一份安装"。
    /// 用进程名会把两个不同安装目录的启动器也互相挡住；用安装根做键，
    /// 既保护了共享的暂存区，又不影响多份安装并存。</para>
    ///
    /// <para><b>为什么是 Local 命名空间：</b>启动器是桌面程序，两个实例必然在同一登录会话；
    /// 本地命名空间不需要全局命名空间权限，避免在受限账户下创建锁失败。</para>
    /// </remarks>
    public sealed class UpdateProcessLock : IDisposable
    {
        /// <summary>互斥体名前缀。</summary>
        private const string MutexPrefix = @"Local\RaidDemo.Update.";

        private readonly Mutex m_Mutex;
        private bool m_Held;

        private UpdateProcessLock(Mutex mutex)
        {
            m_Mutex = mutex;
        }

        /// <summary>
        /// 尝试获取某个安装根的更新锁；拿不到时立即返回而不是排队等待。
        /// </summary>
        /// <param name="installRoot">安装根目录。</param>
        /// <param name="busyReason">拿不到锁时的中文说明。</param>
        /// <returns>拿到返回实例；已被占用返回 <c>null</c>。</returns>
        public static UpdateProcessLock TryAcquire(string installRoot, out string busyReason)
        {
            busyReason = null;
            var mutex = new Mutex(false, BuildMutexName(installRoot));

            try
            {
                if (!mutex.WaitOne(TimeSpan.Zero))
                {
                    mutex.Dispose();
                    busyReason = "同一个安装目录正在被另一个更新进程使用，请等它完成后再试。";
                    return null;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 极少见：同名互斥体由另一个权限更高的进程持有。按"被占用"处理，
                // 提示玩家重试，而不是让一个权限异常冒到界面。
                mutex.Dispose();
                busyReason = "同一个安装目录正在被另一个更新进程使用，请稍后重试。";
                return null;
            }

            return new UpdateProcessLock(mutex) { m_Held = true };
        }

        /// <summary>按安装根的规范化绝对路径生成互斥体名。</summary>
        private static string BuildMutexName(string installRoot)
        {
            var normalized = string.IsNullOrWhiteSpace(installRoot)
                ? string.Empty
                : Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            var builder = new StringBuilder(MutexPrefix, MutexPrefix.Length + 32);
            for (var index = 0; index < 16; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }

        /// <summary>释放锁。</summary>
        public void Dispose()
        {
            if (m_Held)
            {
                try
                {
                    m_Mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 只在持锁线程释放；退出路径上真出现跨线程释放也不该把程序打崩。
                }

                m_Held = false;
            }

            m_Mutex.Dispose();
        }
    }
}
