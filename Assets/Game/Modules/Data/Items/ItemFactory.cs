using System;

namespace RaidDemo.Data
{
    /// <summary>
    /// 物品实例工厂。所有 <see cref="ItemInstance"/> 的创建都经过这里，以保证实例号唯一。
    /// </summary>
    /// <remarks>
    /// 它刻意做成普通类而不是静态工具类：联机时服务端与客户端各自持有自己的工厂，
    /// 实例号也各自分配，不会因为两个进程共用一个静态计数器而产生难以复现的冲突。
    /// </remarks>
    public sealed class ItemFactory
    {
        /// <summary>
        /// 按定义创建一个物品实例。
        /// </summary>
        /// <param name="definition">静态定义，不允许为 null。</param>
        /// <param name="count">数量，必须大于 0；超过 <c>MaxStack</c> 时会被截断到上限。</param>
        /// <returns>新的物品实例。</returns>
        /// <remarks>
        /// 超量请求采用截断而不是抛异常：调用方多来自掉落表或存档载入这类数据驱动路径，
        /// 一次配置错误不应该让整个战局启动失败。截断是保守的——它只会少给，不会凭空多给。
        /// </remarks>
        public ItemInstance Create(IItemDefinition definition, int count = 1)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition), "物品定义不能为 null。");
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "物品数量必须大于 0。");
            }

            var actual = Math.Min(count, Math.Max(1, definition.MaxStack));
            return new ItemInstance(definition, ItemInstanceIdAllocator.Next(), actual);
        }
    }

    /// <summary>
    /// 物品实例号分配器。
    /// </summary>
    /// <remarks>
    /// 进程内单调递增。Unity 的主线程是唯一的游戏逻辑执行线程，因此这里不需要加锁；
    /// 联机时由服务端分配实例号，客户端只接收，不做本地分配。
    /// </remarks>
    internal static class ItemInstanceIdAllocator
    {
        /// <summary>下一个可用的实例号。从 1 开始，0 保留为"无实例"。</summary>
        private static int s_Next = 1;

        /// <summary>取一个新的实例号。</summary>
        public static int Next()
        {
            return s_Next++;
        }
    }
}
