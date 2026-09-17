using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 状态页的路由与渲染测试。
    /// </summary>
    /// <remarks>
    /// <para>被测对象是纯逻辑（路线判定、来源限制、HTML 转义、JSON 契约），
    /// 因此不需要真的开端口、也不需要进入播放模式。真实的 socket 收发由
    /// 验收时对开发版服务器的 <c>curl</c> 覆盖。</para>
    /// </remarks>
    [TestFixture]
    public sealed class DashboardTests
    {
        private const string Local = "127.0.0.1";
        private const string Remote = "192.168.1.30";

        private static ServerStatusSnapshot BuildSnapshot()
        {
            var snapshot = new ServerStatusSnapshot
            {
                UptimeSeconds = 125f,
                Port = 7777,
                Listening = true,
                SaveDirectory = "server_saves",
                Phase = (byte)LobbyPhase.Waiting,
                PhaseText = "等待中",
                RoomName = "验收房间",
                HasPassword = true,
                HostNickname = "小明",
                ConnectedPlayerCount = 2,
                RaidElapsedSeconds = 0f,
            };

            snapshot.Addresses.Add("127.0.0.1:7777");
            snapshot.Members.Add(new ServerStatusMember
            {
                ClientId = 0,
                Nickname = "小明",
                IsHost = true,
                InRoom = true,
                OnlineSeconds = 125f,
                StateText = "在房间（房主）",
            });
            snapshot.Members.Add(new ServerStatusMember
            {
                ClientId = 1,
                Nickname = "<script>坏名字</script>",
                IsHost = false,
                InRoom = false,
                StateText = "已登录（未进房间）",
            });
            snapshot.Logs.Add(new ServerStatusLogEntry { Time = "12.3s", Level = "信息", Message = "房间已创建" });
            return snapshot;
        }

        /// <summary>最近一次动作处理器收到的参数（踢人编号等）。每次请求前重置。</summary>
        private static string s_LastActionArgument;

        private static DashboardResponse Handle(
            string requestLine,
            string remote,
            out string actionTaken,
            string actionResult = null,
            string adminToken = null,
            string requestToken = null)
        {
            // 局部变量中转：out 参数不能直接在 lambda 里赋值（CS1628）。
            string taken = null;
            s_LastActionArgument = null;

            var headers = new Dictionary<string, string>();
            if (requestToken != null)
            {
                headers[DashboardRouter.AdminTokenHeader] = requestToken;
            }

            var response = DashboardRouter.Handle(
                requestLine,
                headers,
                remote,
                BuildSnapshot,
                (action, argument) =>
                {
                    taken = action;
                    s_LastActionArgument = argument;
                    return actionResult;
                },
                adminToken);

            actionTaken = taken;
            return response;
        }

        /// <summary>首页应当给出 HTML，并带上房间与玩家信息。</summary>
        [Test]
        public void 首页返回HTML与房间信息()
        {
            var response = Handle("GET / HTTP/1.1", Local, out _);

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(DashboardRenderer.HtmlContentType, response.ContentType);
            StringAssert.Contains("验收房间", response.Body);
            StringAssert.Contains("小明", response.Body);
            StringAssert.Contains("等待中", response.Body);
        }

        /// <summary>玩家昵称里的标签必须被转义：状态页不能成为注入点。</summary>
        [Test]
        public void 渲染会转义玩家输入()
        {
            var response = Handle("GET / HTTP/1.1", Local, out _);

            // 页面自身的内联脚本是允许的（自动刷新与口令框）；要断言的是昵称里的标签没有原样落进 HTML。
            StringAssert.DoesNotContain("<script>坏名字</script>", response.Body);
            StringAssert.Contains("&lt;script&gt;", response.Body);
        }

        /// <summary>外网 + 未配置口令时页面只有只读信息（按钮画出来也点不动）。</summary>
        [Test]
        public void 运维按钮只对本机显示()
        {
            StringAssert.Contains("stop-room", Handle("GET / HTTP/1.1", Local, out _).Body);
            StringAssert.DoesNotContain("action=stop-room", Handle("GET / HTTP/1.1", Remote, out _).Body);
            StringAssert.Contains("未配置管理口令", Handle("GET / HTTP/1.1", Remote, out _).Body);
        }

        /// <summary>配置了口令后，外网页面也画出操作区（真正的拦截在路由层）。</summary>
        [Test]
        public void 配置口令后外网页面显示操作区()
        {
            var response = Handle("GET / HTTP/1.1", Remote, out _, adminToken: "s3cret");

            StringAssert.Contains("action=stop-room", response.Body);
            StringAssert.Contains("adminToken", response.Body);
        }

        /// <summary>JSON 路由：与页面同源的机器可读版本。</summary>
        [Test]
        public void JSON路由可被解析回快照()
        {
            var response = Handle("GET /status.json HTTP/1.1", Local, out _);

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(DashboardRenderer.JsonContentType, response.ContentType);

            var parsed = JsonUtility.FromJson<ServerStatusSnapshot>(response.Body);
            Assert.IsNotNull(parsed);
            Assert.AreEqual("验收房间", parsed.RoomName);
            Assert.AreEqual(2, parsed.Members.Count);
            Assert.AreEqual("等待中", parsed.PhaseText);
            Assert.AreEqual(1, parsed.Logs.Count);
        }

        /// <summary>健康检查路由给监控用。</summary>
        [Test]
        public void 健康检查返回ok()
        {
            var response = Handle("GET /healthz HTTP/1.1", Local, out _);
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("ok", response.Body);
        }

        /// <summary>未登记的路径与非法请求分别回 404 与 400。</summary>
        [Test]
        public void 未知路径与非GET方法被拒()
        {
            Assert.AreEqual(404, Handle("GET /nope HTTP/1.1", Local, out _).StatusCode);
            Assert.AreEqual(405, Handle("POST / HTTP/1.1", Local, out _).StatusCode);
            Assert.AreEqual(400, Handle("这不是 HTTP", Local, out _).StatusCode);
        }

        /// <summary>回环判定：整段 127/8 与 IPv6 回环都算本机。</summary>
        [Test]
        public void 回环判定覆盖常见写法()
        {
            Assert.IsTrue(DashboardRouter.IsLoopback("127.0.0.1"));
            Assert.IsTrue(DashboardRouter.IsLoopback("127.0.0.11"));
            Assert.IsTrue(DashboardRouter.IsLoopback("::1"));
            Assert.IsFalse(DashboardRouter.IsLoopback("10.0.0.5"));
            Assert.IsFalse(DashboardRouter.IsLoopback("203.0.113.9"));
            Assert.IsFalse(DashboardRouter.IsLoopback(null));
        }

        /// <summary>本机发起的停止房间请求会打到动作处理器上。</summary>
        [Test]
        public void 本机可执行停止房间()
        {
            var response = Handle("GET /?action=stop-room HTTP/1.1", Local, out var actionTaken);

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(DashboardRouter.StopRoomAction, actionTaken);
            Assert.AreEqual(DashboardRouter.StopRoomAction, response.ActionTaken);
            StringAssert.Contains("房间已解散", response.Body);
        }

        /// <summary>外网来源且在没配置口令时，写操作被拒绝且不会调用处理器（默认攻击面为零）。</summary>
        [Test]
        public void 外网不能执行写操作()
        {
            var response = Handle("GET /?action=stop-server HTTP/1.1", Remote, out var actionTaken);

            Assert.AreEqual(403, response.StatusCode);
            Assert.IsNull(actionTaken, "被拒绝的请求不能被执行。");
            StringAssert.Contains("本机", response.Body);
        }

        /// <summary>带对管理口令的远程请求可以执行；口令写在请求头里。</summary>
        [Test]
        public void 远程请求带对口令可以执行()
        {
            var response = Handle(
                "GET /?action=stop-room HTTP/1.1",
                Remote,
                out var actionTaken,
                adminToken: "s3cret",
                requestToken: "s3cret");

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(DashboardRouter.StopRoomAction, actionTaken);
        }

        /// <summary>口令错误与"未带口令"一样被拒绝，且不给任何执行机会。</summary>
        [Test]
        public void 远程请求口令错误被拒()
        {
            var wrong = Handle(
                "GET /?action=stop-room HTTP/1.1",
                Remote,
                out var actionTaken,
                adminToken: "s3cret",
                requestToken: "guess");
            Assert.AreEqual(403, wrong.StatusCode);
            Assert.IsNull(actionTaken);
            StringAssert.Contains("管理口令", wrong.Body);

            var missing = Handle("GET /?action=stop-room HTTP/1.1", Remote, out var missingTaken, adminToken: "s3cret");
            Assert.AreEqual(403, missing.StatusCode);
            Assert.IsNull(missingTaken);
        }

        /// <summary>命令行排查用：口令也能从查询串传（页面自身走请求头）。</summary>
        [Test]
        public void 口令可从查询串传入()
        {
            var response = Handle(
                "GET /?action=stop-room&token=s3cret HTTP/1.1",
                Remote,
                out var actionTaken,
                adminToken: "s3cret");

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(DashboardRouter.StopRoomAction, actionTaken);
        }

        /// <summary>踢人动作与参数解析：client 参数必须原样交给处理器。</summary>
        [Test]
        public void 踢人动作解析参数()
        {
            var response = Handle("GET /?action=kick-player&client=3 HTTP/1.1", Local, out var actionTaken);

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(DashboardRouter.KickPlayerAction, actionTaken);
            Assert.AreEqual("3", s_LastActionArgument);
            StringAssert.Contains("已踢出玩家", response.Body);
        }

        /// <summary>本机页面渲染出踢人按钮：只对房间成员画，昵称进 data 属性而不是脚本字面量。</summary>
        [Test]
        public void 页面渲染踢人按钮()
        {
            var response = Handle("GET / HTTP/1.1", Local, out _);

            StringAssert.Contains("kick-player", response.Body);
            StringAssert.Contains("data-nick=\"小明\"", response.Body);
            // 不在房间里的连接没有可踢的席位：不该出现指向它的按钮。
            StringAssert.DoesNotContain("client=1", response.Body);
        }

        /// <summary>在线时长列渲染成可读文本。</summary>
        [Test]
        public void 页面渲染在线时长()
        {
            var response = Handle("GET / HTTP/1.1", Local, out _);
            StringAssert.Contains("2 分 5 秒", response.Body);
        }

        /// <summary>动作名未登记时回 400，而不是"看起来成功了"。</summary>
        [Test]
        public void 未知动作被拒()
        {
            var response = Handle("GET /?action=drop-database HTTP/1.1", Local, out var actionTaken);

            Assert.AreEqual(400, response.StatusCode);
            Assert.IsNull(actionTaken);
        }

        /// <summary>处理器报错时，页面要显示原因而不是假装成功。</summary>
        [Test]
        public void 动作失败原因会显示出来()
        {
            var response = Handle("GET /?action=stop-room HTTP/1.1", Local, out _, "当前没有房间。");

            Assert.AreEqual(200, response.StatusCode);
            StringAssert.Contains("操作失败：当前没有房间。", response.Body);
        }

        /// <summary>时长文本：分钟与小时两种量级都要能读。</summary>
        [Test]
        public void 时长格式化()
        {
            Assert.AreEqual("2 分 5 秒", DashboardRenderer.FormatDuration(125f));
            Assert.AreEqual("1 小时 0 分", DashboardRenderer.FormatDuration(3600f));
            Assert.AreEqual("0 分 0 秒", DashboardRenderer.FormatDuration(-3f));
        }

        /// <summary>快照缺失时页面仍然可访问（服务器刚启动的一瞬间）。</summary>
        [Test]
        public void 没有快照时页面不崩()
        {
            var response = DashboardRouter.Handle("GET / HTTP/1.1", null, Local, null, null);

            Assert.AreEqual(200, response.StatusCode);
            StringAssert.Contains("RaidDemo", response.Body);
        }
    }
}
