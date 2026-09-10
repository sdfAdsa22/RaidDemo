using System;
using System.Collections.Generic;

namespace RaidDemo.Kernel
{
    /// <summary>
    /// 通用对象池：复用对象以避免频繁分配导致的 GC 峰值。
    /// </summary>
    /// <typeparam name="T">被池化的对象类型。</typeparam>
    /// <remarks>
    /// <para>为什么需要它：本项目中会高频创建的对象包括子弹、命中特效、飘出的伤害数字，
    /// 以及背包界面里的物品格子。若每次都新建实例并在用完后丢弃，会造成明显的 GC 峰值，
    /// 表现为每隔几秒卡顿一下。对象池通过复用消除这部分分配。</para>
    ///
    /// <para>使用约定：从池中取出对象后，调用方必须保证在使用结束时归还（<see cref="Release"/>）。
    /// 推荐配合 <see cref="GetScope"/> 使用，这样即使中途抛出异常也能正确归还。</para>
    ///
    /// <para>本类不负责的事情：对象池只管理取出与归还这一件事，不负责对象的激活与失活、
    /// 位置重置、特效播放等表现层行为。那些属于 Unity 侧的封装。</para>
    /// </remarks>
    public sealed class ObjectPool<T> : IDisposable
        where T : class
    {
        private readonly Stack<T> m_Available;
        private readonly Func<T> m_Factory;
        private readonly Action<T> m_OnTake;
        private readonly Action<T> m_OnRelease;
        private readonly int m_MaxSize;

        private int m_TotalCreated;
        private bool m_IsDisposed;

        /// <summary>
        /// 创建对象池。
        /// </summary>
        /// <param name="factory">创建新实例的工厂方法，池中无可用对象时调用。不允许为 null。</param>
        /// <param name="maxSize">
        /// 池中保留的闲置对象上限。超出部分会被丢弃而不是无限增长。
        /// 默认 128 是本项目的经验值：单帧同时存在的子弹与特效通常远低于此数量。
        /// 若某类对象的高峰远超该值，应针对该类型单独调整，而不是全局调大。
        /// </param>
        /// <param name="prewarmCount">预热数量，在池创建时预先分配，用于避免首次使用时的分配尖峰。</param>
        /// <param name="onTake">取出时的回调，用于重置对象状态（例如清空集合、恢复默认值）。</param>
        /// <param name="onRelease">归还时的回调，用于清理对象上不应被复用的状态。</param>
        public ObjectPool(
            Func<T> factory,
            int maxSize = 128,
            int prewarmCount = 0,
            Action<T> onTake = null,
            Action<T> onRelease = null)
        {
            m_Factory = factory ?? throw new ArgumentNullException(nameof(factory), "工厂方法不能为 null。");

            if (maxSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSize), maxSize, "池容量上限必须大于 0。");
            }

            m_MaxSize = maxSize;
            m_OnTake = onTake;
            m_OnRelease = onRelease;
            m_Available = new Stack<T>(Math.Min(maxSize, Math.Max(prewarmCount, 4)));

            for (var i = 0; i < prewarmCount && i < maxSize; i++)
            {
                m_Available.Push(m_Factory());
                m_TotalCreated++;
            }
        }

        /// <summary>当前池中可复用的闲置对象数量。</summary>
        public int AvailableCount => m_Available.Count;

        /// <summary>本池累计创建过的对象总数。用于观测池容量设置是否合理。</summary>
        public int TotalCreated => m_TotalCreated;

        /// <summary>
        /// 从池中取出一个对象。池为空时创建新实例。
        /// </summary>
        public T Get()
        {
            ThrowIfDisposed();

            T item;
            if (m_Available.Count > 0)
            {
                item = m_Available.Pop();
            }
            else
            {
                item = m_Factory();
                m_TotalCreated++;
            }

            m_OnTake?.Invoke(item);
            return item;
        }

        /// <summary>
        /// 归还对象。
        /// </summary>
        /// <param name="item">要归还的对象；传入 null 会被忽略。</param>
        /// <remarks>
        /// 超出容量上限时对象被直接丢弃，交由 GC 回收。这是有意的取舍：
        /// 无限保留会让池在偶发高负载后长期占用内存，而收益很低。
        /// </remarks>
        public void Release(T item)
        {
            if (item == null || m_IsDisposed)
            {
                return;
            }

            m_OnRelease?.Invoke(item);

            if (m_Available.Count >= m_MaxSize)
            {
                return;
            }

            m_Available.Push(item);
        }

        /// <summary>
        /// 取出一个对象，并在作用域结束时自动归还。
        /// </summary>
        /// <remarks>
        /// 用法：<c>using (pool.GetScope(out var item)) { ... }</c>
        /// 离开作用域后自动归还，即使中途抛出异常也不会泄漏。
        /// </remarks>
        public Scope GetScope(out T item)
        {
            item = Get();
            return new Scope(this, item);
        }

        /// <summary>清空闲置对象。正在被外部使用的对象不受影响。</summary>
        public void Clear()
        {
            m_Available.Clear();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_IsDisposed = true;
            m_Available.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(ObjectPool<T>), "对象池已释放，不能再取出对象。");
            }
        }

        /// <summary>取出对象的自动归还作用域。</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly ObjectPool<T> m_Owner;
            private readonly T m_Item;

            internal Scope(ObjectPool<T> owner, T item)
            {
                m_Owner = owner;
                m_Item = item;
            }

            public void Dispose()
            {
                m_Owner?.Release(m_Item);
            }
        }
    }
}
