using System;
using System.IO;
using System.Text;
using System.Threading;

namespace RaidDemo.ServerHost
{
    /// <summary>
    /// 跟随服务器日志文件，把新写进来的内容交给界面。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么读文件而不是抓进程输出：</b>服务器是无头运行的，日志按项目约定写在
    /// <c>Logs/server.log</c>（Linux 的 deploy/server.sh 与 Windows 面板用同一个位置）。
    /// 读文件还有一个好处：面板是后打开的也能看到之前的日志，而抓管道只能看到打开之后的。</para>
    ///
    /// <para><b>为什么用 <see cref="FileShare.ReadWrite"/>：</b>日志文件正被服务器进程打开着，
    /// 不共享读写就会得到文件被占用，于是面板里永远没有日志——而这恰好是最需要日志的时候。</para>
    ///
    /// <para><b>为什么要处理文件变短：</b>重新开服时 Unity 会重建日志文件，长度一下回到 0。
    /// 此时必须把读取位置也归零，否则面板会一直停在旧内容上、再也刷不出新行。</para>
    /// </remarks>
    internal sealed class LogTailer : IDisposable
    {
        /// <summary>轮询间隔（毫秒）。</summary>
        private const int PollIntervalMilliseconds = 400;

        /// <summary>单次最多读多少字节（避免日志暴涨时一次吃掉几十兆内存）。</summary>
        private const int ReadBufferBytes = 64 * 1024;

        /// <summary>日志文件路径。</summary>
        private readonly string m_Path;

        /// <summary>UTF-8 增量解码器（多字节字符可能被两次读取切开）。</summary>
        private readonly Decoder m_Decoder = new UTF8Encoding(false).GetDecoder();

        /// <summary>轮询定时器。</summary>
        private Timer m_Timer;

        /// <summary>当前打开的日志流。</summary>
        private FileStream m_Stream;

        /// <summary>新内容到达（在后台线程触发，界面需自行切回 UI 线程）。</summary>
        public event Action<string> TextAppended;

        /// <summary>
        /// 建立跟随器。
        /// </summary>
        /// <param name="path">日志文件绝对路径。</param>
        public LogTailer(string path)
        {
            m_Path = path;
        }

        /// <summary>开始跟随（日志文件尚未存在也立即开始，出现后会自动接上）。</summary>
        public void Start()
        {
            Stop();
            m_Timer = new Timer(_ => Poll(), null, 0, PollIntervalMilliseconds);
        }

        /// <summary>停止跟随并关闭文件。</summary>
        public void Stop()
        {
            m_Timer?.Dispose();
            m_Timer = null;
            CloseStream();
        }

        /// <summary>从头重读（面板切换服务器目录时用）。</summary>
        public void Rewind()
        {
            CloseStream();
            m_Decoder.Reset();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stop();
        }

        /// <summary>读一次增量。</summary>
        private void Poll()
        {
            try
            {
                if (!EnsureStream())
                {
                    return;
                }

                if (m_Stream.Length < m_Stream.Position)
                {
                    m_Stream.Position = 0;
                    m_Decoder.Reset();
                }

                var buffer = new byte[ReadBufferBytes];
                var characters = new char[ReadBufferBytes];
                int read;
                while ((read = m_Stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var count = m_Decoder.GetChars(buffer, 0, read, characters, 0);
                    if (count > 0)
                    {
                        TextAppended?.Invoke(new string(characters, 0, count));
                    }
                }
            }
            catch (Exception)
            {
                // 文件被删、权限变化、磁盘暂时不可读：丢掉句柄，下一拍重新尝试。
                CloseStream();
            }
        }

        /// <summary>确保日志流已打开；文件不存在时返回 false。</summary>
        private bool EnsureStream()
        {
            if (m_Stream != null)
            {
                return true;
            }

            if (!File.Exists(m_Path))
            {
                return false;
            }

            m_Stream = new FileStream(
                m_Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return true;
        }

        /// <summary>关闭并丢弃当前流。</summary>
        private void CloseStream()
        {
            try
            {
                m_Stream?.Dispose();
            }
            catch (Exception)
            {
                // 关闭失败只会丢一个句柄，进程退出时由系统回收。
            }

            m_Stream = null;
        }
    }
}
