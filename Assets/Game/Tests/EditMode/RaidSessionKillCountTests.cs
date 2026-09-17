using NUnit.Framework;
using RaidDemo.Raid;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 战局击杀计数的回归用例（U-100）。
    /// </summary>
    /// <remarks>
    /// 联机下"这一枪是不是本机玩家打的"由调用方（<c>SceneBootstrap.OnKillCounted</c>）按运行形态
    /// 判定：联机用客户端编号、单机用战斗单位编号。<see cref="RaidSession.NotifyKill"/> 只负责累计，
    /// 若它再拿编号和战斗单位编号比一次，联机里的击杀会全部被拒——右上角击杀数恒为 0。
    /// </remarks>
    [TestFixture]
    public sealed class RaidSessionKillCountTests
    {
        /// <summary>调用方判定过的击杀必须原样累计，不再被编号空间挡住。</summary>
        [Test]
        public void 击杀回调直接累计不再比较编号()
        {
            // 刻意用一个与战斗单位编号无关的值构造会话：旧实现会拿它和 m_PlayerCombatantId 比较，
            // 从而把击杀拒之门外。
            var session = new RaidSession(new RaidSettings(), null, playerCombatantId: 7);

            session.NotifyKill();
            session.NotifyKill();

            Assert.AreEqual(2, session.Kills, "调用方判定过的击杀必须原样累计。");
        }

        /// <summary>结算之后不再累计。</summary>
        [Test]
        public void 战局结束后不再累计()
        {
            var session = new RaidSession(new RaidSettings(), null, playerCombatantId: 7);
            session.NotifyKill();
            session.NotifyPlayerKilled();

            session.NotifyKill();

            Assert.AreEqual(1, session.Kills, "结算后不应再记击杀。");
        }
    }
}
