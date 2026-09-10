using System;
using NUnit.Framework;
using RaidDemo.Kernel;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>确定性随机数生成器的测试。</summary>
    /// <remarks>
    /// 这些用例的核心不是验证随机分布是否好看，而是验证确定性：
    /// 相同种子必须产生完全相同的序列。联机环境下客户端与服务端依赖这一点达成一致。
    /// </remarks>
    [TestFixture]
    public sealed class DeterministicRandomTests
    {
        private const uint TestSeed = 20260910u;

        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            var first = new DeterministicRandom(TestSeed);
            var second = new DeterministicRandom(TestSeed);

            for (var i = 0; i < 100; i++)
            {
                Assert.AreEqual(
                    first.NextFloat(),
                    second.NextFloat(),
                    0f,
                    $"第 {i} 个随机数不一致。相同种子必须产生相同序列，否则联机两端会失去同步。");
            }
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            var first = new DeterministicRandom(1u);
            var second = new DeterministicRandom(2u);

            var differences = 0;
            for (var i = 0; i < 32; i++)
            {
                if (!first.NextFloat().Equals(second.NextFloat()))
                {
                    differences++;
                }
            }

            Assert.Greater(differences, 20, "不同种子应产生明显不同的序列。");
        }

        [Test]
        public void Reseed_RestartsSequence()
        {
            var generator = new DeterministicRandom(TestSeed);
            var baseline = new float[16];
            for (var i = 0; i < baseline.Length; i++)
            {
                baseline[i] = generator.NextFloat();
            }

            generator.Reseed(TestSeed);

            for (var i = 0; i < baseline.Length; i++)
            {
                Assert.AreEqual(
                    baseline[i],
                    generator.NextFloat(),
                    0f,
                    "重置为相同种子后应重现完全相同的序列，这是读档与回放的基础。");
            }
        }

        [Test]
        public void NextFloat_StaysInUnitRange()
        {
            var generator = new DeterministicRandom(TestSeed);

            for (var i = 0; i < 5000; i++)
            {
                var value = generator.NextFloat();
                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f, "随机值必须严格小于 1，否则按比例换算时会产生越界。");
            }
        }

        [Test]
        public void NextInt_StaysWithinRequestedRange()
        {
            var generator = new DeterministicRandom(TestSeed);

            for (var i = 0; i < 5000; i++)
            {
                var value = generator.NextInt(3, 8);
                Assert.GreaterOrEqual(value, 3);
                Assert.Less(value, 8);
            }
        }

        [Test]
        public void NextInt_WithEmptyRange_ReturnsMin()
        {
            var generator = new DeterministicRandom(TestSeed);

            Assert.AreEqual(5, generator.NextInt(5, 5), "空区间应返回下界而不是抛异常或产生越界值。");
        }

        [Test]
        public void NextFloat_WithRange_StaysWithinBounds()
        {
            var generator = new DeterministicRandom(TestSeed);

            for (var i = 0; i < 5000; i++)
            {
                var value = generator.NextFloat(-2f, 4f);
                Assert.GreaterOrEqual(value, -2f);
                Assert.LessOrEqual(value, 4f);
            }
        }

        [Test]
        public void NextChance_WithZeroAlwaysFalse()
        {
            var generator = new DeterministicRandom(TestSeed);

            for (var i = 0; i < 100; i++)
            {
                Assert.IsFalse(generator.NextChance(0f));
            }
        }

        [Test]
        public void NextChance_WithOneAlwaysTrue()
        {
            var generator = new DeterministicRandom(TestSeed);

            for (var i = 0; i < 100; i++)
            {
                Assert.IsTrue(generator.NextChance(1f));
            }
        }

        [Test]
        public void NextChance_RoughlyMatchesProbability()
        {
            var generator = new DeterministicRandom(TestSeed);
            var hits = 0;
            const int trials = 20000;

            for (var i = 0; i < trials; i++)
            {
                if (generator.NextChance(0.25f))
                {
                    hits++;
                }
            }

            // 允许 ±2% 的偏差：这是伪随机数在有限样本下的正常波动范围。
            var ratio = (float)hits / trials;
            Assert.That(ratio, Is.InRange(0.23f, 0.27f), $"实际命中率 {ratio:P1} 偏离设定值 25% 过多。");
        }

        [Test]
        public void NextWeightedIndex_RespectsWeights()
        {
            var generator = new DeterministicRandom(TestSeed);
            var weights = new[] { 70, 20, 10 };
            var counts = new int[3];
            const int trials = 30000;

            for (var i = 0; i < trials; i++)
            {
                counts[generator.NextWeightedIndex(weights)]++;
            }

            Assert.That((float)counts[0] / trials, Is.InRange(0.67f, 0.73f), "高权重项应占约 70%。");
            Assert.That((float)counts[1] / trials, Is.InRange(0.17f, 0.23f), "中权重项应占约 20%。");
            Assert.That((float)counts[2] / trials, Is.InRange(0.07f, 0.13f), "低权重项应占约 10%。");
        }

        [Test]
        public void NextWeightedIndex_IsDeterministic()
        {
            var weights = new[] { 5, 15, 80 };
            var first = new DeterministicRandom(TestSeed);
            var second = new DeterministicRandom(TestSeed);

            for (var i = 0; i < 200; i++)
            {
                Assert.AreEqual(
                    first.NextWeightedIndex(weights),
                    second.NextWeightedIndex(weights),
                    "战利品抽取必须可复现，否则联机时两端会掉落不同的物品。");
            }
        }

        [Test]
        public void NextWeightedIndex_ZeroTotalWeights_ReturnsFirst()
        {
            var generator = new DeterministicRandom(TestSeed);
            var weights = new[] { 0, 0, 0 };

            Assert.AreEqual(
                0,
                generator.NextWeightedIndex(weights),
                "权重全为零属于配置数据问题，应返回首项而不是抛异常导致战局崩溃。");
        }

        [Test]
        public void NextWeightedIndex_EmptyWeights_ReturnsMinusOne()
        {
            var generator = new DeterministicRandom(TestSeed);

            Assert.AreEqual(-1, generator.NextWeightedIndex(ReadOnlySpan<int>.Empty));
        }

        [Test]
        public void NextWeightedIndex_SingleWeight_ReturnsZero()
        {
            var generator = new DeterministicRandom(TestSeed);

            Assert.AreEqual(0, generator.NextWeightedIndex(new[] { 42 }));
        }
    }
}
