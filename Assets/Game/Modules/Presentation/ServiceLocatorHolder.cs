using RaidDemo.Kernel;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 场景级服务访问入口。
    /// </summary>
    /// <remarks>
    /// 表现层组件需要在 OnEnable 阶段拿到事件总线，但此时场景启动流程可能尚未执行完。
    /// 本类提供一个静态入口，使组件能安全查询服务是否已就绪，而不会抛异常。
    ///
    /// 它刻意只暴露最小接口，不作为通用全局访问点使用。
    /// 业务数据仍须通过构造函数或显式参数传递，不能塞进这里。
    /// </remarks>
    public static class ServiceLocatorHolder
    {
        private static ServiceLocator s_Locator;

        /// <summary>当前场景使用的服务定位器。未初始化时为 null。</summary>
        public static ServiceLocator Current => s_Locator;

        /// <summary>设置当前场景的服务定位器。由场景启动流程调用。</summary>
        public static void Set(ServiceLocator locator)
        {
            s_Locator = locator;
        }

        /// <summary>
        /// 尝试获取指定服务。
        /// </summary>
        /// <returns>服务已注册且可用时返回 true。</returns>
        public static bool TryGet<TService>(out TService service)
            where TService : class
        {
            if (s_Locator == null)
            {
                service = null;
                return false;
            }

            return s_Locator.TryGet(out service);
        }

        /// <summary>清空引用。场景卸载或测试结束时调用，避免残留引用。</summary>
        public static void Clear()
        {
            s_Locator = null;
        }
    }
}
