using System;
using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 会话作用域：一局「权威世界」所需的会话级服务及其生命周期。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要这一层（M9 · P0）：</b>在 M0~M8 里，事件总线、服务定位器与命令路由
    /// 由场景启动类各自 <c>new</c> 出来，生命周期跟着场景走。联机要求同一套权威逻辑既能跑在
    /// 客户端进程里（单机 = 本机内嵌服务器），也能跑在没有场景、没有表现层的服务器进程里
    /// （专用服务器）。把「会话」抽成独立对象之后，**谁创建它、谁决定它的生死**，
    /// 场景只是它的一个使用者，而不是它的宿主。</para>
    ///
    /// <para><b>职责边界：</b>本类只创建并持有会话级服务，不装配任何具体系统。
    /// 背包、战斗、AI、战局这些系统仍由各自的装配步骤注册进 <see cref="Services"/>。
    /// 这样单机与服务器可以复用同一批服务，只在「装配哪些系统」上分叉。</para>
    ///
    /// <para><b>与静态入口的关系：</b>表现层组件（<c>PlayerMotor</c>、<c>AudioService</c> 等）
    /// 在 <c>OnEnable</c> 阶段通过 <see cref="ServiceLocatorHolder"/> 查询服务，
    /// 因此会话创建时写入、释放时清空，避免跨场景或跨会话残留引用。</para>
    ///
    /// <para>线程约束：与本项目其余部分一致，只在主线程使用。</para>
    /// </remarks>
    public sealed class SessionScope : IDisposable
    {
        /// <summary>会话创建者标签，仅用于日志区分（单机内嵌 / 专用服务器 / 测试）。</summary>
        public string Owner { get; }

        /// <summary>会话级事件总线。跨场景存活，但随会话一起销毁。</summary>
        public EventBus Events { get; }

        /// <summary>会话级服务定位器。表现层通过 <see cref="ServiceLocatorHolder"/> 访问。</summary>
        public ServiceLocator Services { get; }

        /// <summary>会话级命令路由。单机时输入直接派发，联机时输入来自网络。</summary>
        public CommandRouter Commands { get; }

        /// <summary>会话级日志服务。服务器模式下调高等级可以只看关键事件。</summary>
        public LogService Log { get; }

        /// <summary>是否已释放。释放后不应再向本会话注册系统。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// 创建一个会话作用域，并把它注册为当前场景的静态访问入口。
        /// </summary>
        /// <param name="owner">创建者标签，写进日志便于区分是哪个宿主起的会话。</param>
        /// <param name="minimumLogLevel">最低日志等级。</param>
        public SessionScope(string owner, LogLevel minimumLogLevel = LogLevel.Info)
        {
            Owner = string.IsNullOrWhiteSpace(owner) ? "未命名会话" : owner;

            Events = new EventBus();
            Services = new ServiceLocator();
            Commands = new CommandRouter();
            Log = new LogService(minimumLogLevel);

            Services.Register(Events);
            Services.Register(Commands);
            Services.Register(Log);

            ServiceLocatorHolder.Set(Services);

            Log.Info($"[会话] 创建：{Owner}");
        }

        /// <summary>
        /// 单机模式的会话：权威逻辑跑在客户端进程内（本机内嵌服务器）。
        /// </summary>
        /// <remarks>
        /// 单机与联机共用同一条代码路径——差别只在「谁来驱动会话」：
        /// 单机由本地帧循环驱动，联机由服务器进程驱动、客户端通过网络参与。
        /// </remarks>
        public static SessionScope CreateLocal()
        {
            return new SessionScope("本机内嵌服务器");
        }

        /// <summary>
        /// 专用服务器进程的会话：无场景、无表现层，只让权威逻辑运行。
        /// </summary>
        public static SessionScope CreateDedicatedServer(LogLevel minimumLogLevel = LogLevel.Info)
        {
            return new SessionScope("专用服务器", minimumLogLevel);
        }

        /// <summary>释放后仍被使用属于编码错误，调用方据此尽早暴露问题。</summary>
        public void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(SessionScope), $"会话「{Owner}」已释放。");
            }
        }

        /// <summary>
        /// 释放会话：清空静态入口并按注册逆序释放服务。
        /// </summary>
        /// <remarks>
        /// <para>只有当静态入口仍然指向本会话时才清空它——场景重载时新旧会话可能短暂共存，
        /// 无条件清空会把新会话的入口误删，表现为「第二局开始表现层收不到事件」。</para>
        ///
        /// <para>本方法可重复调用：第二次起直接返回，避免卸载路径上重复释放。</para>
        /// </remarks>
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            if (ReferenceEquals(ServiceLocatorHolder.Current, Services))
            {
                ServiceLocatorHolder.Clear();
            }

            Services.Clear();

            Log.Info($"[会话] 释放：{Owner}");
        }
    }
}
