using NUnit.Framework;
using RaidDemo.Presentation;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 音效触发规则的单元测试。
    /// </summary>
    /// <remarks>
    /// 声音本身无法断言，但"什么时候该响"可以。这一层恰恰是最容易出错的部分：
    /// 重复播、该响时不响、跑起来脚步比子弹还密——这些都是纯逻辑，能测就应该测。
    /// </remarks>
    [TestFixture]
    public sealed class AudioPlaybackRulesTests
    {
        [Test]
        public void 占两格及以下判为短枪_三格及以上判为长枪()
        {
            Assert.AreEqual(WeaponPresentationKind.Pistol, AudioPlaybackRules.ResolveWeaponKind(0));
            Assert.AreEqual(WeaponPresentationKind.Pistol, AudioPlaybackRules.ResolveWeaponKind(2));
            Assert.AreEqual(WeaponPresentationKind.Rifle, AudioPlaybackRules.ResolveWeaponKind(3));
            Assert.AreEqual(WeaponPresentationKind.Rifle, AudioPlaybackRules.ResolveWeaponKind(5));
        }

        [Test]
        public void 从未播放过的音效不会被节流()
        {
            Assert.IsFalse(AudioPlaybackRules.ShouldThrottle(-1f, 100f, 0.035f));
        }

        [Test]
        public void 间隔不足时同一条音效被丢弃()
        {
            Assert.IsTrue(AudioPlaybackRules.ShouldThrottle(10f, 10.01f, 0.035f));
        }

        [Test]
        public void 间隔足够时同一条音效照常播放()
        {
            Assert.IsFalse(AudioPlaybackRules.ShouldThrottle(10f, 10.05f, 0.035f));
        }

        [Test]
        public void 草地材质判定为草地()
        {
            Assert.AreEqual(FootstepSurface.Grass, AudioPlaybackRules.ResolveSurface("M_TerrainGrass"));
        }

        [Test]
        public void 非草地材质一律判定为硬地()
        {
            Assert.AreEqual(FootstepSurface.Hard, AudioPlaybackRules.ResolveSurface("M_Prop_Container_A"));
            Assert.AreEqual(FootstepSurface.Hard, AudioPlaybackRules.ResolveSurface("M_BasinClay"));
        }

        [Test]
        public void 材质名为空时回落到草地()
        {
            Assert.AreEqual(FootstepSurface.Grass, AudioPlaybackRules.ResolveSurface(null));
            Assert.AreEqual(FootstepSurface.Grass, AudioPlaybackRules.ResolveSurface(string.Empty));
        }

        [Test]
        public void 站定不动不会产生脚步()
        {
            var cadence = new FootstepCadence();
            for (var i = 0; i < 100; i++)
            {
                Assert.IsFalse(cadence.Advance(0f, 0.02f), "速度为 0 时不应触发脚步。");
            }
        }

        [Test]
        public void 按走过的距离触发脚步而不是按时间()
        {
            var cadence = new FootstepCadence();
            var steps = 0;
            // 以 2 米/秒走 3 秒 = 6 米，步幅 0.95 米，因此大约 6 步，而不是按时间算出的十几步。
            for (var i = 0; i < 150; i++)
            {
                if (cadence.Advance(2f, 0.02f))
                {
                    steps++;
                }
            }

            Assert.That(steps, Is.InRange(5, 7), $"3 秒步行 6 米应产生约 6 步，实际 {steps} 步。");
        }

        [Test]
        public void 奔跑时脚步比步行更密但不失控()
        {
            var walk = CountSteps(3f, 3f);
            var sprint = CountSteps(6f, 3f);

            Assert.Greater(sprint, walk, "跑得更快时脚步应当更密。");
            Assert.LessOrEqual(sprint / 3f, 5.1f, "再快也不该超过每秒 5 步，否则听起来像机关枪。");
        }

        [Test]
        public void 站定后累计距离清零_起步不会立刻补一步()
        {
            var cadence = new FootstepCadence();

            // 先走一小段（不足一个步幅），然后站定。
            for (var i = 0; i < 10; i++)
            {
                cadence.Advance(2f, 0.02f);
            }

            cadence.Advance(0f, 0.02f);

            // 再次起步的第一步不应立刻触发——累计距离已经被清零。
            Assert.IsFalse(cadence.Advance(2f, 0.02f));
        }

        private static int CountSteps(float speed, float seconds)
        {
            var cadence = new FootstepCadence();
            var steps = 0;
            var ticks = (int)(seconds / 0.02f);
            for (var i = 0; i < ticks; i++)
            {
                if (cadence.Advance(speed, 0.02f))
                {
                    steps++;
                }
            }

            return steps;
        }
    }
}
