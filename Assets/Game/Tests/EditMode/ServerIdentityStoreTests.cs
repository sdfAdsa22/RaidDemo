using System.IO;
using NUnit.Framework;
using RaidDemo.Bootstrap;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 服务器侧账号库的测试：建号、登录、token 自动登录、落盘与不存明文。
    /// </summary>
    /// <remarks>
    /// <para>用临时目录而不是工程目录：账号文件属于运行时数据，
    /// 落进仓库会被误提交，也会让下一次运行读到上一次的残留（测试就不再独立）。</para>
    ///
    /// <para>这里的每条断言都对应一条"玩家能撞上的边界"：昵称被占用、口令格式、
    /// token 失效、换台机器重新登录。</para>
    /// </remarks>
    [TestFixture]
    public sealed class ServerIdentityStoreTests
    {
        private string m_Directory;

        [SetUp]
        public void SetUp()
        {
            m_Directory = Path.Combine(
                Application.temporaryCachePath,
                "RaidDemoIdentityTests",
                TestContext.CurrentContext.Test.ID.Replace('(', '_').Replace(')', '_'));

            if (Directory.Exists(m_Directory))
            {
                Directory.Delete(m_Directory, true);
            }

            Directory.CreateDirectory(m_Directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Directory))
            {
                Directory.Delete(m_Directory, true);
            }
        }

        [Test]
        public void TryLogin_NewNickname_CreatesAccountAndReturnsToken()
        {
            var store = new ServerIdentityStore(m_Directory);

            var ok = store.TryLogin("小明", "123456", out var error, out var detail, out var token, out var created);

            Assert.IsTrue(ok);
            Assert.AreEqual(LobbyError.None, error);
            Assert.IsTrue(created);
            Assert.IsTrue(LobbyLimits.IsValidToken(token));
            Assert.IsNotEmpty(detail);
            Assert.AreEqual(1, store.AccountCount);
        }

        [Test]
        public void TryLogin_ExistingNicknameWithCorrectPassphrase_LogsInWithoutCreating()
        {
            var store = new ServerIdentityStore(m_Directory);
            store.TryLogin("小明", "123456", out _, out _, out var firstToken, out _);

            var ok = store.TryLogin("小明", "123456", out var error, out _, out var token, out var created);

            Assert.IsTrue(ok);
            Assert.AreEqual(LobbyError.None, error);
            Assert.IsFalse(created);
            Assert.AreEqual(firstToken, token);
            Assert.AreEqual(1, store.AccountCount);
        }

        [Test]
        public void TryLogin_ExistingNicknameWithWrongPassphrase_ReportsNicknameTaken()
        {
            var store = new ServerIdentityStore(m_Directory);
            store.TryLogin("小明", "123456", out _, out _, out _, out _);

            var ok = store.TryLogin("小明", "999999", out var error, out var detail, out _, out _);

            Assert.IsFalse(ok);
            Assert.AreEqual(LobbyError.NicknameTaken, error);
            Assert.IsNotEmpty(detail);
        }

        [Test]
        public void TryLogin_WithSavedToken_SkipsPassphrase()
        {
            var store = new ServerIdentityStore(m_Directory);
            store.TryLogin("小红", "1234", out _, out _, out var token, out _);

            var ok = store.TryLogin("小红", token, out var error, out _, out var again, out var created);

            Assert.IsTrue(ok);
            Assert.AreEqual(LobbyError.None, error);
            Assert.IsFalse(created);
            Assert.AreEqual(token, again);
        }

        [Test]
        public void TryLogin_WithStaleToken_ReportsBadSecret()
        {
            var store = new ServerIdentityStore(m_Directory);
            store.TryLogin("小红", "1234", out _, out _, out _, out _);

            // 32 位十六进制但不是这个账号的 token：按"令牌失效"处理，提示重新输口令。
            var ok = store.TryLogin("小红", "0123456789abcdef0123456789abcdef", out var error, out var detail, out _, out _);

            Assert.IsFalse(ok);
            Assert.AreEqual(LobbyError.BadSecret, error);
            Assert.IsNotEmpty(detail);
        }

        [Test]
        public void TryLogin_FormatErrors_AreReported()
        {
            var store = new ServerIdentityStore(m_Directory);

            Assert.IsFalse(store.TryLogin("带|竖线", "1234", out var badName, out _, out _, out _));
            Assert.AreEqual(LobbyError.BadNickname, badName);

            Assert.IsFalse(store.TryLogin("小刚", "12", out var badPass, out _, out _, out _));
            Assert.AreEqual(LobbyError.BadPasswordFormat, badPass);
        }

        [Test]
        public void Accounts_SurviveReload()
        {
            var first = new ServerIdentityStore(m_Directory);
            first.TryLogin("小明", "123456", out _, out _, out var token, out _);

            var second = new ServerIdentityStore(m_Directory);

            Assert.AreEqual(1, second.AccountCount);
            Assert.IsTrue(second.TryLogin("小明", token, out _, out _, out _, out var created));
            Assert.IsFalse(created);
        }

        [Test]
        public void AccountsFile_DoesNotContainPlainPassphrase()
        {
            var store = new ServerIdentityStore(m_Directory);
            store.TryLogin("小明", "135790", out _, out _, out _, out _);

            var json = File.ReadAllText(store.FilePath);

            Assert.IsFalse(json.Contains("135790"), "账号文件里出现了明文口令。");
            Assert.IsTrue(json.Contains("Hash"), "账号文件里没有哈希字段。");
            Assert.IsTrue(json.Contains("Salt"), "账号文件里没有盐字段。");
        }

        [Test]
        public void 两段式登录_第一步不计算哈希_第二步用哈希收尾()
        {
            // AR-07：主线程只做便宜判断（格式 / token 快速路径），PBKDF2 交给调用方放进后台。
            var store = new ServerIdentityStore(m_Directory);

            var attempt = store.BeginLogin("小明", "123456");
            Assert.IsFalse(attempt.IsImmediate, "首次登录需要后台算哈希");
            Assert.IsTrue(attempt.CreatingAccount);
            Assert.IsNotNull(attempt.Salt, "第一步必须把盐准备好，后台线程只做纯计算");
            Assert.AreEqual(ServerIdentityStore.DefaultHashIterations, attempt.Iterations);

            var hash = ServerIdentityStore.ComputeHashHex(
                attempt.Passphrase, attempt.Salt, attempt.Iterations);
            var ok = store.CompleteLogin(
                attempt, hash, out var error, out var detail, out var token, out var created);

            Assert.IsTrue(ok, string.IsNullOrEmpty(detail) ? error.ToString() : detail);
            Assert.IsTrue(created);
            Assert.IsFalse(string.IsNullOrEmpty(token));

            // token 快速路径必须立即返回（不进后台队列）。
            var fast = store.BeginLogin("小明", token);
            Assert.IsTrue(fast.IsImmediate, "token 自动登录不应再算一次哈希");
            Assert.IsTrue(fast.ImmediateSuccess);
        }

        [Test]
        public void 两段式登录_错误口令在收尾阶段被拒()
        {
            var store = new ServerIdentityStore(m_Directory);

            var first = store.BeginLogin("小红", "1234");
            var firstHash = ServerIdentityStore.ComputeHashHex(
                first.Passphrase, first.Salt, first.Iterations);
            Assert.IsTrue(store.CompleteLogin(first, firstHash, out _, out _, out _, out _));

            var attempt = store.BeginLogin("小红", "9999");
            Assert.IsFalse(attempt.IsImmediate);
            var wrongHash = ServerIdentityStore.ComputeHashHex(
                attempt.Passphrase, attempt.Salt, attempt.Iterations);
            var ok = store.CompleteLogin(attempt, wrongHash, out var error, out _, out _, out _);

            Assert.IsFalse(ok);
            Assert.AreEqual(LobbyError.NicknameTaken, error);
        }
    }
}
