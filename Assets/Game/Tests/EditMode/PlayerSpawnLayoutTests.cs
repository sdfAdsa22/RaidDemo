using NUnit.Framework;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 联机出生点排布规则测试。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-22。服务器与客户端各算各的出生点时，
    /// 客户端要等第一帧快照才发现自己站错了位置，对账硬吸附就是玩家看到的"一进图被拉一下"。
    /// 这里钉住双方共用的规则本身：只要两个调用方都走 <see cref="PlayerSpawnLayout"/>，
    /// 就不会再出现两套偏移量。</para>
    /// </remarks>
    [TestFixture]
    public sealed class PlayerSpawnLayoutTests
    {
        private static readonly Vector2F Base = new Vector2F(-16.8f, -24.1f);

        /// <summary>战局的前四名玩家沿横向按 1.6 米排开，纵向不偏移。</summary>
        [Test]
        public void 战局前四名玩家沿横向按间距排开()
        {
            for (var i = 0; i < 4; i++)
            {
                var position = PlayerSpawnLayout.RaidPosition(Base, i);
                Assert.AreEqual(
                    Base.X + (i * PlayerSpawnLayout.RaidSpacing),
                    position.X,
                    1e-5f,
                    $"玩家 {i} 的横向位置");
                Assert.AreEqual(Base.Y, position.Y, 1e-5f, $"玩家 {i} 不应有纵向偏移");
            }
        }

        /// <summary>第五名玩家换到第二行，避免与第一名叠在一起。</summary>
        [Test]
        public void 战局第五名玩家换到第二行()
        {
            var position = PlayerSpawnLayout.RaidPosition(Base, 4);
            Assert.AreEqual(Base.X, position.X, 1e-5f);
            Assert.AreEqual(Base.Y + PlayerSpawnLayout.RaidSpacing, position.Y, 1e-5f);
        }

        /// <summary>编号缺失（负数）时退化为基准点，而不是抛异常或算到反向。</summary>
        [Test]
        public void 战局负编号退化为基准点()
        {
            var position = PlayerSpawnLayout.RaidPosition(Base, -1);
            Assert.AreEqual(Base.X, position.X, 1e-5f);
            Assert.AreEqual(Base.Y, position.Y, 1e-5f);
        }

        /// <summary>安全屋四人以中轴左右摊开：-2.1 / -0.7 / +0.7 / +2.1 米。</summary>
        [Test]
        public void 安全屋四人以中轴左右摊开()
        {
            var expected = new[] { -2.1f, -0.7f, 0.7f, 2.1f };
            for (var i = 0; i < expected.Length; i++)
            {
                var position = PlayerSpawnLayout.SafeHousePosition(new Vector2F(0f, -5f), i, 4);
                Assert.AreEqual(expected[i], position.X, 1e-4f, $"玩家 {i} 的横向偏移");
                Assert.AreEqual(-5f, position.Y, 1e-5f, "安全屋出生只在横向摊开");
            }
        }

        /// <summary>超出人数上限的编号被夹到最右侧，不允许算出房间之外的位置。</summary>
        [Test]
        public void 安全屋超出上限的编号被夹到最右侧()
        {
            var position = PlayerSpawnLayout.SafeHousePosition(new Vector2F(0f, -5f), 9, 4);
            Assert.AreEqual(2.1f, position.X, 1e-4f);
        }

        /// <summary>单人房间时中轴就是基准点，不产生任何偏移。</summary>
        [Test]
        public void 单人房间里中轴落在基准点()
        {
            var position = PlayerSpawnLayout.SafeHousePosition(new Vector2F(0f, -5f), 0, 1);
            Assert.AreEqual(0f, position.X, 1e-5f);
        }
    }
}
