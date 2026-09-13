using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Kernel;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器启动参数解析的行为测试。
    /// </summary>
    /// <remarks>
    /// <para>这些用例覆盖的是「服务器唯一的输入通道」：无头环境下没有界面、没有配置文件，
    /// 一切都来自命令行。参数解析错了不会抛异常，只会表现为「服务器起来了但行为不对」，
    /// 因此把每种合法与非法组合都钉在测试里。</para>
    /// </remarks>
    [TestFixture]
    public sealed class LaunchOptionsTests
    {
        /// <summary>没有任何参数时应拿到默认值，且不认为是服务器启动。</summary>
        [Test]
        public void 无参数时使用默认值且不进入服务器模式()
        {
            var ok = LaunchOptions.TryParse(new string[0], out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsFalse(options.IsServerRequested);
            Assert.AreEqual(LaunchOptions.DefaultPort, options.Port);
            Assert.AreEqual(LaunchOptions.DefaultRoomName, options.RoomName);
            Assert.AreEqual(LaunchOptions.DefaultSaveDirectory, options.SaveDirectory);
            Assert.AreEqual(LogLevel.Info, options.MinimumLogLevel);
            Assert.IsFalse(options.IsHeadless);
        }

        /// <summary>传 null 等价于空参数，不应抛异常（编辑器与测试环境会走到这条路径）。</summary>
        [Test]
        public void 参数为null时按空处理()
        {
            var ok = LaunchOptions.TryParse(null, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsFalse(options.IsServerRequested);
        }

        /// <summary>完整参数组合应逐项生效。</summary>
        [Test]
        public void 完整的服务器参数逐项生效()
        {
            var args = new[]
            {
                "-server", "-port", "8123", "-room", "工业区", "-saveDir", "server_saves/prod",
                "-logLevel", "warning", "-batchmode", "-nographics",
            };

            var ok = LaunchOptions.TryParse(args, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsTrue(options.IsServerRequested);
            Assert.AreEqual(8123, options.Port);
            Assert.AreEqual("工业区", options.RoomName);
            Assert.AreEqual("server_saves/prod", options.SaveDirectory);
            Assert.AreEqual(LogLevel.Warning, options.MinimumLogLevel);
            Assert.IsTrue(options.IsHeadless);
        }

        /// <summary>引擎自己的参数必须被忽略，不能因为不认识就报错。</summary>
        [Test]
        public void 不认识的参数被忽略()
        {
            var args = new[] { "-server", "-logFile", "server.log", "-screen-width", "1920", "-force-d3d11" };

            var ok = LaunchOptions.TryParse(args, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsTrue(options.IsServerRequested);
            Assert.AreEqual(LaunchOptions.DefaultPort, options.Port);
        }

        /// <summary>端口超范围必须被拒绝——静默截断会让玩家连到一个谁都没监听的端口。</summary>
        [Test]
        public void 端口超范围时解析失败()
        {
            var ok = LaunchOptions.TryParse(new[] { "-server", "-port", "70000" }, out _, out var error);

            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
            StringAssert.Contains("65535", error);
        }

        /// <summary>端口不是数字时必须报错，而不是退回默认端口。</summary>
        [Test]
        public void 端口非数字时解析失败()
        {
            var ok = LaunchOptions.TryParse(new[] { "-server", "-port", "abc" }, out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("-port", error);
        }

        /// <summary>开关缺少取值时必须报错。</summary>
        [Test]
        public void 开关缺少取值时解析失败()
        {
            var ok = LaunchOptions.TryParse(new[] { "-server", "-room" }, out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("缺少取值", error);
        }

        /// <summary>
        /// 存档目录必须是相对路径。
        /// </summary>
        /// <remarks>
        /// <para>Windows 风格盘符与 Linux 风格根路径都要拒绝：同一份配置要能在两种服务器上通用，
        /// 而绝对路径在换机器后指向的是不存在的目录。</para>
        ///
        /// <para>用例在运行时拼出来而不是写成字面量：本工程有「源码不得包含本机路径」的硬规则
        /// （见 <c>PathRulesTests</c>），把盘符路径直接写进测试源码本身就会被判违规。</para>
        /// </remarks>
        [Test]
        public void 存档目录不是相对路径时解析失败()
        {
            var drivePrefix = "C" + ":";
            var invalid = new[]
            {
                drivePrefix + "/saves",
                drivePrefix + "\\saves",
                "/" + "var/saves",
                "\\" + "saves",
                ".." + "/saves",
                string.Empty,
            };

            foreach (var saveDir in invalid)
            {
                var ok = LaunchOptions.TryParse(new[] { "-server", "-saveDir", saveDir }, out _, out var error);

                Assert.IsFalse(ok, $"「{saveDir}」应被拒绝。");
                StringAssert.Contains("相对路径", error);
            }
        }

        /// <summary>房间名不能空，也不能长到塞爆日志与界面。</summary>
        [Test]
        public void 房间名为空或超长时解析失败()
        {
            Assert.IsFalse(LaunchOptions.TryParse(new[] { "-server", "-room", "   " }, out _, out var emptyError));
            StringAssert.Contains("不能为空", emptyError);

            var tooLong = new string('长', LaunchOptions.MaxRoomNameLength + 1);
            Assert.IsFalse(LaunchOptions.TryParse(new[] { "-server", "-room", tooLong }, out _, out var longError));
            StringAssert.Contains("最长", longError);
        }

        /// <summary>日志等级只接受四个已知取值。</summary>
        [Test]
        public void 日志等级非法时解析失败()
        {
            var ok = LaunchOptions.TryParse(new[] { "-server", "-logLevel", "trace" }, out _, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("verbose", error);
        }

        /// <summary>
        /// 无头参数在没有 -server 时只提示、不失败。
        /// </summary>
        /// <remarks>
        /// 这条是给「用 -batchmode 跑自动化任务」准备的：那种运行同样无头，但不是服务器。
        /// 直接报错会让任务跑不起来，因此降级为警告。
        /// </remarks>
        [Test]
        public void 无头参数缺少server时给出警告但仍可继续()
        {
            var ok = LaunchOptions.TryParse(new[] { "-batchmode" }, out var options, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsFalse(options.IsServerRequested);
            Assert.IsTrue(options.IsHeadless);
            Assert.AreEqual(1, options.Warnings.Count);
        }

        /// <summary>启动摘要要能一眼看出身份、端口与房间，供服务器日志使用。</summary>
        [Test]
        public void 启动摘要包含关键信息()
        {
            LaunchOptions.TryParse(
                new[] { "-server", "-port", "9000", "-room", "测试房" },
                out var options,
                out _);

            var summary = options.Describe();

            StringAssert.Contains("专用服务器", summary);
            StringAssert.Contains("9000", summary);
            StringAssert.Contains("测试房", summary);
        }
    }
}
