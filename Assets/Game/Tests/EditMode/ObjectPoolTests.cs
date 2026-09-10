using System;
using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Kernel;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>对象池的行为测试。</summary>
    [TestFixture]
    public sealed class ObjectPoolTests
    {
        /// <summary>模拟被池化的对象，例如一颗子弹。</summary>
        private sealed class Bullet
        {
            public int Id;
            public bool IsActive;
        }

        [Test]
        public void Get_WhenEmpty_CreatesNewInstance()
        {
            var created = 0;
            using var pool = new ObjectPool<Bullet>(() =>
            {
                created++;
                return new Bullet();
            });

            var bullet = pool.Get();

            Assert.IsNotNull(bullet);
            Assert.AreEqual(1, created);
            Assert.AreEqual(0, pool.AvailableCount, "取出的对象不应仍留在池中。");
        }

        [Test]
        public void Release_ThenGet_ReusesSameInstance()
        {
            using var pool = new ObjectPool<Bullet>(() => new Bullet());

            var first = pool.Get();
            pool.Release(first);
            var second = pool.Get();

            Assert.AreSame(first, second, "归还后的对象应被复用，而不是重新创建。");
            Assert.AreEqual(1, pool.TotalCreated);
        }

        [Test]
        public void Prewarm_CreatesInstancesUpFront()
        {
            var created = 0;
            using var pool = new ObjectPool<Bullet>(
                () =>
                {
                    created++;
                    return new Bullet();
                },
                prewarmCount: 5);

            Assert.AreEqual(5, created, "预热应在构造时一次性创建指定数量的对象。");
            Assert.AreEqual(5, pool.AvailableCount);
        }

        [Test]
        public void Release_BeyondMaxSize_DiscardsInstance()
        {
            using var pool = new ObjectPool<Bullet>(() => new Bullet(), maxSize: 2);

            var a = pool.Get();
            var b = pool.Get();
            var c = pool.Get();

            pool.Release(a);
            pool.Release(b);
            pool.Release(c);

            Assert.AreEqual(2, pool.AvailableCount, "超出容量上限的对象应被丢弃，避免池无限增长。");
        }

        [Test]
        public void Release_Null_IsIgnored()
        {
            using var pool = new ObjectPool<Bullet>(() => new Bullet());

            Assert.DoesNotThrow(() => pool.Release(null));
            Assert.AreEqual(0, pool.AvailableCount);
        }

        [Test]
        public void OnTake_And_OnRelease_AreInvoked()
        {
            var takeCount = 0;
            var releaseCount = 0;

            using var pool = new ObjectPool<Bullet>(
                () => new Bullet(),
                onTake: _ => takeCount++,
                onRelease: _ => releaseCount++);

            var bullet = pool.Get();
            pool.Release(bullet);

            Assert.AreEqual(1, takeCount);
            Assert.AreEqual(1, releaseCount);
        }

        /// <summary>
        /// 取出回调应当把对象恢复到初始状态。
        /// </summary>
        /// <remarks>
        /// 这是对象池最容易出错的地方：忘记重置状态会让上一次使用留下的数据泄漏到下一次，
        /// 表现为难以复现的随机故障。
        /// </remarks>
        [Test]
        public void OnTake_ResetsStateBetweenUses()
        {
            using var pool = new ObjectPool<Bullet>(
                () => new Bullet(),
                onTake: bullet => bullet.IsActive = false);

            var first = pool.Get();
            first.IsActive = true;
            first.Id = 99;
            pool.Release(first);

            var second = pool.Get();

            Assert.IsFalse(second.IsActive, "取出回调应重置状态，避免上次使用的残留数据影响下一次。");
        }

        [Test]
        public void GetScope_ReturnsObjectToPoolOnDispose()
        {
            using var pool = new ObjectPool<Bullet>(() => new Bullet());

            using (pool.GetScope(out var bullet))
            {
                Assert.IsNotNull(bullet);
                Assert.AreEqual(0, pool.AvailableCount);
            }

            Assert.AreEqual(1, pool.AvailableCount, "离开作用域后对象应自动归还。");
        }

        [Test]
        public void GetScope_ReturnsObjectEvenWhenExceptionThrown()
        {
            using var pool = new ObjectPool<Bullet>(() => new Bullet());

            Assert.Throws<InvalidOperationException>(() =>
            {
                using (pool.GetScope(out _))
                {
                    throw new InvalidOperationException("模拟使用过程中出错");
                }
            });

            Assert.AreEqual(1, pool.AvailableCount, "即使中途抛异常，对象也应被归还，避免池逐渐枯竭。");
        }

        [Test]
        public void Dispose_ThenGet_Throws()
        {
            var pool = new ObjectPool<Bullet>(() => new Bullet());
            pool.Dispose();

            Assert.Throws<ObjectDisposedException>(() => pool.Get());
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new ObjectPool<Bullet>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<Bullet>(() => new Bullet(), maxSize: 0));
        }

        [Test]
        public void ManyGetReleaseCycles_DoNotGrowTotalCreated()
        {
            using var pool = new ObjectPool<Bullet>(() => new Bullet(), maxSize: 8);
            var reused = 0;
            var list = new List<Bullet>();

            // 模拟连续发射 200 发子弹：只要并发数量不超过池容量，就不应再创建新对象。
            for (var i = 0; i < 200; i++)
            {
                var bullet = pool.Get();
                list.Add(bullet);
                pool.Release(bullet);

                if (i > 0)
                {
                    reused++;
                }
            }

            Assert.Greater(reused, 0);
            Assert.AreEqual(1, pool.TotalCreated, "池容量足够时不应反复创建新对象，这正是消除 GC 峰值的关键。");
        }
    }
}
