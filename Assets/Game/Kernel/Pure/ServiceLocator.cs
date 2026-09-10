using System;
using System.Collections.Generic;

namespace RaidDemo.Kernel
{
    /// <summary>
    /// 服务定位器：集中管理跨模块共享的服务实例。
    /// </summary>
    /// <remarks>
    /// <para>为什么不用到处挂静态单例：静态单例的问题在于无法替换。
    /// 单元测试需要注入一个固定种子的随机数服务、或一个假的寻路服务时，静态单例会强迫测试
    /// 去修改全局状态，导致测试之间互相污染、执行顺序影响结果。定位器提供了注册与替换能力，
    /// 让测试实现与生产实现可以自由切换。</para>
    ///
    /// <para>使用边界（重要）：定位器只应用于<b>生命周期与整个应用相同</b>的服务，
    /// 例如时间服务、日志服务、随机数服务、对象池、事件总线。
    /// 业务数据（背包、战局状态、玩家进度）必须通过构造函数或显式参数传递，不要塞进定位器。
    /// 否则定位器会退化成全局变量袋，与它本来要解决的问题一样糟糕。</para>
    ///
    /// <para>本类不引用 UnityEngine，原因同 EventBus：服务端无头环境同样需要它。</para>
    /// </remarks>
    public sealed class ServiceLocator
    {
        /// <summary>已注册的服务表。键为服务接口类型。</summary>
        private readonly Dictionary<Type, object> m_Services = new Dictionary<Type, object>();

        /// <summary>服务创建工厂。仅对允许延迟创建的服务生效，用于避免启动时的无谓开销。</summary>
        private readonly Dictionary<Type, Func<object>> m_Factories = new Dictionary<Type, Func<object>>();

        /// <summary>
        /// 注册服务实现。
        /// </summary>
        /// <typeparam name="TService">服务接口或抽象类型，调用方按此类型获取。</typeparam>
        /// <param name="instance">服务实例。</param>
        /// <param name="overwrite">
        /// 是否允许覆盖已注册的同一服务。
        /// 默认 false 是刻意的：重复注册几乎总是逻辑错误（例如两个模块各自初始化了一遍），
        /// 静默覆盖会让这类问题潜伏到很久以后才暴露。
        /// </param>
        public void Register<TService>(TService instance, bool overwrite = false)
            where TService : class
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance), $"服务 {typeof(TService).Name} 的实例不能为 null。");
            }

            var type = typeof(TService);
            if (!overwrite && m_Services.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"服务 {type.Name} 已注册。若确实需要替换（例如测试注入），请显式传入 overwrite: true。");
            }

            m_Services[type] = instance;
            m_Factories.Remove(type);
        }

        /// <summary>
        /// 注册延迟创建的服务。首次获取时才执行工厂方法。
        /// </summary>
        /// <remarks>
        /// 适用于创建成本高、但并非每次运行都会用到的服务。
        /// 由于工厂在获取时才执行，若运行期间从未获取，则该服务永远不会被创建。
        /// </remarks>
        public void RegisterLazy<TService>(Func<TService> factory, bool overwrite = false)
            where TService : class
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory), "工厂方法不能为 null。");
            }

            var type = typeof(TService);
            if (!overwrite && (m_Services.ContainsKey(type) || m_Factories.ContainsKey(type)))
            {
                throw new InvalidOperationException(
                    $"服务 {type.Name} 已注册。若确实需要替换，请显式传入 overwrite: true。");
            }

            m_Services.Remove(type);
            m_Factories[type] = () => factory();
        }

        /// <summary>
        /// 获取服务实例。未注册时抛出异常。
        /// </summary>
        /// <remarks>
        /// 这里刻意不返回 null：静默返回 null 会让错误延迟到使用点才爆发，
        /// 而且丢失了究竟是哪一步没注册这一关键线索。直接抛异常可以让问题在获取点立刻暴露。
        /// </remarks>
        public TService Get<TService>()
            where TService : class
        {
            if (TryGet<TService>(out var service))
            {
                return service;
            }

            throw new InvalidOperationException(
                $"服务 {typeof(TService).Name} 尚未注册。请确认初始化流程已执行（见 Bootstrap 程序集）。");
        }

        /// <summary>
        /// 尝试获取服务实例。
        /// </summary>
        /// <param name="service">获取到的实例；未注册时为 null。</param>
        /// <returns>是否成功获取。</returns>
        public bool TryGet<TService>(out TService service)
            where TService : class
        {
            var type = typeof(TService);

            if (m_Services.TryGetValue(type, out var existing))
            {
                service = (TService)existing;
                return true;
            }

            if (m_Factories.TryGetValue(type, out var factory))
            {
                var created = factory();

                // 创建后立即转为常驻实例，避免每次获取都重新构造。
                m_Services[type] = created;
                m_Factories.Remove(type);
                service = (TService)created;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>查询服务是否已注册（含尚未创建的延迟注册）。</summary>
        public bool IsRegistered<TService>()
            where TService : class
        {
            var type = typeof(TService);
            return m_Services.ContainsKey(type) || m_Factories.ContainsKey(type);
        }

        /// <summary>移除指定服务的注册。若该服务实现了 IDisposable 则一并释放。</summary>
        /// <returns>是否确实移除了注册。</returns>
        public bool Unregister<TService>(bool dispose = true)
            where TService : class
        {
            var type = typeof(TService);
            var removedFactory = m_Factories.Remove(type);

            if (!m_Services.TryGetValue(type, out var instance))
            {
                return removedFactory;
            }

            m_Services.Remove(type);

            if (dispose && instance is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return true;
        }

        /// <summary>
        /// 清空全部注册。默认释放实现了 IDisposable 的服务。
        /// </summary>
        /// <remarks>
        /// 单元测试必须在每个用例开始前调用本方法，保证测试之间互不影响。
        /// </remarks>
        public void Clear(bool dispose = true)
        {
            if (dispose)
            {
                foreach (var pair in m_Services)
                {
                    if (pair.Value is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            m_Services.Clear();
            m_Factories.Clear();
        }

        /// <summary>已注册（含延迟注册）的服务数量，用于断言与调试。</summary>
        public int Count => m_Services.Count + m_Factories.Count;
    }
}
