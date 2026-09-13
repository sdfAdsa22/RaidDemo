using System;
using RaidDemo.Inventory;
using RaidDemo.Kernel;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 图鉴点亮的自动化：监听背包变更事件，物品进入玩家容器时立刻补记图鉴条目。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么光靠 <see cref="MetaProgress.RefreshCodex"/> 不够：</b>
    /// 那个方法只回答"玩家现在手里有什么"。战局里搜到的东西如果在这一局内死亡丢失，
    /// 就永远没机会被下一次扫描看到——而图鉴记录的是"曾经拿到过"，
    /// 捡起来的那一刻就应该点亮，哪怕随后就阵亡。</para>
    ///
    /// <para><b>为什么按容器种类过滤而不是按容器 ID：</b>容器 ID 每个场景都会重新分配，
    /// 同样的"背包"在安全屋和战局里的 ID 不同；种类是稳定语义。
    /// 只对玩家名下的三类容器（背包、仓库、弹药挂）响应，箱子被搜空之类的世界事件不会触发扫描。</para>
    ///
    /// <para>本类不改存档、不碰界面：发现新条目后由 <see cref="MetaProgress.RefreshCodex"/>
    /// 广播既有的变化事件，自动存档与界面刷新照常各自响应。</para>
    /// </remarks>
    public sealed class CodexMarker : IDisposable
    {
        private readonly MetaProgress m_Progress;
        private readonly ContainerRegistry m_Registry;
        private IDisposable m_Subscription;

        /// <summary>
        /// 创建图鉴点亮器并开始监听背包变更。
        /// </summary>
        /// <param name="progress">局外进度；为 null 时本对象退化为空操作。</param>
        /// <param name="registry">场景的容器注册表，用于查询容器种类。</param>
        /// <param name="eventBus">事件总线；为 null 时不建立订阅。</param>
        public CodexMarker(MetaProgress progress, ContainerRegistry registry, EventBus eventBus)
        {
            m_Progress = progress;
            m_Registry = registry;
            if (eventBus != null)
            {
                m_Subscription = eventBus.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
            }
        }

        /// <summary>解除订阅。场景卸载或启动对象销毁时必须调用。</summary>
        public void Dispose()
        {
            m_Subscription?.Dispose();
            m_Subscription = null;
        }

        /// <summary>玩家名下容器发生变化时补扫一次图鉴。</summary>
        private void OnInventoryChanged(InventoryChangedEvent change)
        {
            if (m_Progress == null || m_Registry == null)
            {
                return;
            }

            if (!m_Registry.TryGetKind(change.ContainerId, out var kind))
            {
                return;
            }

            if (kind != ContainerKind.PlayerBackpack
                && kind != ContainerKind.Stash
                && kind != ContainerKind.AmmoPouch)
            {
                return;
            }

            m_Progress.RefreshCodex();
        }
    }
}
