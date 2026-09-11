using NUnit.Framework;
using RaidDemo.AI;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 感知记忆测试：记录、刷新、过期。
    /// </summary>
    /// <remarks>
    /// 记忆的时长决定了"跑两步就能摆脱"还是"被追到天涯海角"，
    /// 因此它的边界（恰好到期、刷新后再计时）必须有明确断言。
    /// </remarks>
    [TestFixture]
    public sealed class AiSensesMemoryTests
    {
        [Test]
        public void EmptyMemory_IsNotFresh()
        {
            var memory = new AISensesMemory();

            Assert.IsFalse(memory.HasMemory);
            Assert.IsFalse(memory.IsFresh(now: 100f, memorySeconds: 5f), "没有记录过任何东西时不应当被认为是新鲜的。");
            Assert.IsTrue(float.IsPositiveInfinity(memory.TimeSinceLastKnown(100f)));
        }

        [Test]
        public void Remember_StoresPositionAndTime()
        {
            var memory = new AISensesMemory();
            memory.Remember(new Vector2F(3f, 4f), now: 12f);

            Assert.IsTrue(memory.HasMemory);
            Assert.AreEqual(new Vector2F(3f, 4f), memory.LastKnownPosition);
            Assert.AreEqual(12f, memory.LastKnownTime);
            Assert.AreEqual(0f, memory.TimeSinceLastKnown(12f));
        }

        [Test]
        public void IsFresh_ExpiresAfterMemorySeconds()
        {
            var memory = new AISensesMemory();
            memory.Remember(new Vector2F(1f, 1f), now: 0f);

            Assert.IsTrue(memory.IsFresh(5f, memorySeconds: 5f), "恰好到期时仍算新鲜（用 <= 判定）。");
            Assert.IsFalse(memory.IsFresh(5.01f, memorySeconds: 5f), "超过记忆时长后应当失效。");
        }

        [Test]
        public void Remember_RefreshesTimer()
        {
            var memory = new AISensesMemory();
            memory.Remember(new Vector2F(1f, 1f), now: 0f);
            memory.Remember(new Vector2F(9f, 9f), now: 4f);

            Assert.IsTrue(memory.IsFresh(8f, memorySeconds: 5f), "再次看到目标应当重新开始计时。");
            Assert.AreEqual(new Vector2F(9f, 9f), memory.LastKnownPosition, "位置应当被新观测覆盖。");
        }

        [Test]
        public void DiscardIfExpired_ClearsOnlyWhenStale()
        {
            var memory = new AISensesMemory();
            memory.Remember(new Vector2F(2f, 2f), now: 0f);

            Assert.IsFalse(memory.DiscardIfExpired(1f, memorySeconds: 5f), "还没过期时不应当被清掉。");
            Assert.IsTrue(memory.IsFresh(1f, 5f));

            Assert.IsTrue(memory.DiscardIfExpired(6f, memorySeconds: 5f), "过期后应当被清掉。");
            Assert.IsFalse(memory.HasMemory);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var memory = new AISensesMemory();
            memory.Remember(new Vector2F(2f, 2f), now: 0f);
            memory.Clear();

            Assert.IsFalse(memory.HasMemory);
            Assert.AreEqual(Vector2F.Zero, memory.LastKnownPosition);
        }
    }
}
