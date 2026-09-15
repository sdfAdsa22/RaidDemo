using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RaidDemo.UpdateSource
{
    /// <summary>
    /// 更新源服务端入口。
    /// </summary>
    /// <remarks>
    /// <para><b>同一份程序跑三个位置：</b>开发机（本机更新源）、云主机（对外分发）、
    /// 以及将来任何一台能跑 .NET 的机器。不区分"开发版 / 生产版"，
    /// 因为差异只应该体现在参数上，而不是代码分支上。</para>
    ///
    /// <para><b>Windows 上默认只监听 127.0.0.1：</b><c>HttpListener</c> 绑定通配地址
    /// （<c>+</c> / <c>*</c>）在 Windows 上需要管理员权限或 URL 预留（<c>netsh http add urlacl</c>）。
    /// 本机演示不需要对外监听，因此默认给最小权限的地址；需要局域网访问时显式传
    /// <c>--host +</c> 并以管理员身份运行。Linux 上默认监听全部地址（云主机需要它）。</para>
    /// </remarks>
    public static class Program
    {
        /// <summary>默认端口：与游戏服务器（7777 / 8080）错开，避免抢端口。</summary>
        private const int DefaultPort = 8090;

        /// <summary>入口。</summary>
        /// <param name="args">命令行参数。</param>
        /// <returns>退出码。</returns>
        public static int Main(string[] args)
        {
            ConfigureConsoleEncoding();

            var options = ParseArguments(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            var root = Path.GetFullPath(options.Root ?? Path.Combine(AppContext.BaseDirectory, "update-source"));
            var token = options.Token ?? Environment.GetEnvironmentVariable("RAIDDEMO_UPDATE_TOKEN");
            if (string.IsNullOrWhiteSpace(token) && !options.ReadOnly)
            {
                token = GenerateToken();
                Console.WriteLine($"[口令] 未指定写操作口令，已自动生成：{token}");
            }

            var store = new UpdateSourceStore(root);
            var server = new UpdateSourceServer(store, options.Host, options.Port, token, options.ReadOnly);

            Console.WriteLine("RaidDemo 更新源服务端");
            Console.WriteLine($"  更新源目录 : {root}");
            Console.WriteLine($"  当前版本   : {(string.IsNullOrEmpty(store.ReadCurrentVersion()) ? "（尚未发布）" : store.ReadCurrentVersion())}");
            Console.WriteLine($"  监听端口   : {options.Port}");
            Console.WriteLine($"  写操作     : {(options.ReadOnly ? "已关闭（只读模式）" : "需要口令（X-Auth-Token）")}");
            Console.WriteLine();
            Console.WriteLine("  面板地址：");
            Console.WriteLine($"    http://127.0.0.1:{options.Port}/");
            foreach (var address in GetLocalAddresses())
            {
                Console.WriteLine($"    http://{address}:{options.Port}/   （局域网）");
            }

            Console.WriteLine();
            Console.WriteLine("  客户端使用的地址（启动器 / 游戏内）：");
            Console.WriteLine($"    http://<本机或公网地址>:{options.Port}");
            Console.WriteLine();
            Console.WriteLine("按 Ctrl+C 停止。");

            try
            {
                server.Start();
            }
            catch (HttpListenerException exception)
            {
                Console.Error.WriteLine("[错误] 无法监听该地址：" + exception.Message);
                Console.Error.WriteLine("      Windows 上绑定通配地址需要管理员权限或 URL 预留，例如：");
                Console.Error.WriteLine($"      netsh http add urlacl url=http://+:{options.Port}/ user=Everyone");
                return 2;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[错误] 服务启动失败：" + exception.Message);
                return 2;
            }

            return 0;
        }

        /// <summary>
        /// 控制台统一 UTF-8（与启动器同一处理：重定向时 Console.OutputEncoding 不可靠）。
        /// </summary>
        private static void ConfigureConsoleEncoding()
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        }

        /// <summary>取本机所有 IPv4 地址（用于提示局域网访问地址）。</summary>
        private static List<string> GetLocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    {
                        result.Add(address.ToString());
                    }
                }
            }
            catch (Exception)
            {
                // 取不到地址不影响服务本身。
            }

            return result;
        }

        /// <summary>生成一个便于手抄的随机口令。</summary>
        private static string GenerateToken()
        {
            const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
            var random = new Random();
            var builder = new StringBuilder(10);
            for (var index = 0; index < 10; index++)
            {
                builder.Append(alphabet[random.Next(alphabet.Length)]);
            }

            return builder.ToString();
        }

        /// <summary>解析命令行参数。</summary>
        private static ServerOptions ParseArguments(string[] args)
        {
            var options = new ServerOptions
            {
                // Windows 默认最小权限绑定；Linux（云主机）默认监听全部地址。
                Host = OperatingSystem.IsWindows() ? "127.0.0.1" : "+",
                Port = DefaultPort,
            };

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index].ToLowerInvariant())
                {
                    case "--root":
                        options.Root = NextValue(args, ref index);
                        break;
                    case "--port":
                        if (int.TryParse(NextValue(args, ref index), out var port))
                        {
                            options.Port = port;
                        }

                        break;
                    case "--host":
                        options.Host = NextValue(args, ref index) ?? options.Host;
                        break;
                    case "--token":
                        options.Token = NextValue(args, ref index);
                        break;
                    case "--readonly":
                        options.ReadOnly = true;
                        break;
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;
                }
            }

            return options;
        }

        /// <summary>取下一个参数值。</summary>
        private static string NextValue(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                return null;
            }

            index++;
            return args[index];
        }

        /// <summary>打印帮助。</summary>
        private static void PrintHelp()
        {
            Console.WriteLine("RaidDemo 更新源服务端");
            Console.WriteLine();
            Console.WriteLine("用法：");
            Console.WriteLine("  RaidDemo.UpdateSource --root <更新源目录> [--port 8090] [--host +] [--token <口令>] [--readonly]");
            Console.WriteLine();
            Console.WriteLine("参数：");
            Console.WriteLine("  --root <目录>   更新源根目录（含 manifest.json 与 versions/）；默认 ./update-source");
            Console.WriteLine("  --port <端口>   监听端口，默认 8090");
            Console.WriteLine("  --host <地址>   监听地址；Windows 默认 127.0.0.1，Linux 默认 +（全部地址）");
            Console.WriteLine("  --token <口令>  写操作口令；也可用环境变量 RAIDDEMO_UPDATE_TOKEN；缺省时自动生成并打印");
            Console.WriteLine("  --readonly      只读模式：只托管下载，关闭上传 / 发布 / 删除");
            Console.WriteLine();
            Console.WriteLine("只读接口（无需口令）：GET /manifest.json、/body/…、/content/…、/code/…、/api/status");
            Console.WriteLine("写接口（需要 X-Auth-Token）：POST /api/upload、/api/publish、/api/delete");
        }

        /// <summary>命令行参数。</summary>
        private sealed class ServerOptions
        {
            /// <summary>更新源根目录。</summary>
            public string Root { get; set; }

            /// <summary>监听端口。</summary>
            public int Port { get; set; }

            /// <summary>监听地址。</summary>
            public string Host { get; set; }

            /// <summary>写操作口令。</summary>
            public string Token { get; set; }

            /// <summary>只读模式。</summary>
            public bool ReadOnly { get; set; }

            /// <summary>显示帮助。</summary>
            public bool ShowHelp { get; set; }
        }
    }
}
